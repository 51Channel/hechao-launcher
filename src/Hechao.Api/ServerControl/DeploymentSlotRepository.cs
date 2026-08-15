using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hechao.Api.Admin;
using Hechao.Api.PackageImports;
using Hechao.Contracts;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Hechao.Api.ServerControl;

public enum DeploymentSlotCreateStatus
{
    Success,
    FeatureDisabled,
    AlreadyExists,
    TemplateNotFound,
    TemplateUnavailable,
    LimitReached
}

public sealed record DeploymentSlotCreateResult(
    DeploymentSlotCreateStatus Status,
    AdminDeploymentSlotQueueResult? Result = null);

public sealed class DeploymentSlotRepository(
    NpgsqlDataSource dataSource,
    IOptions<ServerControlOptions> options)
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly ServerControlOptions controlOptions = options.Value;

    public async Task<DeploymentSlotCreateResult> CreateAsync(
        AdminCreateDeploymentSlotRequest request,
        Guid actorUserId,
        IPAddress? sourceIp,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!controlOptions.Enabled)
        {
            return new(DeploymentSlotCreateStatus.FeatureDisabled);
        }

        await using var connection =
            await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable,
            cancellationToken);

        var serverId = request.ServerId.Trim();
        if (await TargetExistsAsync(
                connection,
                transaction,
                serverId,
                cancellationToken))
        {
            return new(DeploymentSlotCreateStatus.AlreadyExists);
        }

        var template = await ReadTemplateAsync(
            connection,
            transaction,
            request.TemplateServerId.Trim(),
            cancellationToken);
        if (template is null)
        {
            return new(DeploymentSlotCreateStatus.TemplateNotFound);
        }

        if (!template.PackageDeploymentEnabled ||
            !PackageImportRules.IsPackageDeploymentTarget(
                template.AgentId,
                template.ConflictGroup,
                template.Port) ||
            now - template.LastSeenAt > TimeSpan.FromSeconds(
                controlOptions.AgentFreshnessSeconds))
        {
            return new(DeploymentSlotCreateStatus.TemplateUnavailable);
        }

        if (await CountDynamicSlotsAsync(
                connection,
                transaction,
                template.AgentId,
                cancellationToken) >=
            DeploymentSlotRules.MaximumDynamicSlotsPerAgent)
        {
            return new(DeploymentSlotCreateStatus.LimitReached);
        }

        var operationId = Guid.NewGuid();
        var commandId = Guid.NewGuid();
        var displayName = request.DisplayName.Trim();
        var reason = request.Reason.Trim();
        var provisioning = new ServerDeploymentSlotProvisioningRequest(
            serverId,
            displayName,
            template.ServerId);

        await InsertPlaceholderTargetAsync(
            connection,
            transaction,
            serverId,
            template,
            now,
            cancellationToken);
        await InsertOperationAsync(
            connection,
            transaction,
            operationId,
            serverId,
            reason,
            actorUserId,
            now,
            cancellationToken);
        await InsertCommandAsync(
            connection,
            transaction,
            commandId,
            operationId,
            serverId,
            template.AgentId,
            provisioning,
            cancellationToken);
        await InsertSlotAsync(
            connection,
            transaction,
            operationId,
            provisioning,
            actorUserId,
            now,
            cancellationToken);
        await WriteAuditAsync(
            connection,
            transaction,
            actorUserId,
            sourceIp,
            operationId,
            provisioning,
            reason,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var operation = new AdminServerControlOperationRecord(
            operationId,
            serverId,
            displayName,
            ServerControlAction.CreateDeploymentSlot,
            ServerControlOperationStatus.Pending,
            reason,
            actorUserId,
            now,
            null,
            null,
            null,
            null,
            []);
        return new(
            DeploymentSlotCreateStatus.Success,
            new AdminDeploymentSlotQueueResult(
                operation,
                serverId,
                DeploymentSlotProvisioningStatus.Provisioning));
    }

    private static async Task<bool> TargetExistsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string serverId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT EXISTS (
                SELECT 1
                FROM launcher.server_control_targets
                WHERE server_id = $1
                UNION ALL
                SELECT 1
                FROM launcher.servers
                WHERE id = $1
            );
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(serverId);
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    private static async Task<SlotTemplate?> ReadTemplateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string serverId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT server_id, agent_id, agent_version, conflict_group, port,
                   settings::text, allowed_command_prefixes,
                   package_deployment_enabled, host_total_memory_mib,
                   last_seen_at
            FROM launcher.server_control_targets
            WHERE server_id = $1
            FOR SHARE;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(serverId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new SlotTemplate(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.GetInt32(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.GetFieldValue<string[]>(6),
            reader.GetBoolean(7),
            reader.IsDBNull(8) ? null : reader.GetInt32(8),
            new DateTimeOffset(reader.GetDateTime(9)));
    }

    private static async Task<int> CountDynamicSlotsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string agentId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT count(*)::integer
            FROM launcher.deployment_slots AS slot
            JOIN launcher.server_control_targets AS target
              ON target.server_id = slot.server_id
            WHERE target.agent_id = $1
              AND slot.status IN ('Provisioning', 'Ready');
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(agentId);
        return (int)(await command.ExecuteScalarAsync(cancellationToken) ?? 0);
    }

    private static async Task InsertPlaceholderTargetAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string serverId,
        SlotTemplate template,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO launcher.server_control_targets
                (server_id, agent_id, agent_version, conflict_group, port,
                 reported_online, process_id, settings,
                 allowed_command_prefixes, console_tail,
                 package_deployment_enabled, server_deletion_enabled,
                 server_files_present, deletion_cleanup_pending,
                 host_total_memory_mib, last_seen_at, updated_at)
            VALUES ($1, $2, $3, $4, $5, false, NULL, $6, $7, '', false,
                    false, false, false, $8, $9, $10);
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(serverId);
        command.Parameters.AddWithValue(template.AgentId);
        command.Parameters.AddWithValue(template.AgentVersion);
        AdminPostgresParameters.AddPositional(
            command.Parameters,
            NpgsqlDbType.Text,
            template.ConflictGroup);
        command.Parameters.AddWithValue(template.Port);
        AdminPostgresParameters.AddPositional(
            command.Parameters,
            NpgsqlDbType.Jsonb,
            template.SettingsJson);
        command.Parameters.AddWithValue(
            NpgsqlDbType.Array | NpgsqlDbType.Text,
            template.AllowedCommandPrefixes);
        AdminPostgresParameters.AddPositional(
            command.Parameters,
            NpgsqlDbType.Integer,
            template.HostTotalMemoryMiB);
        command.Parameters.AddWithValue(DateTimeOffset.UnixEpoch);
        command.Parameters.AddWithValue(now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertOperationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid operationId,
        string serverId,
        string reason,
        Guid actorUserId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO launcher.server_control_operations
                (id, target_server_id, action, status, reason, requested_by,
                 requested_at, automatically_stopping_server_ids)
            VALUES ($1, $2, 'CreateDeploymentSlot', 'Pending', $3, $4, $5,
                    ARRAY[]::text[]);
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(operationId);
        command.Parameters.AddWithValue(serverId);
        command.Parameters.AddWithValue(reason);
        command.Parameters.AddWithValue(actorUserId);
        command.Parameters.AddWithValue(now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertCommandAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid commandId,
        Guid operationId,
        string serverId,
        string agentId,
        ServerDeploymentSlotProvisioningRequest provisioning,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO launcher.server_control_commands
                (id, operation_id, sequence, server_id, agent_id, kind, payload)
            VALUES ($1, $2, 0, $3, $4, 'CreateDeploymentSlot', $5);
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(commandId);
        command.Parameters.AddWithValue(operationId);
        command.Parameters.AddWithValue(serverId);
        command.Parameters.AddWithValue(agentId);
        command.Parameters.AddWithValue(
            NpgsqlDbType.Jsonb,
            JsonSerializer.Serialize(
                new CommandPayload(SlotProvisioning: provisioning),
                JsonOptions));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertSlotAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid operationId,
        ServerDeploymentSlotProvisioningRequest provisioning,
        Guid actorUserId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO launcher.deployment_slots
                (server_id, display_name, template_server_id, status,
                 operation_id, created_by, created_at, updated_at)
            VALUES ($1, $2, $3, 'Provisioning', $4, $5, $6, $6);
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(provisioning.ServerId);
        command.Parameters.AddWithValue(provisioning.DisplayName);
        command.Parameters.AddWithValue(provisioning.TemplateServerId);
        command.Parameters.AddWithValue(operationId);
        command.Parameters.AddWithValue(actorUserId);
        command.Parameters.AddWithValue(now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task WriteAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid actorUserId,
        IPAddress? sourceIp,
        Guid operationId,
        ServerDeploymentSlotProvisioningRequest provisioning,
        string reason,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO launcher.audit_logs
                (actor_user_id, action, target_type, target_id, source_ip,
                 before_data, after_data)
            VALUES ($1, 'server_control.deployment_slot.queued',
                    'deployment_slot', $2, $3, NULL, $4);
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(actorUserId);
        command.Parameters.AddWithValue(provisioning.ServerId);
        AdminPostgresParameters.AddPositional(
            command.Parameters,
            NpgsqlDbType.Inet,
            sourceIp);
        command.Parameters.AddWithValue(
            NpgsqlDbType.Jsonb,
            JsonSerializer.Serialize(new
            {
                operationId,
                provisioning.ServerId,
                provisioning.DisplayName,
                provisioning.TemplateServerId,
                reason
            }, JsonOptions));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private sealed record SlotTemplate(
        string ServerId,
        string AgentId,
        string AgentVersion,
        string? ConflictGroup,
        int Port,
        string? SettingsJson,
        string[] AllowedCommandPrefixes,
        bool PackageDeploymentEnabled,
        int? HostTotalMemoryMiB,
        DateTimeOffset LastSeenAt);

    private sealed record CommandPayload(
        string? ConsoleCommand = null,
        ServerQuickSettings? Settings = null,
        ServerPackageDeploymentRequest? PackageDeployment = null,
        ServerDeploymentSlotProvisioningRequest? SlotProvisioning = null);
}

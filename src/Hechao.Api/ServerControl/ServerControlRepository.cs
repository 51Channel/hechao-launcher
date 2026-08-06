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

public enum ServerControlQueueStatus
{
    Success,
    FeatureDisabled,
    TargetNotFound,
    AgentUnavailable,
    StateStale,
    OperationInProgress,
    CommandNotAllowed,
    TargetOffline,
    TargetOnline,
    TargetFilesMissing,
    ServerDeletionDisabled
}

public enum ServerControlCompletionStatus
{
    Success,
    CommandNotFound,
    ClaimConflict
}

public sealed record ServerControlQueueMutationResult(
    ServerControlQueueStatus Status,
    AdminServerControlQueueResult? Result = null,
    IReadOnlyList<string>? BlockingServerIds = null);

public sealed record ServerControlCompletionResult(
    ServerControlCompletionStatus Status,
    AdminServerControlOperationRecord? Operation = null);

public sealed class ServerControlRepository(
    NpgsqlDataSource dataSource,
    IOptions<ServerControlOptions> options)
{
    private static readonly JsonSerializerOptions JsonOptions =
        CreateJsonOptions();
    private readonly ServerControlOptions _options = options.Value;

    public async Task<ServerControlAgentHeartbeatResponse> ImportHeartbeatAsync(
        ServerControlAgentHeartbeatRequest request,
        DateTimeOffset receivedAt,
        CancellationToken cancellationToken)
    {
        var imported = 0;
        await using var connection =
            await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction =
            await connection.BeginTransactionAsync(cancellationToken);
        foreach (var target in request.Targets)
        {
            const string sql = """
                INSERT INTO launcher.server_control_targets
                    (server_id, agent_id, agent_version, conflict_group, port,
                     reported_online, process_id, settings,
                     allowed_command_prefixes, console_tail,
                     console_captured_at, package_deployment_enabled,
                     server_deletion_enabled, server_files_present,
                     deletion_cleanup_pending, host_total_memory_mib,
                     last_seen_at, updated_at)
                VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12,
                        $13, $14, $15, $16, $17, $17)
                ON CONFLICT (server_id) DO UPDATE
                SET agent_id = EXCLUDED.agent_id,
                    agent_version = EXCLUDED.agent_version,
                    conflict_group = EXCLUDED.conflict_group,
                    port = EXCLUDED.port,
                    reported_online = EXCLUDED.reported_online,
                    process_id = EXCLUDED.process_id,
                    settings = EXCLUDED.settings,
                    allowed_command_prefixes = EXCLUDED.allowed_command_prefixes,
                    console_tail = EXCLUDED.console_tail,
                    console_captured_at = EXCLUDED.console_captured_at,
                    package_deployment_enabled =
                        EXCLUDED.package_deployment_enabled,
                    server_deletion_enabled =
                        EXCLUDED.server_deletion_enabled,
                    server_files_present = EXCLUDED.server_files_present,
                    deletion_cleanup_pending =
                        EXCLUDED.deletion_cleanup_pending,
                    host_total_memory_mib = EXCLUDED.host_total_memory_mib,
                    last_seen_at = EXCLUDED.last_seen_at,
                    updated_at = EXCLUDED.updated_at
                WHERE
                    launcher.server_control_targets.agent_id = EXCLUDED.agent_id
                    OR launcher.server_control_targets.last_seen_at <
                       EXCLUDED.last_seen_at - interval '5 minutes';
                """;
            await using var command =
                new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue(target.ServerId);
            command.Parameters.AddWithValue(request.AgentId);
            command.Parameters.AddWithValue(request.AgentVersion.Trim());
            AdminPostgresParameters.AddPositional(
                command.Parameters,
                NpgsqlDbType.Text,
                target.ConflictGroup);
            command.Parameters.AddWithValue(target.Port);
            command.Parameters.AddWithValue(target.Online);
            AdminPostgresParameters.AddPositional(
                command.Parameters,
                NpgsqlDbType.Integer,
                target.ProcessId);
            AdminPostgresParameters.AddPositional(
                command.Parameters,
                NpgsqlDbType.Jsonb,
                target.Settings is null
                    ? null
                    : JsonSerializer.Serialize(target.Settings, JsonOptions));
            command.Parameters.AddWithValue(
                NpgsqlDbType.Array | NpgsqlDbType.Text,
                target.AllowedCommandPrefixes.ToArray());
            command.Parameters.AddWithValue(target.ConsoleTail);
            AdminPostgresParameters.AddPositional(
                command.Parameters,
                NpgsqlDbType.TimestampTz,
                target.ConsoleCapturedAt);
            command.Parameters.AddWithValue(target.PackageDeploymentEnabled);
            command.Parameters.AddWithValue(target.ServerDeletionEnabled);
            command.Parameters.AddWithValue(target.ServerFilesPresent);
            command.Parameters.AddWithValue(target.DeletionCleanupPending);
            AdminPostgresParameters.AddPositional(
                command.Parameters,
                NpgsqlDbType.Integer,
                request.HostTotalMemoryMiB);
            command.Parameters.AddWithValue(receivedAt);
            imported += await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var activeDeploymentCommandIds =
            request.ActiveDeploymentCommandIds?.ToArray() ?? [];
        await using (var renewDeployments = new NpgsqlCommand(
                         """
                         UPDATE launcher.server_control_commands
                         SET claim_expires_at = CASE
                                 WHEN id = ANY($3) THEN $4
                                 ELSE LEAST(claim_expires_at, $2)
                             END
                         WHERE agent_id = $1
                           AND claimed_by = $1
                           AND status = 'Claimed'
                           AND kind = 'DeployPackage';
                         """,
                         connection,
                         transaction))
        {
            renewDeployments.Parameters.AddWithValue(request.AgentId);
            renewDeployments.Parameters.AddWithValue(
                receivedAt.AddSeconds(_options.ClaimLeaseSeconds));
            renewDeployments.Parameters.AddWithValue(
                NpgsqlDbType.Array | NpgsqlDbType.Uuid,
                activeDeploymentCommandIds);
            renewDeployments.Parameters.AddWithValue(
                receivedAt.AddMinutes(
                    _options.PackageDeploymentClaimLeaseMinutes));
            await renewDeployments.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new ServerControlAgentHeartbeatResponse(imported, receivedAt);
    }

    public async Task<AdminServerControlOverview> GetOverviewAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken,
        bool includeDeletedTargets = false)
    {
        await using var connection =
            await dataSource.OpenConnectionAsync(cancellationToken);
        var operations = await ReadActiveOperationsAsync(
            connection,
            cancellationToken);
        var activeByTarget = operations
            .GroupBy(operation => operation.ServerId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(item => item.RequestedAt).First(),
                StringComparer.Ordinal);
        var targets = new List<AdminServerControlTargetSummaryRecord>();
        const string sql = """
            SELECT target.server_id,
                   COALESCE(server.display_name, target.server_id),
                   target.agent_id,
                   target.conflict_group,
                   target.port,
                   target.last_seen_at,
                   target.reported_online,
                   target.process_id,
                   target.settings::text,
                   target.package_deployment_enabled,
                   target.server_deletion_enabled,
                   target.server_files_present,
                   target.deletion_cleanup_pending,
                   target.host_total_memory_mib
            FROM launcher.server_control_targets AS target
            LEFT JOIN launcher.servers AS server ON server.id = target.server_id
            ORDER BY COALESCE(server.sort_order, 2147483647),
                     COALESCE(server.display_name, target.server_id),
                     target.server_id;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var serverId = reader.GetString(0);
            var lastSeenAt = new DateTimeOffset(reader.GetDateTime(5));
            var serverFilesPresent = reader.GetBoolean(11);
            var deletionCleanupPending = reader.GetBoolean(12);
            var agentId = reader.GetString(2);
            var conflictGroup = reader.IsDBNull(3) ? null : reader.GetString(3);
            var port = reader.GetInt32(4);
            var settings = reader.IsDBNull(8)
                ? null
                : JsonSerializer.Deserialize<ServerQuickSettings>(
                    reader.GetString(8),
                    JsonOptions);
            var packageDeploymentEnabled = reader.GetBoolean(9);
            var activeOperation = activeByTarget.GetValueOrDefault(serverId);
            if (!ServerControlTargetVisibility.IncludeInOverview(
                    serverFilesPresent,
                    deletionCleanupPending,
                    activeOperation is not null,
                    includeDeletedTargets))
            {
                continue;
            }

            targets.Add(new AdminServerControlTargetSummaryRecord(
                serverId,
                reader.GetString(1),
                agentId,
                conflictGroup,
                port,
                now - lastSeenAt <=
                    TimeSpan.FromSeconds(_options.AgentFreshnessSeconds),
                lastSeenAt,
                reader.GetBoolean(6),
                reader.IsDBNull(7) ? null : reader.GetInt32(7),
                settings,
                activeOperation,
                packageDeploymentEnabled,
                reader.GetBoolean(10),
                serverFilesPresent,
                deletionCleanupPending,
                PackageImportRules.ResolvePackageDeploymentMemoryGuidance(
                    serverId,
                    agentId,
                    conflictGroup,
                    port,
                    packageDeploymentEnabled,
                    reader.IsDBNull(13) ? null : reader.GetInt32(13))));
        }

        return new AdminServerControlOverview(
            now,
            _options.AgentFreshnessSeconds,
            targets);
    }

    public async Task<AdminServerControlTargetDetail?> GetTargetDetailAsync(
        string serverId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var connection =
            await dataSource.OpenConnectionAsync(cancellationToken);
        var operations = await ReadRecentOperationsAsync(
            connection,
            serverId,
            limit: 20,
            cancellationToken);
        var activeOperation = operations.FirstOrDefault(operation =>
            operation.Status is ServerControlOperationStatus.Pending or
                ServerControlOperationStatus.Running);
        const string sql = """
            SELECT target.server_id,
                   COALESCE(server.display_name, target.server_id),
                   target.agent_id,
                   target.conflict_group,
                   target.port,
                   target.last_seen_at,
                   target.reported_online,
                   target.process_id,
                   target.settings::text,
                   target.allowed_command_prefixes,
                   target.console_tail,
                   target.console_captured_at,
                   target.package_deployment_enabled,
                   target.server_deletion_enabled,
                   target.server_files_present,
                   target.deletion_cleanup_pending,
                   target.host_total_memory_mib
            FROM launcher.server_control_targets AS target
            LEFT JOIN launcher.servers AS server ON server.id = target.server_id
            WHERE target.server_id = $1;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(serverId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var lastSeenAt = new DateTimeOffset(reader.GetDateTime(5));
        var targetServerId = reader.GetString(0);
        var targetAgentId = reader.GetString(2);
        var targetConflictGroup = reader.IsDBNull(3) ? null : reader.GetString(3);
        var targetPort = reader.GetInt32(4);
        var targetSettings = reader.IsDBNull(8)
            ? null
            : JsonSerializer.Deserialize<ServerQuickSettings>(
                reader.GetString(8),
                JsonOptions);
        var packageDeploymentEnabled = reader.GetBoolean(12);
        var serverFilesPresent = reader.GetBoolean(14);
        var target = new AdminServerControlTargetRecord(
            targetServerId,
            reader.GetString(1),
            targetAgentId,
            targetConflictGroup,
            targetPort,
            now - lastSeenAt <=
                TimeSpan.FromSeconds(_options.AgentFreshnessSeconds),
            lastSeenAt,
            reader.GetBoolean(6),
            reader.IsDBNull(7) ? null : reader.GetInt32(7),
            targetSettings,
            reader.GetFieldValue<string[]>(9),
            reader.GetString(10),
            reader.IsDBNull(11)
                ? null
                : new DateTimeOffset(reader.GetDateTime(11)),
            activeOperation,
            packageDeploymentEnabled,
            reader.GetBoolean(13),
            serverFilesPresent,
            reader.GetBoolean(15),
            PackageImportRules.ResolvePackageDeploymentMemoryGuidance(
                targetServerId,
                targetAgentId,
                targetConflictGroup,
                targetPort,
                packageDeploymentEnabled,
                reader.IsDBNull(16) ? null : reader.GetInt32(16)));
        return new AdminServerControlTargetDetail(
            now,
            _options.AgentFreshnessSeconds,
            target,
            operations);
    }

    public async Task<ServerControlQueueMutationResult> QueueAsync(
        string serverId,
        AdminServerControlRequest request,
        Guid actorUserId,
        IPAddress? sourceIp,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return new ServerControlQueueMutationResult(
                ServerControlQueueStatus.FeatureDisabled);
        }

        await using var connection =
            await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction =
            await connection.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable,
                cancellationToken);
        var target = await ReadTargetForUpdateAsync(
            connection,
            transaction,
            serverId,
            cancellationToken);
        if (target is null)
        {
            return new ServerControlQueueMutationResult(
                ServerControlQueueStatus.TargetNotFound);
        }

        if (!IsFresh(target.LastSeenAt, now))
        {
            return new ServerControlQueueMutationResult(
                ServerControlQueueStatus.AgentUnavailable);
        }

        var affectedTargets = new List<ControlTarget> { target };
        var automaticallyStopping = new List<string>();
        if ((request.Action is ServerControlAction.Start or
                ServerControlAction.Restart) &&
            target.ConflictGroup is not null)
        {
            var conflicts = await ReadConflictTargetsForUpdateAsync(
                connection,
                transaction,
                target.ConflictGroup,
                serverId,
                cancellationToken);
            if (conflicts.Any(conflict => !IsFresh(conflict.LastSeenAt, now)))
            {
                return new ServerControlQueueMutationResult(
                    ServerControlQueueStatus.StateStale,
                    BlockingServerIds: conflicts
                        .Where(conflict => !IsFresh(conflict.LastSeenAt, now))
                        .Select(conflict => conflict.ServerId)
                        .ToArray());
            }

            var onlineConflicts = conflicts
                .Where(conflict => conflict.Online)
                .ToArray();
            affectedTargets.AddRange(onlineConflicts);
            automaticallyStopping.AddRange(
                onlineConflicts.Select(conflict => conflict.ServerId));
        }

        var affectedIds = affectedTargets
            .Select(item => item.ServerId)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (await HasActiveCommandsAsync(
                connection,
                transaction,
                affectedIds,
                cancellationToken))
        {
            return new ServerControlQueueMutationResult(
                ServerControlQueueStatus.OperationInProgress,
                BlockingServerIds: affectedIds);
        }

        if (request.Action == ServerControlAction.ConsoleCommand)
        {
            if (!target.Online)
            {
                return new ServerControlQueueMutationResult(
                    ServerControlQueueStatus.TargetOffline);
            }

            if (!IsCommandAllowed(
                    request.ConsoleCommand!,
                    target.AllowedCommandPrefixes))
            {
                return new ServerControlQueueMutationResult(
                    ServerControlQueueStatus.CommandNotAllowed);
            }
        }

        if ((request.Action is ServerControlAction.Start or
                ServerControlAction.Restart or
                ServerControlAction.ApplySettings) &&
            !target.ServerFilesPresent)
        {
            return new ServerControlQueueMutationResult(
                ServerControlQueueStatus.TargetFilesMissing);
        }

        if (request.Action == ServerControlAction.DeleteServerFiles)
        {
            if (!target.ServerDeletionEnabled)
            {
                return new ServerControlQueueMutationResult(
                    ServerControlQueueStatus.ServerDeletionDisabled);
            }

            if (target.Online)
            {
                return new ServerControlQueueMutationResult(
                    ServerControlQueueStatus.TargetOnline);
            }
        }

        var operationId = Guid.NewGuid();
        var immediateCode = GetImmediateResultCode(
            request.Action,
            target.Online,
            automaticallyStopping.Count);
        var operationStatus = immediateCode is null
            ? ServerControlOperationStatus.Pending
            : ServerControlOperationStatus.Succeeded;
        var operation = await InsertOperationAsync(
            connection,
            transaction,
            operationId,
            target,
            request,
            operationStatus,
            actorUserId,
            sourceIp,
            automaticallyStopping,
            immediateCode,
            now,
            cancellationToken);

        if (immediateCode is null)
        {
            await InsertCommandsAsync(
                connection,
                transaction,
                operationId,
                target,
                affectedTargets,
                request,
                cancellationToken);
        }

        await WriteAuditAsync(
            connection,
            transaction,
            actorUserId,
            sourceIp,
            "server_control.operation.queued",
            operationId.ToString("D"),
            new
            {
                request.Action,
                ServerId = serverId,
                AutomaticallyStoppingServerIds = automaticallyStopping
            },
            operation,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var result = new AdminServerControlQueueResult(
            operation,
            automaticallyStopping);
        return new ServerControlQueueMutationResult(
            ServerControlQueueStatus.Success,
            result);
    }

    public async Task<ServerControlCommandClaimResponse> ClaimAsync(
        string agentId,
        int limit,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var leaseExpiresAt = now.AddSeconds(_options.ClaimLeaseSeconds);
        var deploymentLeaseExpiresAt = now.AddMinutes(
            _options.PackageDeploymentClaimLeaseMinutes);
        const string sql = """
            WITH due AS (
                SELECT command.id
                FROM launcher.server_control_commands AS command
                JOIN launcher.server_control_operations AS operation
                  ON operation.id = command.operation_id
                WHERE command.agent_id = $1
                  AND operation.status IN ('Pending', 'Running')
                  AND (
                      command.status = 'Pending'
                      OR (
                          command.status = 'Claimed'
                          AND command.claim_expires_at <= $2
                      )
                  )
                  AND NOT EXISTS (
                      SELECT 1
                      FROM launcher.server_control_commands AS predecessor
                      WHERE predecessor.operation_id = command.operation_id
                        AND predecessor.sequence < command.sequence
                        AND predecessor.status <> 'Succeeded'
                  )
                ORDER BY operation.requested_at, command.sequence, command.id
                LIMIT $3
                FOR UPDATE OF command SKIP LOCKED
            ),
            claimed AS (
                UPDATE launcher.server_control_commands AS command
                SET status = 'Claimed',
                    claimed_by = $1,
                    claimed_at = $2,
                    claim_expires_at = CASE
                        WHEN command.kind = 'DeployPackage' THEN $5
                        ELSE $4
                    END,
                    attempt_count = command.attempt_count + 1,
                    completed_at = NULL,
                    result_code = NULL,
                    result_message = NULL
                FROM due
                WHERE command.id = due.id
                RETURNING command.id,
                          command.operation_id,
                          command.server_id,
                          command.kind,
                          command.attempt_count,
                          command.payload::text
            )
            SELECT id, operation_id, server_id, kind, attempt_count, payload
            FROM claimed;
            """;
        var deliveries = new List<ServerControlCommandDelivery>();
        await using var connection =
            await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction =
            await connection.BeginTransactionAsync(cancellationToken);
        await using (var command = new NpgsqlCommand(sql, connection, transaction))
        {
            command.Parameters.AddWithValue(agentId);
            command.Parameters.AddWithValue(now);
            command.Parameters.AddWithValue(limit);
            command.Parameters.AddWithValue(leaseExpiresAt);
            command.Parameters.AddWithValue(deploymentLeaseExpiresAt);
            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var payload = JsonSerializer.Deserialize<CommandPayload>(
                    reader.GetString(5),
                    JsonOptions) ?? new CommandPayload();
                deliveries.Add(new ServerControlCommandDelivery(
                    reader.GetGuid(0),
                    reader.GetGuid(1),
                    reader.GetString(2),
                    Enum.Parse<ServerControlCommandKind>(
                        reader.GetString(3),
                        ignoreCase: true),
                    reader.GetInt32(4),
                    payload.ConsoleCommand,
                    payload.Settings,
                    payload.PackageDeployment));
            }
        }

        if (deliveries.Count > 0)
        {
            await using var markRunning = new NpgsqlCommand(
                """
                UPDATE launcher.server_control_operations
                SET status = 'Running',
                    started_at = COALESCE(started_at, $1)
                WHERE id = ANY($2)
                  AND status = 'Pending';
                """,
                connection,
                transaction);
            markRunning.Parameters.AddWithValue(now);
            markRunning.Parameters.AddWithValue(
                NpgsqlDbType.Array | NpgsqlDbType.Uuid,
                deliveries.Select(item => item.OperationId).Distinct().ToArray());
            await markRunning.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new ServerControlCommandClaimResponse(deliveries, now);
    }

    public async Task<ServerControlCompletionResult> CompleteAsync(
        Guid commandId,
        ServerControlCommandCompletionRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var connection =
            await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction =
            await connection.BeginTransactionAsync(cancellationToken);
        Guid operationId;
        await using (var read = new NpgsqlCommand(
                         """
                         SELECT operation_id, status, claimed_by, attempt_count
                         FROM launcher.server_control_commands
                         WHERE id = $1
                         FOR UPDATE;
                         """,
                         connection,
                         transaction))
        {
            read.Parameters.AddWithValue(commandId);
            await using var reader =
                await read.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return new ServerControlCompletionResult(
                    ServerControlCompletionStatus.CommandNotFound);
            }

            operationId = reader.GetGuid(0);
            if (!string.Equals(reader.GetString(1), "Claimed", StringComparison.Ordinal) ||
                reader.IsDBNull(2) ||
                !string.Equals(
                    reader.GetString(2),
                    request.AgentId,
                    StringComparison.Ordinal) ||
                reader.GetInt32(3) != request.AttemptCount)
            {
                return new ServerControlCompletionResult(
                    ServerControlCompletionStatus.ClaimConflict);
            }
        }

        var succeeded = request.Outcome == ServerControlCommandOutcome.Succeeded;
        await using (var update = new NpgsqlCommand(
                         """
                         UPDATE launcher.server_control_commands
                         SET status = $2,
                             completed_at = $3,
                             claim_expires_at = NULL,
                             result_code = $4,
                             result_message = $5
                         WHERE id = $1;
                         """,
                         connection,
                         transaction))
        {
            update.Parameters.AddWithValue(commandId);
            update.Parameters.AddWithValue(succeeded ? "Succeeded" : "Failed");
            update.Parameters.AddWithValue(now);
            update.Parameters.AddWithValue(request.ResultCode);
            update.Parameters.AddWithValue(request.ResultMessage.Trim());
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        var becameTerminal = false;
        if (!succeeded)
        {
            await using var cancel = new NpgsqlCommand(
                """
                UPDATE launcher.server_control_commands
                SET status = 'Cancelled',
                    completed_at = $2,
                    result_code = 'DEPENDENCY_FAILED',
                    result_message = '前置控制动作失败，后续动作未执行。'
                WHERE operation_id = $1
                  AND status = 'Pending';
                """,
                connection,
                transaction);
            cancel.Parameters.AddWithValue(operationId);
            cancel.Parameters.AddWithValue(now);
            await cancel.ExecuteNonQueryAsync(cancellationToken);
            becameTerminal = await UpdateOperationTerminalStateAsync(
                connection,
                transaction,
                operationId,
                ServerControlOperationStatus.Failed,
                request.ResultCode,
                request.ResultMessage.Trim(),
                now,
                cancellationToken);
        }
        else if (await AllCommandsSucceededAsync(
                     connection,
                     transaction,
                     operationId,
                     cancellationToken))
        {
            becameTerminal = await UpdateOperationTerminalStateAsync(
                connection,
                transaction,
                operationId,
                ServerControlOperationStatus.Succeeded,
                "SUCCEEDED",
                "所有控制动作已完成。",
                now,
                cancellationToken);
        }

        var operation = await ReadOperationAsync(
            connection,
            transaction,
            operationId,
            cancellationToken);
        if (becameTerminal &&
            operation is not null &&
            operation.Status is ServerControlOperationStatus.Succeeded or
                ServerControlOperationStatus.Failed)
        {
            await WriteAuditAsync(
                connection,
                transaction,
                operation.RequestedBy,
                null,
                "server_control.operation.completed",
                operation.OperationId.ToString("D"),
                null,
                operation,
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new ServerControlCompletionResult(
            ServerControlCompletionStatus.Success,
            operation);
    }

    public async Task<AdminServerControlOperationRecord?> GetOperationAsync(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        await using var connection =
            await dataSource.OpenConnectionAsync(cancellationToken);
        return await ReadOperationAsync(
            connection,
            transaction: null,
            operationId,
            cancellationToken);
    }

    public async Task<Guid?> GetAuthorizedPackageArchiveImportIdAsync(
        Guid commandId,
        string agentId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT package.id,
                   package.analysis::text,
                   package.plan::text,
                   command.payload::text,
                   command.server_id,
                   target.server_id,
                   target.agent_id,
                   target.conflict_group,
                   target.port
            FROM launcher.server_control_commands AS command
            JOIN launcher.package_imports AS package
              ON package.deployment_operation_id = command.operation_id
            JOIN launcher.server_control_targets AS target
              ON target.server_id = command.server_id
            WHERE command.id = $1
              AND command.agent_id = $2
              AND command.claimed_by = $2
              AND command.status = 'Claimed'
              AND command.kind = 'DeployPackage'
              AND command.claim_expires_at >= $3
              AND package.status = 'DeployingServer'
              AND target.package_deployment_enabled;
            """;
        await using var connection =
            await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(commandId);
        command.Parameters.AddWithValue(agentId);
        command.Parameters.AddWithValue(now);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var importId = reader.GetGuid(0);
        var analysis = JsonSerializer.Deserialize<PackageImportAnalysisRecord>(
            reader.GetString(1),
            JsonOptions);
        var plan = JsonSerializer.Deserialize<PackageImportDeploymentPlanRecord>(
            reader.GetString(2),
            JsonOptions);
        var payload = JsonSerializer.Deserialize<CommandPayload>(
            reader.GetString(3),
            JsonOptions);
        var deployment = payload?.PackageDeployment;
        var server = analysis?.Server;
        return PackageImportRules.IsActivityTarget(
                   reader.GetString(5),
                   reader.GetString(6),
                   reader.IsDBNull(7) ? null : reader.GetString(7),
                   reader.GetInt32(8)) &&
               deployment is not null &&
               server is not null &&
               plan is not null &&
               deployment.ImportId == importId &&
               string.Equals(
                   deployment.ProfileId,
                   plan.ProfileId,
                   StringComparison.Ordinal) &&
               string.Equals(
                   deployment.Version,
                   plan.Version,
                   StringComparison.Ordinal) &&
               string.Equals(
                   plan.TargetServerId,
                   reader.GetString(4),
                   StringComparison.Ordinal) &&
               deployment.ArchiveBytes == server.ArchiveBytes &&
               string.Equals(
                   deployment.ArchiveSha256,
                   server.Sha256,
                   StringComparison.Ordinal) &&
               deployment.ExpandedBytes == server.ExpandedBytes &&
               deployment.FileCount == server.FileCount &&
               deployment.PreserveWorldData == plan.PreserveWorldData &&
               deployment.MaximumMemoryMiB == plan.MaximumMemoryMiB
            ? importId
            : null;
    }

    private bool IsFresh(DateTimeOffset lastSeenAt, DateTimeOffset now) =>
        now - lastSeenAt <=
        TimeSpan.FromSeconds(_options.AgentFreshnessSeconds);

    private static string? GetImmediateResultCode(
        ServerControlAction action,
        bool online,
        int automaticallyStoppingCount) =>
        action switch
        {
            ServerControlAction.Start
                when online && automaticallyStoppingCount == 0 =>
                "ALREADY_RUNNING",
            ServerControlAction.Stop when !online => "ALREADY_STOPPED",
            ServerControlAction.DeleteServerFiles when !online => null,
            _ => null
        };

    private static bool IsCommandAllowed(
        string command,
        IReadOnlyList<string> allowedPrefixes)
    {
        var normalized = command.Trim().TrimStart('/');
        var separator = normalized.IndexOfAny([' ', '\t']);
        var prefix = (separator < 0 ? normalized : normalized[..separator])
            .ToLowerInvariant();
        return allowedPrefixes.Contains(prefix, StringComparer.Ordinal);
    }

    private static async Task<ControlTarget?> ReadTargetForUpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string serverId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT target.server_id,
                   COALESCE(server.display_name, target.server_id),
                   target.agent_id,
                   target.conflict_group,
                   target.reported_online,
                   target.allowed_command_prefixes,
                   target.last_seen_at,
                   target.server_deletion_enabled,
                   target.server_files_present
            FROM launcher.server_control_targets AS target
            LEFT JOIN launcher.servers AS server ON server.id = target.server_id
            WHERE target.server_id = $1
            FOR UPDATE OF target;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(serverId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadControlTarget(reader)
            : null;
    }

    private static async Task<IReadOnlyList<ControlTarget>>
        ReadConflictTargetsForUpdateAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            string conflictGroup,
            string excludedServerId,
            CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT target.server_id,
                   COALESCE(server.display_name, target.server_id),
                   target.agent_id,
                   target.conflict_group,
                   target.reported_online,
                   target.allowed_command_prefixes,
                   target.last_seen_at,
                   target.server_deletion_enabled,
                   target.server_files_present
            FROM launcher.server_control_targets AS target
            LEFT JOIN launcher.servers AS server ON server.id = target.server_id
            WHERE target.conflict_group = $1
              AND target.server_id <> $2
            ORDER BY target.server_id
            FOR UPDATE OF target;
            """;
        var result = new List<ControlTarget>();
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(conflictGroup);
        command.Parameters.AddWithValue(excludedServerId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(ReadControlTarget(reader));
        }

        return result;
    }

    private static ControlTarget ReadControlTarget(NpgsqlDataReader reader) =>
        new(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.GetBoolean(4),
            reader.GetFieldValue<string[]>(5),
            new DateTimeOffset(reader.GetDateTime(6)),
            reader.GetBoolean(7),
            reader.GetBoolean(8));

    private static async Task<bool> HasActiveCommandsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string[] serverIds,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT 1
            FROM launcher.server_control_commands
            WHERE server_id = ANY($1)
              AND status IN ('Pending', 'Claimed')
            LIMIT 1;
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(
            NpgsqlDbType.Array | NpgsqlDbType.Text,
            serverIds);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private static async Task<AdminServerControlOperationRecord>
        InsertOperationAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            Guid operationId,
            ControlTarget target,
            AdminServerControlRequest request,
            ServerControlOperationStatus status,
            Guid actorUserId,
            IPAddress? sourceIp,
            IReadOnlyList<string> automaticallyStopping,
            string? immediateResultCode,
            DateTimeOffset now,
            CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO launcher.server_control_operations
                (id, target_server_id, action, status, reason, requested_by,
                 source_ip, requested_at, completed_at, result_code,
                 result_message, automatically_stopping_server_ids)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12);
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(operationId);
        command.Parameters.AddWithValue(target.ServerId);
        command.Parameters.AddWithValue(request.Action.ToString());
        command.Parameters.AddWithValue(status.ToString());
        command.Parameters.AddWithValue(request.Reason.Trim());
        command.Parameters.AddWithValue(actorUserId);
        AdminPostgresParameters.AddPositional(
            command.Parameters,
            NpgsqlDbType.Inet,
            sourceIp);
        command.Parameters.AddWithValue(now);
        AdminPostgresParameters.AddPositional(
            command.Parameters,
            NpgsqlDbType.TimestampTz,
            status == ServerControlOperationStatus.Succeeded ? now : null);
        AdminPostgresParameters.AddPositional(
            command.Parameters,
            NpgsqlDbType.Text,
            immediateResultCode);
        AdminPostgresParameters.AddPositional(
            command.Parameters,
            NpgsqlDbType.Text,
            immediateResultCode switch
            {
                "ALREADY_RUNNING" => "服务器已经在运行。",
                "ALREADY_STOPPED" => "服务器已经停止。",
                _ => null
            });
        command.Parameters.AddWithValue(
            NpgsqlDbType.Array | NpgsqlDbType.Text,
            automaticallyStopping.ToArray());
        await command.ExecuteNonQueryAsync(cancellationToken);
        return new AdminServerControlOperationRecord(
            operationId,
            target.ServerId,
            target.DisplayName,
            request.Action,
            status,
            request.Reason.Trim(),
            actorUserId,
            now,
            null,
            status == ServerControlOperationStatus.Succeeded ? now : null,
            immediateResultCode,
            immediateResultCode switch
            {
                "ALREADY_RUNNING" => "服务器已经在运行。",
                "ALREADY_STOPPED" => "服务器已经停止。",
                _ => null
            },
            automaticallyStopping);
    }

    private static async Task InsertCommandsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid operationId,
        ControlTarget target,
        IReadOnlyList<ControlTarget> affectedTargets,
        AdminServerControlRequest request,
        CancellationToken cancellationToken)
    {
        var planningTarget = new ServerControlPlanningTarget(
            target.ServerId,
            target.AgentId,
            target.Online);
        var commands = ServerControlCommandPlanner.Build(
            planningTarget,
            affectedTargets
                .Select(item => new ServerControlPlanningTarget(
                    item.ServerId,
                    item.AgentId,
                    item.Online))
                .ToArray(),
            request);

        const string sql = """
            INSERT INTO launcher.server_control_commands
                (id, operation_id, sequence, server_id, agent_id, kind, payload)
            VALUES ($1, $2, $3, $4, $5, $6, $7);
            """;
        foreach (var pending in commands)
        {
            await using var command =
                new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue(Guid.NewGuid());
            command.Parameters.AddWithValue(operationId);
            command.Parameters.AddWithValue(pending.Sequence);
            command.Parameters.AddWithValue(pending.ServerId);
            command.Parameters.AddWithValue(pending.AgentId);
            command.Parameters.AddWithValue(pending.Kind.ToString());
            command.Parameters.AddWithValue(
                NpgsqlDbType.Jsonb,
                JsonSerializer.Serialize(
                    new CommandPayload(
                        pending.ConsoleCommand,
                        pending.Settings),
                    JsonOptions));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task<bool> AllCommandsSucceededAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT bool_and(status = 'Succeeded')
            FROM launcher.server_control_commands
            WHERE operation_id = $1;
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(operationId);
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    private static async Task<bool> UpdateOperationTerminalStateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid operationId,
        ServerControlOperationStatus status,
        string resultCode,
        string resultMessage,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            UPDATE launcher.server_control_operations
            SET status = $2,
                completed_at = $3,
                result_code = $4,
                result_message = $5
            WHERE id = $1
              AND status IN ('Pending', 'Running');
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(operationId);
        command.Parameters.AddWithValue(status.ToString());
        command.Parameters.AddWithValue(now);
        command.Parameters.AddWithValue(resultCode);
        command.Parameters.AddWithValue(resultMessage);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private static async Task<IReadOnlyList<AdminServerControlOperationRecord>>
        ReadActiveOperationsAsync(
            NpgsqlConnection connection,
            CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT operation.id,
                   operation.target_server_id,
                   COALESCE(server.display_name, operation.target_server_id),
                   operation.action,
                   operation.status,
                   operation.reason,
                   operation.requested_by,
                   operation.requested_at,
                   operation.started_at,
                   operation.completed_at,
                   operation.result_code,
                   operation.result_message,
                   operation.automatically_stopping_server_ids
            FROM launcher.server_control_operations AS operation
            LEFT JOIN launcher.servers AS server
              ON server.id = operation.target_server_id
            WHERE operation.status IN ('Pending', 'Running')
            ORDER BY operation.requested_at DESC;
            """;
        var result = new List<AdminServerControlOperationRecord>();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(ReadOperation(reader));
        }

        return result;
    }

    private static async Task<IReadOnlyList<AdminServerControlOperationRecord>>
        ReadRecentOperationsAsync(
            NpgsqlConnection connection,
            string serverId,
            int limit,
            CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT operation.id,
                   operation.target_server_id,
                   COALESCE(server.display_name, operation.target_server_id),
                   operation.action,
                   operation.status,
                   operation.reason,
                   operation.requested_by,
                   operation.requested_at,
                   operation.started_at,
                   operation.completed_at,
                   operation.result_code,
                   operation.result_message,
                   operation.automatically_stopping_server_ids
            FROM launcher.server_control_operations AS operation
            LEFT JOIN launcher.servers AS server
              ON server.id = operation.target_server_id
            WHERE operation.target_server_id = $1
            ORDER BY operation.requested_at DESC
            LIMIT $2;
            """;
        var result = new List<AdminServerControlOperationRecord>();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(serverId);
        command.Parameters.AddWithValue(limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(ReadOperation(reader));
        }

        return result;
    }

    private static async Task<AdminServerControlOperationRecord?>
        ReadOperationAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction? transaction,
            Guid operationId,
            CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT operation.id,
                   operation.target_server_id,
                   COALESCE(server.display_name, operation.target_server_id),
                   operation.action,
                   operation.status,
                   operation.reason,
                   operation.requested_by,
                   operation.requested_at,
                   operation.started_at,
                   operation.completed_at,
                   operation.result_code,
                   operation.result_message,
                   operation.automatically_stopping_server_ids
            FROM launcher.server_control_operations AS operation
            LEFT JOIN launcher.servers AS server
              ON server.id = operation.target_server_id
            WHERE operation.id = $1;
            """;
        await using var command =
            new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(operationId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadOperation(reader)
            : null;
    }

    private static AdminServerControlOperationRecord ReadOperation(
        NpgsqlDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            Enum.Parse<ServerControlAction>(reader.GetString(3), ignoreCase: true),
            Enum.Parse<ServerControlOperationStatus>(
                reader.GetString(4),
                ignoreCase: true),
            reader.GetString(5),
            reader.GetGuid(6),
            new DateTimeOffset(reader.GetDateTime(7)),
            reader.IsDBNull(8)
                ? null
                : new DateTimeOffset(reader.GetDateTime(8)),
            reader.IsDBNull(9)
                ? null
                : new DateTimeOffset(reader.GetDateTime(9)),
            reader.IsDBNull(10) ? null : reader.GetString(10),
            reader.IsDBNull(11) ? null : reader.GetString(11),
            reader.GetFieldValue<string[]>(12));

    private static async Task WriteAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid actorUserId,
        IPAddress? sourceIp,
        string action,
        string targetId,
        object? before,
        object? after,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO launcher.audit_logs
                (actor_user_id, action, target_type, target_id, source_ip,
                 before_data, after_data)
            VALUES ($1, $2, 'server_control_operation', $3, $4, $5, $6);
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(actorUserId);
        command.Parameters.AddWithValue(action);
        command.Parameters.AddWithValue(targetId);
        AdminPostgresParameters.AddPositional(
            command.Parameters,
            NpgsqlDbType.Inet,
            sourceIp);
        AdminPostgresParameters.AddPositional(
            command.Parameters,
            NpgsqlDbType.Jsonb,
            before is null
                ? null
                : JsonSerializer.Serialize(before, JsonOptions));
        AdminPostgresParameters.AddPositional(
            command.Parameters,
            NpgsqlDbType.Jsonb,
            after is null
                ? null
                : JsonSerializer.Serialize(after, JsonOptions));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private sealed record ControlTarget(
        string ServerId,
        string DisplayName,
        string AgentId,
        string? ConflictGroup,
        bool Online,
        IReadOnlyList<string> AllowedCommandPrefixes,
        DateTimeOffset LastSeenAt,
        bool ServerDeletionEnabled,
        bool ServerFilesPresent);

    private sealed record CommandPayload(
        string? ConsoleCommand = null,
        ServerQuickSettings? Settings = null,
        ServerPackageDeploymentRequest? PackageDeployment = null);

}

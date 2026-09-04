using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hechao.Api.Catalog;
using Hechao.Api.ServerControl;
using Hechao.Api.Velocity;
using Hechao.Contracts;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Hechao.Api.Admin;

public enum AdminAccessMutationStatus
{
    Success,
    NotFound,
    UserNotFound,
    ServerNotFound,
    RevisionConflict
}

public sealed record AdminAccessMutationResult(
    AdminAccessMutationStatus Status,
    AdminServerAccessRuleRecord? Rule = null);

public sealed class AdminAccessRepository(
    NpgsqlDataSource dataSource,
    IOptions<VelocityAuthorizationOptions> velocityOptions,
    IOptions<ServerControlOptions> controlOptions)
{
    private static readonly JsonSerializerOptions AuditJsonOptions = CreateAuditJsonOptions();
    private readonly TimeSpan _maximumPermissionAge =
        TimeSpan.FromMinutes(velocityOptions.Value.MaximumLuckPermsAgeMinutes);
    private readonly TimeSpan _controlFreshness =
        TimeSpan.FromSeconds(controlOptions.Value.AgentFreshnessSeconds);

    public async Task<IReadOnlyList<AdminUserSummary>> SearchUsersAsync(
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT user_account.id,
                   user_account.username,
                   user_account.display_name,
                   user_account.email,
                   identity.minecraft_uuid,
                   identity.minecraft_name,
                   COALESCE(identity.luckperms_primary_group, 'default'),
                   user_account.access_tier,
                   identity.luckperms_synced_at,
                   user_account.is_disabled,
                   EXISTS (
                       SELECT 1
                       FROM launcher.minecraft_identity_bans identity_ban
                       WHERE identity_ban.minecraft_uuid = identity.minecraft_uuid
                         AND identity_ban.revoked_at IS NULL
                         AND (identity_ban.expires_at IS NULL OR identity_ban.expires_at > now())
                   ),
                   (
                       SELECT count(*)::integer
                       FROM launcher.server_access_overrides access_rule
                       WHERE access_rule.user_id = user_account.id
                         AND (access_rule.expires_at IS NULL OR access_rule.expires_at > now())
                   ),
                   user_account.created_at
            FROM launcher.users user_account
            LEFT JOIN launcher.minecraft_identities identity
                ON identity.user_id = user_account.id
            WHERE $1 = ''
               OR lower(user_account.username) LIKE $2 ESCAPE '\'
               OR lower(user_account.display_name) LIKE $2 ESCAPE '\'
               OR lower(COALESCE(user_account.email, '')) LIKE $2 ESCAPE '\'
               OR lower(COALESCE(identity.minecraft_name, '')) LIKE $2 ESCAPE '\'
            ORDER BY CASE
                         WHEN lower(user_account.username) = $1 THEN 0
                         WHEN lower(COALESCE(identity.minecraft_name, '')) = $1 THEN 1
                         ELSE 2
                     END,
                     user_account.created_at DESC,
                     user_account.id
            LIMIT $3;
            """;

        var normalized = query.Trim().ToLowerInvariant();
        var pattern = $"%{EscapeLikePattern(normalized)}%";
        var users = new List<AdminUserSummary>();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(normalized);
        command.Parameters.AddWithValue(pattern);
        command.Parameters.AddWithValue(limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            users.Add(ReadUser(reader));
        }

        return users;
    }

    public async Task<AdminUserAccessPreview?> GetAccessPreviewAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var user = await GetUserAsync(connection, userId, cancellationToken);
        if (user is null)
        {
            return null;
        }

        const string sql = """
            SELECT server.id,
                   server.display_name,
                   server.status,
                   server.is_visible AND (
                       server.activity_plan_status IS NOT NULL
                       OR NOT EXISTS (
                           SELECT 1
                           FROM launcher.servers published_plan
                           WHERE published_plan.activity_plan_status = 'Published'
                             AND published_plan.activity_target_server_id = server.id
                       )
                   ) AS projected_is_visible,
                   server.minimum_tier,
                   server.opens_at,
                   server.closes_at,
                   access_rule.decision,
                   access_rule.reason,
                   access_rule.expires_at,
                   access_rule.revision,
                   access_rule.created_at,
                   access_rule.updated_at,
                   control_target.reported_online,
                   control_target.last_seen_at,
                   server.activity_plan_status,
                   server.activity_package_import_id,
                   control_target.deployed_package_import_id
            FROM launcher.servers server
            LEFT JOIN launcher.server_access_overrides access_rule
                ON access_rule.user_id = $1
               AND access_rule.server_id = server.id
            LEFT JOIN launcher.server_control_targets control_target
                ON control_target.server_id = COALESCE(
                    server.activity_target_server_id,
                    server.id)
            WHERE server.server_role = 'Player'
            ORDER BY server.sort_order, server.id;
            """;

        var servers = new List<AdminServerAccessPreviewRecord>();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var serverId = reader.GetString(0);
            var configuredStatus =
                Enum.Parse<ServerStatus>(reader.GetString(2), ignoreCase: true);
            DateTimeOffset? opensAt = reader.IsDBNull(5)
                ? null
                : new DateTimeOffset(reader.GetDateTime(5));
            DateTimeOffset? closesAt = reader.IsDBNull(6)
                ? null
                : new DateTimeOffset(reader.GetDateTime(6));
            var scheduledStatus = ServerAvailabilityRules.ResolveStatus(
                configuredStatus,
                opensAt,
                closesAt,
                now);
            ServerControlObservation? controlObservation = null;
            if (!reader.IsDBNull(13))
            {
                controlObservation = new ServerControlObservation(
                    reader.GetBoolean(13),
                    new DateTimeOffset(reader.GetDateTime(14)));
            }

            var effectiveStatus = ServerControlAvailabilityRules.Resolve(
                scheduledStatus,
                controlObservation,
                now,
                _controlFreshness).Status;
            effectiveStatus = CatalogRepository.ResolveActivityDeploymentStatus(
                effectiveStatus,
                !reader.IsDBNull(15),
                reader.IsDBNull(16) ? null : reader.GetGuid(16),
                reader.IsDBNull(17) ? null : reader.GetGuid(17));
            AdminServerAccessRuleRecord? rule = null;
            if (!reader.IsDBNull(7))
            {
                rule = new AdminServerAccessRuleRecord(
                    userId,
                    serverId,
                    Enum.Parse<AdminServerAccessDecision>(
                        reader.GetString(7),
                        ignoreCase: true),
                    reader.GetString(8),
                    reader.IsDBNull(9)
                        ? null
                        : new DateTimeOffset(reader.GetDateTime(9)),
                    reader.GetInt64(10),
                    new DateTimeOffset(reader.GetDateTime(11)),
                    new DateTimeOffset(reader.GetDateTime(12)));
            }

            var minimumTier =
                Enum.Parse<AccessTier>(reader.GetString(4), ignoreCase: true);
            var access = AdminAccessRules.Evaluate(
                user,
                reader.GetBoolean(3),
                effectiveStatus,
                minimumTier,
                rule,
                now,
                _maximumPermissionAge);
            servers.Add(new AdminServerAccessPreviewRecord(
                serverId,
                reader.GetString(1),
                configuredStatus,
                effectiveStatus,
                reader.GetBoolean(3),
                minimumTier,
                access.Allowed,
                access.Reason,
                rule));
        }

        return new AdminUserAccessPreview(user, servers);
    }

    public async Task<AdminAccessMutationResult> UpsertRuleAsync(
        Guid userId,
        string serverId,
        AdminServerAccessRuleUpsertRequest request,
        Guid actorUserId,
        IPAddress? sourceIp,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (!await UserExistsAsync(
                connection,
                transaction,
                userId,
                cancellationToken))
        {
            return new AdminAccessMutationResult(AdminAccessMutationStatus.UserNotFound);
        }

        if (!await ServerExistsAsync(connection, transaction, serverId, cancellationToken))
        {
            return new AdminAccessMutationResult(AdminAccessMutationStatus.ServerNotFound);
        }

        var before = await GetRuleForUpdateAsync(
            connection,
            transaction,
            userId,
            serverId,
            cancellationToken);
        if ((before is null && request.ExpectedRevision is not null) ||
            (before is not null && request.ExpectedRevision != before.Revision))
        {
            return new AdminAccessMutationResult(
                AdminAccessMutationStatus.RevisionConflict,
                before);
        }

        const string insertSql = """
            INSERT INTO launcher.server_access_overrides
                (user_id, server_id, decision, reason, expires_at, created_by)
            VALUES ($1, $2, $3, $4, $5, $6)
            RETURNING user_id, server_id, decision, reason, expires_at,
                      revision, created_at, updated_at;
            """;
        const string updateSql = """
            UPDATE launcher.server_access_overrides
            SET decision = $3,
                reason = $4,
                expires_at = $5,
                created_by = $6,
                revision = revision + 1,
                updated_at = now()
            WHERE user_id = $1 AND server_id = $2
            RETURNING user_id, server_id, decision, reason, expires_at,
                      revision, created_at, updated_at;
            """;

        await using var command = new NpgsqlCommand(
            before is null ? insertSql : updateSql,
            connection,
            transaction);
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(serverId);
        command.Parameters.AddWithValue(request.Decision.ToString());
        command.Parameters.AddWithValue(request.Reason.Trim());
        AdminPostgresParameters.AddPositional(
            command.Parameters,
            NpgsqlDbType.TimestampTz,
            request.ExpiresAt?.ToUniversalTime());
        command.Parameters.AddWithValue(actorUserId);
        AdminServerAccessRuleRecord updated;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            await reader.ReadAsync(cancellationToken);
            updated = ReadRule(reader);
        }

        await WriteAuditAsync(
            connection,
            transaction,
            actorUserId,
            sourceIp,
            before is null
                ? "access.server_rule.created"
                : "access.server_rule.updated",
            userId,
            serverId,
            before,
            updated,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new AdminAccessMutationResult(AdminAccessMutationStatus.Success, updated);
    }

    public async Task<AdminAccessMutationResult> DeleteRuleAsync(
        Guid userId,
        string serverId,
        long expectedRevision,
        Guid actorUserId,
        IPAddress? sourceIp,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var before = await GetRuleForUpdateAsync(
            connection,
            transaction,
            userId,
            serverId,
            cancellationToken);
        if (before is null)
        {
            return new AdminAccessMutationResult(AdminAccessMutationStatus.NotFound);
        }

        if (before.Revision != expectedRevision)
        {
            return new AdminAccessMutationResult(
                AdminAccessMutationStatus.RevisionConflict,
                before);
        }

        await using (var command = new NpgsqlCommand(
                         """
                         DELETE FROM launcher.server_access_overrides
                         WHERE user_id = $1 AND server_id = $2;
                         """,
                         connection,
                         transaction))
        {
            command.Parameters.AddWithValue(userId);
            command.Parameters.AddWithValue(serverId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await WriteAuditAsync(
            connection,
            transaction,
            actorUserId,
            sourceIp,
            "access.server_rule.deleted",
            userId,
            serverId,
            before,
            after: null,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new AdminAccessMutationResult(AdminAccessMutationStatus.Success);
    }

    private static async Task<AdminUserSummary?> GetUserAsync(
        NpgsqlConnection connection,
        Guid userId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT user_account.id,
                   user_account.username,
                   user_account.display_name,
                   user_account.email,
                   identity.minecraft_uuid,
                   identity.minecraft_name,
                   COALESCE(identity.luckperms_primary_group, 'default'),
                   user_account.access_tier,
                   identity.luckperms_synced_at,
                   user_account.is_disabled,
                   EXISTS (
                       SELECT 1
                       FROM launcher.minecraft_identity_bans identity_ban
                       WHERE identity_ban.minecraft_uuid = identity.minecraft_uuid
                         AND identity_ban.revoked_at IS NULL
                         AND (identity_ban.expires_at IS NULL OR identity_ban.expires_at > now())
                   ),
                   (
                       SELECT count(*)::integer
                       FROM launcher.server_access_overrides access_rule
                       WHERE access_rule.user_id = user_account.id
                         AND (access_rule.expires_at IS NULL OR access_rule.expires_at > now())
                   ),
                   user_account.created_at
            FROM launcher.users user_account
            LEFT JOIN launcher.minecraft_identities identity
                ON identity.user_id = user_account.id
            WHERE user_account.id = $1;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadUser(reader) : null;
    }

    private static AdminUserSummary ReadUser(NpgsqlDataReader reader)
    {
        return new AdminUserSummary(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetGuid(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.GetString(6),
            Enum.Parse<AccessTier>(reader.GetString(7), ignoreCase: true),
            reader.IsDBNull(8)
                ? null
                : new DateTimeOffset(reader.GetDateTime(8)),
            reader.GetBoolean(9),
            reader.GetBoolean(10),
            reader.GetInt32(11),
            new DateTimeOffset(reader.GetDateTime(12)));
    }

    private static async Task<AdminServerAccessRuleRecord?> GetRuleForUpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid userId,
        string serverId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT user_id, server_id, decision, reason, expires_at,
                   revision, created_at, updated_at
            FROM launcher.server_access_overrides
            WHERE user_id = $1 AND server_id = $2
            FOR UPDATE;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(serverId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadRule(reader) : null;
    }

    private static AdminServerAccessRuleRecord ReadRule(NpgsqlDataReader reader)
    {
        return new AdminServerAccessRuleRecord(
            reader.GetGuid(0),
            reader.GetString(1),
            Enum.Parse<AdminServerAccessDecision>(
                reader.GetString(2),
                ignoreCase: true),
            reader.GetString(3),
            reader.IsDBNull(4)
                ? null
                : new DateTimeOffset(reader.GetDateTime(4)),
            reader.GetInt64(5),
            new DateTimeOffset(reader.GetDateTime(6)),
            new DateTimeOffset(reader.GetDateTime(7)));
    }

    private static async Task<bool> UserExistsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid id,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT 1 FROM launcher.users WHERE id = $1 FOR SHARE;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(id);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private static async Task<bool> ServerExistsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string serverId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT 1
            FROM launcher.servers
            WHERE id = $1
              AND server_role = 'Player'
            FOR SHARE;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(serverId);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private static async Task WriteAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid actorUserId,
        IPAddress? sourceIp,
        string action,
        Guid userId,
        string serverId,
        AdminServerAccessRuleRecord? before,
        AdminServerAccessRuleRecord? after,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO launcher.audit_logs
                (actor_user_id, action, target_type, target_id, source_ip, before_data, after_data)
            VALUES ($1, $2, 'server_access_rule', $3, $4, $5, $6);
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(actorUserId);
        command.Parameters.AddWithValue(action);
        command.Parameters.AddWithValue($"{userId:D}:{serverId}");
        AdminPostgresParameters.AddPositional(
            command.Parameters,
            NpgsqlDbType.Inet,
            sourceIp);
        AdminPostgresParameters.AddPositional(
            command.Parameters,
            NpgsqlDbType.Jsonb,
            before is null ? null : JsonSerializer.Serialize(before, AuditJsonOptions));
        AdminPostgresParameters.AddPositional(
            command.Parameters,
            NpgsqlDbType.Jsonb,
            after is null ? null : JsonSerializer.Serialize(after, AuditJsonOptions));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string EscapeLikePattern(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    private static JsonSerializerOptions CreateAuditJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}

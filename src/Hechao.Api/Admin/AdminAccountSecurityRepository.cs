using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hechao.Api.Authentication;
using Hechao.Contracts;
using Npgsql;
using NpgsqlTypes;

namespace Hechao.Api.Admin;

public enum AdminAccountSecurityMutationStatus
{
    Success,
    UserNotFound,
    SessionNotFound,
    MinecraftIdentityNotLinked,
    MinecraftBanNotFound,
    SelfProtection,
    LastAdministrator,
    RevisionConflict
}

public sealed record AdminAccountSecurityMutationResult(
    AdminAccountSecurityMutationStatus Status,
    AdminUserSecuritySummary? Security = null,
    AdminMinecraftIdentityBanRecord? CurrentBan = null,
    AdminSecurityRevocationCounts? Revoked = null);

public sealed class AdminAccountSecurityRepository(
    NpgsqlDataSource dataSource,
    ForumSessionRevocationRepository forumSessionRevocations,
    LuckPermsTierCommandRepository tierCommands)
{
    private static readonly JsonSerializerOptions AuditJsonOptions = CreateAuditJsonOptions();

    public async Task<AdminUserSecuritySummary?> GetSecurityAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await ReadSecurityAsync(connection, userId, cancellationToken);
    }

    public async Task<AdminAccountSecurityMutationResult> SetAccountDisabledAsync(
        Guid userId,
        bool isDisabled,
        string reason,
        Guid actorUserId,
        IPAddress? sourceIp,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await AcquireAccountSecurityLockAsync(
            connection,
            transaction,
            cancellationToken);
        var target = await ReadAccountStateForUpdateAsync(
            connection,
            transaction,
            userId,
            cancellationToken);
        if (target is null)
        {
            return new AdminAccountSecurityMutationResult(
                AdminAccountSecurityMutationStatus.UserNotFound);
        }

        if (isDisabled && userId == actorUserId)
        {
            return new AdminAccountSecurityMutationResult(
                AdminAccountSecurityMutationStatus.SelfProtection);
        }

        if (isDisabled &&
            target.AccessTier == AccessTier.Administrator &&
            !target.IsDisabled &&
            await CountActiveAdministratorsAsync(
                connection,
                transaction,
                cancellationToken) <= 1)
        {
            return new AdminAccountSecurityMutationResult(
                AdminAccountSecurityMutationStatus.LastAdministrator);
        }

        await using (var update = new NpgsqlCommand(
                         """
                         UPDATE launcher.users
                         SET is_disabled = $2, updated_at = $3
                         WHERE id = $1;
                         """,
                         connection,
                         transaction))
        {
            update.Parameters.AddWithValue(userId);
            update.Parameters.AddWithValue(isDisabled);
            update.Parameters.AddWithValue(now);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        AdminSecurityRevocationCounts? revoked = null;
        if (isDisabled)
        {
            revoked = await RevokeAllAuthenticationStateAsync(
                connection,
                transaction,
                userId,
                now,
                cancellationToken);
        }

        await WriteAuditAsync(
            connection,
            transaction,
            actorUserId,
            sourceIp,
            isDisabled ? "security.account.disabled" : "security.account.enabled",
            "user",
            userId.ToString("D"),
            new
            {
                target.IsDisabled,
                AccessTier = target.AccessTier.ToString()
            },
            new
            {
                IsDisabled = isDisabled,
                Reason = reason.Trim(),
                Revoked = revoked
            },
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new AdminAccountSecurityMutationResult(
            AdminAccountSecurityMutationStatus.Success,
            await GetSecurityAsync(userId, cancellationToken),
            Revoked: revoked);
    }

    public async Task<AdminAccountSecurityMutationResult> RevokeAllSessionsAsync(
        Guid userId,
        string reason,
        Guid actorUserId,
        IPAddress? sourceIp,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (!await LockUserAsync(connection, transaction, userId, cancellationToken))
        {
            return new AdminAccountSecurityMutationResult(
                AdminAccountSecurityMutationStatus.UserNotFound);
        }

        var revoked = await RevokeAllAuthenticationStateAsync(
            connection,
            transaction,
            userId,
            now,
            cancellationToken);
        await WriteAuditAsync(
            connection,
            transaction,
            actorUserId,
            sourceIp,
            "security.sessions.revoked_all",
            "user",
            userId.ToString("D"),
            before: null,
            new
            {
                Reason = reason.Trim(),
                Revoked = revoked
            },
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new AdminAccountSecurityMutationResult(
            AdminAccountSecurityMutationStatus.Success,
            await GetSecurityAsync(userId, cancellationToken),
            Revoked: revoked);
    }

    public async Task<AdminAccountSecurityMutationResult> RevokeSessionAsync(
        Guid userId,
        Guid sessionId,
        string reason,
        Guid actorUserId,
        IPAddress? sourceIp,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (!await LockUserAsync(connection, transaction, userId, cancellationToken))
        {
            return new AdminAccountSecurityMutationResult(
                AdminAccountSecurityMutationStatus.UserNotFound);
        }

        var before = await ReadDeviceSessionForUpdateAsync(
            connection,
            transaction,
            userId,
            sessionId,
            cancellationToken);
        if (before is null)
        {
            return new AdminAccountSecurityMutationResult(
                AdminAccountSecurityMutationStatus.SessionNotFound);
        }

        await using (var update = new NpgsqlCommand(
                         """
                         UPDATE launcher.auth_sessions
                         SET revoked_at = $3
                         WHERE user_id = $1 AND id = $2 AND revoked_at IS NULL;
                         """,
                         connection,
                         transaction))
        {
            update.Parameters.AddWithValue(userId);
            update.Parameters.AddWithValue(sessionId);
            update.Parameters.AddWithValue(now);
            if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                return new AdminAccountSecurityMutationResult(
                    AdminAccountSecurityMutationStatus.SessionNotFound);
            }
        }

        var revoked = new AdminSecurityRevocationCounts(1, 0, 0, 0, 0);
        await WriteAuditAsync(
            connection,
            transaction,
            actorUserId,
            sourceIp,
            "security.session.revoked",
            "auth_session",
            sessionId.ToString("D"),
            before,
            new
            {
                UserId = userId,
                RevokedAt = now,
                Reason = reason.Trim()
            },
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new AdminAccountSecurityMutationResult(
            AdminAccountSecurityMutationStatus.Success,
            await GetSecurityAsync(userId, cancellationToken),
            Revoked: revoked);
    }

    public async Task<AdminAccountSecurityMutationResult> SetMinecraftIdentityBanAsync(
        Guid userId,
        AdminMinecraftIdentityBanRequest request,
        Guid actorUserId,
        IPAddress? sourceIp,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var targetSnapshot = await ReadAccountStateAsync(
            connection,
            transaction,
            userId,
            cancellationToken);
        if (targetSnapshot is null)
        {
            return new AdminAccountSecurityMutationResult(
                AdminAccountSecurityMutationStatus.UserNotFound);
        }

        if (userId == actorUserId)
        {
            return new AdminAccountSecurityMutationResult(
                AdminAccountSecurityMutationStatus.SelfProtection);
        }

        if (targetSnapshot.MinecraftUuid is null)
        {
            return new AdminAccountSecurityMutationResult(
                AdminAccountSecurityMutationStatus.MinecraftIdentityNotLinked);
        }

        await AcquireMinecraftIdentityLockAsync(
            connection,
            transaction,
            targetSnapshot.MinecraftUuid.Value,
            cancellationToken);
        var target = await ReadAccountStateForUpdateAsync(
            connection,
            transaction,
            userId,
            cancellationToken);
        if (target?.MinecraftUuid != targetSnapshot.MinecraftUuid)
        {
            return new AdminAccountSecurityMutationResult(
                target is null
                    ? AdminAccountSecurityMutationStatus.UserNotFound
                    : AdminAccountSecurityMutationStatus.MinecraftIdentityNotLinked);
        }

        var before = await ReadMinecraftBanForUpdateAsync(
            connection,
            transaction,
            target.MinecraftUuid.Value,
            cancellationToken);
        var activeBefore = IsActive(before, now);
        if ((activeBefore && request.ExpectedRevision != before!.Revision) ||
            (!activeBefore && request.ExpectedRevision is not null))
        {
            return new AdminAccountSecurityMutationResult(
                AdminAccountSecurityMutationStatus.RevisionConflict,
                CurrentBan: activeBefore ? before : null);
        }

        const string insertSql = """
            INSERT INTO launcher.minecraft_identity_bans
                (minecraft_uuid, reason, expires_at, created_by, created_at, updated_at)
            VALUES ($1, $2, $3, $4, $5, $5)
            RETURNING minecraft_uuid, reason, expires_at, created_by,
                      NULL::text, created_at, revoked_at, revoked_by,
                      revoked_reason, updated_at, revision;
            """;
        const string updateSql = """
            UPDATE launcher.minecraft_identity_bans
            SET reason = $2,
                expires_at = $3,
                created_by = $4,
                created_at = $5,
                revoked_at = NULL,
                revoked_by = NULL,
                revoked_reason = NULL,
                updated_at = $5,
                revision = revision + 1
            WHERE minecraft_uuid = $1
            RETURNING minecraft_uuid, reason, expires_at, created_by,
                      NULL::text, created_at, revoked_at, revoked_by,
                      revoked_reason, updated_at, revision;
            """;

        AdminMinecraftIdentityBanRecord updated;
        await using (var command = new NpgsqlCommand(
                         before is null ? insertSql : updateSql,
                         connection,
                         transaction))
        {
            command.Parameters.AddWithValue(target.MinecraftUuid.Value);
            command.Parameters.AddWithValue(request.Reason.Trim());
            AdminPostgresParameters.AddPositional(
                command.Parameters,
                NpgsqlDbType.TimestampTz,
                request.ExpiresAt?.ToUniversalTime());
            command.Parameters.AddWithValue(actorUserId);
            command.Parameters.AddWithValue(now);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            updated = ReadMinecraftBan(reader);
        }

        var revoked = await RevokeAllAuthenticationStateAsync(
            connection,
            transaction,
            userId,
            now,
            cancellationToken);
        await WriteAuditAsync(
            connection,
            transaction,
            actorUserId,
            sourceIp,
            activeBefore
                ? "security.minecraft_ban.updated"
                : "security.minecraft_ban.created",
            "minecraft_identity",
            target.MinecraftUuid.Value.ToString("D"),
            activeBefore ? before : null,
            new
            {
                Ban = updated,
                Revoked = revoked
            },
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new AdminAccountSecurityMutationResult(
            AdminAccountSecurityMutationStatus.Success,
            await GetSecurityAsync(userId, cancellationToken),
            updated,
            revoked);
    }

    public async Task<AdminAccountSecurityMutationResult> RevokeMinecraftIdentityBanAsync(
        Guid userId,
        AdminMinecraftIdentityBanDeleteRequest request,
        Guid actorUserId,
        IPAddress? sourceIp,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var targetSnapshot = await ReadAccountStateAsync(
            connection,
            transaction,
            userId,
            cancellationToken);
        if (targetSnapshot is null)
        {
            return new AdminAccountSecurityMutationResult(
                AdminAccountSecurityMutationStatus.UserNotFound);
        }

        if (userId == actorUserId)
        {
            return new AdminAccountSecurityMutationResult(
                AdminAccountSecurityMutationStatus.SelfProtection);
        }

        if (targetSnapshot.MinecraftUuid is null)
        {
            return new AdminAccountSecurityMutationResult(
                AdminAccountSecurityMutationStatus.MinecraftIdentityNotLinked);
        }

        await AcquireMinecraftIdentityLockAsync(
            connection,
            transaction,
            targetSnapshot.MinecraftUuid.Value,
            cancellationToken);
        var target = await ReadAccountStateForUpdateAsync(
            connection,
            transaction,
            userId,
            cancellationToken);
        if (target?.MinecraftUuid != targetSnapshot.MinecraftUuid)
        {
            return new AdminAccountSecurityMutationResult(
                target is null
                    ? AdminAccountSecurityMutationStatus.UserNotFound
                    : AdminAccountSecurityMutationStatus.MinecraftIdentityNotLinked);
        }

        var before = await ReadMinecraftBanForUpdateAsync(
            connection,
            transaction,
            target.MinecraftUuid.Value,
            cancellationToken);
        if (!IsActive(before, now))
        {
            return new AdminAccountSecurityMutationResult(
                AdminAccountSecurityMutationStatus.MinecraftBanNotFound);
        }

        if (before!.Revision != request.ExpectedRevision)
        {
            return new AdminAccountSecurityMutationResult(
                AdminAccountSecurityMutationStatus.RevisionConflict,
                CurrentBan: before);
        }

        AdminMinecraftIdentityBanRecord updated;
        await using (var command = new NpgsqlCommand(
                         """
                         UPDATE launcher.minecraft_identity_bans
                         SET revoked_at = $2,
                             revoked_by = $3,
                             revoked_reason = $4,
                             updated_at = $2,
                             revision = revision + 1
                         WHERE minecraft_uuid = $1
                         RETURNING minecraft_uuid, reason, expires_at, created_by,
                                   NULL::text, created_at, revoked_at, revoked_by,
                                   revoked_reason, updated_at, revision;
                         """,
                         connection,
                         transaction))
        {
            command.Parameters.AddWithValue(target.MinecraftUuid.Value);
            command.Parameters.AddWithValue(now);
            command.Parameters.AddWithValue(actorUserId);
            command.Parameters.AddWithValue(request.Reason.Trim());
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            updated = ReadMinecraftBan(reader);
        }

        await WriteAuditAsync(
            connection,
            transaction,
            actorUserId,
            sourceIp,
            "security.minecraft_ban.revoked",
            "minecraft_identity",
            target.MinecraftUuid.Value.ToString("D"),
            before,
            updated,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new AdminAccountSecurityMutationResult(
            AdminAccountSecurityMutationStatus.Success,
            await GetSecurityAsync(userId, cancellationToken));
    }

    private async Task<AdminUserSecuritySummary?> ReadSecurityAsync(
        NpgsqlConnection connection,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var user = await ReadUserAsync(connection, userId, cancellationToken);
        if (user is null)
        {
            return null;
        }

        var sessions = await ReadDeviceSessionsAsync(connection, userId, cancellationToken);
        var counts = await ReadPendingSecurityCountsAsync(
            connection,
            userId,
            cancellationToken);
        AdminMinecraftIdentityBanRecord? ban = null;
        if (user.MinecraftUuid is not null)
        {
            ban = await ReadActiveMinecraftBanAsync(
                connection,
                user.MinecraftUuid.Value,
                cancellationToken);
        }
        var pendingTierChange = await tierCommands.GetPendingForUserAsync(
            userId,
            cancellationToken);

        return new AdminUserSecuritySummary(
            user,
            sessions,
            counts.AdminSessions,
            counts.AdminTickets,
            counts.VelocityLaunchGrants,
            counts.ForumSessionRevocations,
            pendingTierChange,
            ban);
    }

    private static async Task<AdminUserSummary?> ReadUserAsync(
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
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadUser(reader);
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

    private static async Task<IReadOnlyList<AdminDeviceSessionRecord>> ReadDeviceSessionsAsync(
        NpgsqlConnection connection,
        Guid userId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, created_at, last_seen_at, refresh_expires_at, source_ip
            FROM launcher.auth_sessions
            WHERE user_id = $1
              AND revoked_at IS NULL
              AND refresh_expires_at > now()
            ORDER BY last_seen_at DESC, id
            LIMIT 100;
            """;

        var sessions = new List<AdminDeviceSessionRecord>();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            sessions.Add(ReadDeviceSession(reader));
        }

        return sessions;
    }

    private static async Task<PendingSecurityCounts> ReadPendingSecurityCountsAsync(
        NpgsqlConnection connection,
        Guid userId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                (
                    SELECT count(*)::integer
                    FROM launcher.admin_web_sessions
                    WHERE user_id = $1
                      AND revoked_at IS NULL
                      AND expires_at > now()
                ),
                (
                    SELECT count(*)::integer
                    FROM launcher.admin_login_tickets
                    WHERE user_id = $1
                      AND consumed_at IS NULL
                      AND expires_at > now()
                ),
                (
                    SELECT count(*)::integer
                    FROM launcher.velocity_launch_grants
                    WHERE user_id = $1
                      AND consumed_at IS NULL
                      AND revoked_at IS NULL
                      AND expires_at > now()
                ),
                (
                    SELECT count(*)::integer
                    FROM launcher.forum_session_revocation_outbox
                    WHERE user_id = $1
                      AND completed_at IS NULL
                );
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new PendingSecurityCounts(
            reader.GetInt32(0),
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.GetInt32(3));
    }

    private static async Task<AccountState?> ReadAccountStateForUpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid userId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT user_account.is_disabled,
                   user_account.access_tier,
                   identity.minecraft_uuid
            FROM launcher.users user_account
            LEFT JOIN launcher.minecraft_identities identity
                ON identity.user_id = user_account.id
            WHERE user_account.id = $1
            FOR UPDATE OF user_account;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new AccountState(
            reader.GetBoolean(0),
            Enum.Parse<AccessTier>(reader.GetString(1), ignoreCase: true),
            reader.IsDBNull(2) ? null : reader.GetGuid(2));
    }

    private static async Task<AccountState?> ReadAccountStateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid userId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT user_account.is_disabled,
                   user_account.access_tier,
                   identity.minecraft_uuid
            FROM launcher.users user_account
            LEFT JOIN launcher.minecraft_identities identity
                ON identity.user_id = user_account.id
            WHERE user_account.id = $1;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new AccountState(
            reader.GetBoolean(0),
            Enum.Parse<AccessTier>(reader.GetString(1), ignoreCase: true),
            reader.IsDBNull(2) ? null : reader.GetGuid(2));
    }

    private static async Task<bool> LockUserAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid userId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT 1 FROM launcher.users WHERE id = $1 FOR UPDATE;",
            connection,
            transaction);
        command.Parameters.AddWithValue(userId);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private static async Task AcquireAccountSecurityLockAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(721220003);",
            connection,
            transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task AcquireMinecraftIdentityLockAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid minecraftUuid,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtextextended($1, 721220002));",
            connection,
            transaction);
        command.Parameters.AddWithValue(minecraftUuid.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> CountActiveAdministratorsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT count(*)::integer
            FROM launcher.users
            WHERE access_tier = 'Administrator' AND NOT is_disabled;
            """,
            connection,
            transaction);
        return (int)(await command.ExecuteScalarAsync(cancellationToken) ?? 0);
    }

    private static async Task<AdminDeviceSessionRecord?> ReadDeviceSessionForUpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, created_at, last_seen_at, refresh_expires_at, source_ip
            FROM launcher.auth_sessions
            WHERE user_id = $1
              AND id = $2
              AND revoked_at IS NULL
              AND refresh_expires_at > now()
            FOR UPDATE;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(sessionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadDeviceSession(reader) : null;
    }

    private static AdminDeviceSessionRecord ReadDeviceSession(NpgsqlDataReader reader)
    {
        return new AdminDeviceSessionRecord(
            reader.GetGuid(0),
            new DateTimeOffset(reader.GetDateTime(1)),
            new DateTimeOffset(reader.GetDateTime(2)),
            new DateTimeOffset(reader.GetDateTime(3)),
            reader.IsDBNull(4)
                ? null
                : reader.GetFieldValue<IPAddress>(4).ToString());
    }

    private static async Task<AdminMinecraftIdentityBanRecord?> ReadActiveMinecraftBanAsync(
        NpgsqlConnection connection,
        Guid minecraftUuid,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT identity_ban.minecraft_uuid,
                   identity_ban.reason,
                   identity_ban.expires_at,
                   identity_ban.created_by,
                   creator.display_name,
                   identity_ban.created_at,
                   identity_ban.revoked_at,
                   identity_ban.revoked_by,
                   identity_ban.revoked_reason,
                   identity_ban.updated_at,
                   identity_ban.revision
            FROM launcher.minecraft_identity_bans identity_ban
            LEFT JOIN launcher.users creator ON creator.id = identity_ban.created_by
            WHERE identity_ban.minecraft_uuid = $1
              AND identity_ban.revoked_at IS NULL
              AND (identity_ban.expires_at IS NULL OR identity_ban.expires_at > now());
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(minecraftUuid);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadMinecraftBan(reader) : null;
    }

    private static async Task<AdminMinecraftIdentityBanRecord?> ReadMinecraftBanForUpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid minecraftUuid,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT identity_ban.minecraft_uuid,
                   identity_ban.reason,
                   identity_ban.expires_at,
                   identity_ban.created_by,
                   creator.display_name,
                   identity_ban.created_at,
                   identity_ban.revoked_at,
                   identity_ban.revoked_by,
                   identity_ban.revoked_reason,
                   identity_ban.updated_at,
                   identity_ban.revision
            FROM launcher.minecraft_identity_bans identity_ban
            LEFT JOIN launcher.users creator ON creator.id = identity_ban.created_by
            WHERE identity_ban.minecraft_uuid = $1
            FOR UPDATE OF identity_ban;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(minecraftUuid);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadMinecraftBan(reader) : null;
    }

    private static AdminMinecraftIdentityBanRecord ReadMinecraftBan(NpgsqlDataReader reader)
    {
        return new AdminMinecraftIdentityBanRecord(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.IsDBNull(2)
                ? null
                : new DateTimeOffset(reader.GetDateTime(2)),
            reader.GetGuid(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            new DateTimeOffset(reader.GetDateTime(5)),
            reader.IsDBNull(6)
                ? null
                : new DateTimeOffset(reader.GetDateTime(6)),
            reader.IsDBNull(7) ? null : reader.GetGuid(7),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            new DateTimeOffset(reader.GetDateTime(9)),
            reader.GetInt64(10));
    }

    private static bool IsActive(
        AdminMinecraftIdentityBanRecord? ban,
        DateTimeOffset now)
    {
        return ban is not null &&
               ban.RevokedAt is null &&
               (ban.ExpiresAt is null || ban.ExpiresAt > now);
    }

    private async Task<AdminSecurityRevocationCounts> RevokeAllAuthenticationStateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid userId,
        DateTimeOffset revokedAt,
        CancellationToken cancellationToken)
    {
        var launcherSessions = await ExecuteRevocationAsync(
            connection,
            transaction,
            """
            UPDATE launcher.auth_sessions
            SET revoked_at = $2
            WHERE user_id = $1 AND revoked_at IS NULL;
            """,
            userId,
            revokedAt,
            cancellationToken);
        var adminSessions = await ExecuteRevocationAsync(
            connection,
            transaction,
            """
            UPDATE launcher.admin_web_sessions
            SET revoked_at = $2
            WHERE user_id = $1 AND revoked_at IS NULL;
            """,
            userId,
            revokedAt,
            cancellationToken);
        _ = await ExecuteRevocationAsync(
            connection,
            transaction,
            """
            UPDATE launcher.admin_trusted_devices
            SET revoked_at = $2
            WHERE user_id = $1 AND revoked_at IS NULL;
            """,
            userId,
            revokedAt,
            cancellationToken);
        var adminTickets = await ExecuteRevocationAsync(
            connection,
            transaction,
            """
            UPDATE launcher.admin_login_tickets
            SET consumed_at = $2
            WHERE user_id = $1 AND consumed_at IS NULL;
            """,
            userId,
            revokedAt,
            cancellationToken);
        var velocityLaunchGrants = await ExecuteRevocationAsync(
            connection,
            transaction,
            """
            UPDATE launcher.velocity_launch_grants
            SET revoked_at = $2
            WHERE user_id = $1
              AND consumed_at IS NULL
              AND revoked_at IS NULL;
            """,
            userId,
            revokedAt,
            cancellationToken);
        await forumSessionRevocations.EnqueueAsync(
            connection,
            transaction,
            userId,
            revokedAt,
            cancellationToken);
        return new AdminSecurityRevocationCounts(
            launcherSessions,
            adminSessions,
            adminTickets,
            velocityLaunchGrants,
            1);
    }

    private static async Task<int> ExecuteRevocationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        Guid userId,
        DateTimeOffset revokedAt,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(revokedAt);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task WriteAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid actorUserId,
        IPAddress? sourceIp,
        string action,
        string targetType,
        string targetId,
        object? before,
        object? after,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO launcher.audit_logs
                (actor_user_id, action, target_type, target_id, source_ip,
                 before_data, after_data)
            VALUES ($1, $2, $3, $4, $5, $6, $7);
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(actorUserId);
        command.Parameters.AddWithValue(action);
        command.Parameters.AddWithValue(targetType);
        command.Parameters.AddWithValue(targetId);
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

    private static JsonSerializerOptions CreateAuditJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private sealed record AccountState(
        bool IsDisabled,
        AccessTier AccessTier,
        Guid? MinecraftUuid);

    private sealed record PendingSecurityCounts(
        int AdminSessions,
        int AdminTickets,
        int VelocityLaunchGrants,
        int ForumSessionRevocations);
}

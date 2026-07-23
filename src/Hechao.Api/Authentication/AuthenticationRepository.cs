using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hechao.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Hechao.Api.Authentication;

public sealed class AuthenticationRepository(
    NpgsqlDataSource dataSource,
    SessionTokenGenerator tokenGenerator,
    IOptions<LauncherAuthenticationOptions> authenticationOptions,
    IPasswordHasher<HechaoAccountPasswordSubject> passwordHasher)
{
    private const int MaximumActiveSessionsPerUser = 20;
    private readonly LauncherAuthenticationOptions _options = authenticationOptions.Value;
    private readonly string _dummyPasswordHash = passwordHasher.HashPassword(
        new HechaoAccountPasswordSubject(Guid.Empty, "missing"),
        Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));

    public async Task<AuthSessionResponse> RegisterAccountAsync(
        string username,
        string displayName,
        string password,
        string? email,
        IPAddress? sourceIp,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var userId = Guid.NewGuid();
        var passwordSubject = new HechaoAccountPasswordSubject(userId, username);
        var passwordHash = passwordHasher.HashPassword(passwordSubject, password);
        var tokens = tokenGenerator.Create();
        var accessExpiresAt = now.AddMinutes(_options.AccessTokenMinutes);
        var refreshExpiresAt = now.AddDays(_options.RefreshTokenDays);
        var sessionId = Guid.NewGuid();

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using var insertUser = new NpgsqlCommand(
                """
                INSERT INTO launcher.users
                    (id, username, display_name, email, password_hash,
                     access_tier, created_at, updated_at)
                VALUES ($1, $2, $3, $4, $5, 'Member', $6, $6);
                """,
                connection,
                transaction);
            insertUser.Parameters.AddWithValue(userId);
            insertUser.Parameters.AddWithValue(username);
            insertUser.Parameters.AddWithValue(displayName);
            insertUser.Parameters.Add(new NpgsqlParameter
            {
                NpgsqlDbType = NpgsqlDbType.Text,
                Value = email ?? (object)DBNull.Value
            });
            insertUser.Parameters.AddWithValue(passwordHash);
            insertUser.Parameters.AddWithValue(now);
            await insertUser.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException exception) when (
            exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new HechaoAccountConflictException(
                exception.ConstraintName == "users_email_ci_idx" ? "email" : "username");
        }

        await InsertSessionAsync(
            connection,
            transaction,
            sessionId,
            userId,
            tokens,
            accessExpiresAt,
            refreshExpiresAt,
            sourceIp,
            HashUserAgent(userAgent),
            now,
            cancellationToken);
        await WriteAccountAuditAsync(
            connection,
            transaction,
            userId,
            "auth.account.registered",
            sourceIp,
            new { username, email },
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var account = new HechaoAccount(
            userId,
            username,
            displayName,
            email,
            null,
            null,
            "default",
            AccessTier.Member,
            null,
            now);
        return CreateSessionResponse(
            tokens,
            accessExpiresAt,
            refreshExpiresAt,
            account);
    }

    public async Task<AuthSessionResponse?> LoginAccountAsync(
        string usernameOrEmail,
        string password,
        IPAddress? sourceIp,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        const string selectSql = """
            SELECT u.password_hash,
                   u.id, u.username, u.display_name, u.email,
                   i.minecraft_uuid, i.minecraft_name,
                   COALESCE(i.luckperms_primary_group, 'default'),
                   u.access_tier, i.luckperms_synced_at, u.created_at
            FROM launcher.users u
            LEFT JOIN launcher.minecraft_identities i ON i.user_id = u.id
            WHERE (lower(u.username) = $1 OR lower(u.email) = $1)
              AND NOT u.is_disabled
            FOR UPDATE OF u;
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var select = new NpgsqlCommand(selectSql, connection, transaction);
        select.Parameters.AddWithValue(usernameOrEmail);

        HechaoAccount? account = null;
        string? storedPasswordHash = null;
        await using (var reader = await select.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                storedPasswordHash = reader.IsDBNull(0) ? null : reader.GetString(0);
                account = ReadAccount(reader, offset: 1);
            }
        }

        var passwordSubject = account is null
            ? new HechaoAccountPasswordSubject(Guid.Empty, "missing")
            : new HechaoAccountPasswordSubject(account.UserId, account.Username);
        var verification = passwordHasher.VerifyHashedPassword(
            passwordSubject,
            storedPasswordHash ?? _dummyPasswordHash,
            password);
        if (account is null ||
            storedPasswordHash is null ||
            verification == PasswordVerificationResult.Failed)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            var replacementHash = passwordHasher.HashPassword(passwordSubject, password);
            await using var rehash = new NpgsqlCommand(
                "UPDATE launcher.users SET password_hash = $2, updated_at = now() WHERE id = $1;",
                connection,
                transaction);
            rehash.Parameters.AddWithValue(account.UserId);
            rehash.Parameters.AddWithValue(replacementHash);
            await rehash.ExecuteNonQueryAsync(cancellationToken);
        }

        var now = DateTimeOffset.UtcNow;
        var tokens = tokenGenerator.Create();
        var accessExpiresAt = now.AddMinutes(_options.AccessTokenMinutes);
        var refreshExpiresAt = now.AddDays(_options.RefreshTokenDays);
        await InsertSessionAsync(
            connection,
            transaction,
            Guid.NewGuid(),
            account.UserId,
            tokens,
            accessExpiresAt,
            refreshExpiresAt,
            sourceIp,
            HashUserAgent(userAgent),
            now,
            cancellationToken);
        await RevokeExcessSessionsAsync(
            connection,
            transaction,
            account.UserId,
            now,
            cancellationToken);
        await WriteAccountAuditAsync(
            connection,
            transaction,
            account.UserId,
            "auth.account.logged_in",
            sourceIp,
            new { account.Username },
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return CreateSessionResponse(
            tokens,
            accessExpiresAt,
            refreshExpiresAt,
            account);
    }

    public async Task<HechaoAccount> LinkMinecraftIdentityAsync(
        Guid userId,
        VerifiedMinecraftIdentity identity,
        IPAddress? sourceIp,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await LockIdentityAsync(connection, transaction, identity.MinecraftUuid, cancellationToken);
        var account = await ReadAccountAsync(
            connection,
            transaction,
            userId,
            lockUser: true,
            cancellationToken);
        if (account is null)
        {
            throw new HechaoAccountNotFoundException();
        }

        var identityOwner = await FindUserIdAsync(
            connection,
            transaction,
            identity.MinecraftUuid,
            cancellationToken);
        if (identityOwner is not null && identityOwner != userId)
        {
            if (!await TryTransferLegacyIdentityAsync(
                    connection,
                    transaction,
                    identityOwner.Value,
                    userId,
                    identity.MinecraftUuid,
                    now,
                    cancellationToken))
            {
                throw new MinecraftIdentityAlreadyLinkedException();
            }

            identityOwner = userId;
        }

        if (account.MinecraftUuid is not null &&
            account.MinecraftUuid != identity.MinecraftUuid)
        {
            throw new HechaoAccountMinecraftLinkConflictException();
        }

        var luckPerms = await ReadLuckPermsAccessAsync(
            connection,
            transaction,
            identity.MinecraftUuid,
            cancellationToken);
        if (identityOwner is null)
        {
            await using var insert = new NpgsqlCommand(
                """
                INSERT INTO launcher.minecraft_identities
                    (minecraft_uuid, user_id, minecraft_name, verified_at, updated_at,
                     luckperms_primary_group, luckperms_synced_at)
                VALUES ($1, $2, $3, $4, $4, $5, $6);
                """,
                connection,
                transaction);
            insert.Parameters.AddWithValue(identity.MinecraftUuid);
            insert.Parameters.AddWithValue(userId);
            insert.Parameters.AddWithValue(identity.MinecraftName);
            insert.Parameters.AddWithValue(now);
            insert.Parameters.AddWithValue(luckPerms.PrimaryGroup);
            insert.Parameters.Add(new NpgsqlParameter
            {
                Value = luckPerms.SyncedAt ?? (object)DBNull.Value
            });
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        else
        {
            await using var update = new NpgsqlCommand(
                """
                UPDATE launcher.minecraft_identities
                SET minecraft_name = $2,
                    verified_at = $3,
                    updated_at = $3,
                    luckperms_primary_group = $4,
                    luckperms_synced_at = $5
                WHERE minecraft_uuid = $1 AND user_id = $6;
                """,
                connection,
                transaction);
            update.Parameters.AddWithValue(identity.MinecraftUuid);
            update.Parameters.AddWithValue(identity.MinecraftName);
            update.Parameters.AddWithValue(now);
            update.Parameters.AddWithValue(luckPerms.PrimaryGroup);
            update.Parameters.Add(new NpgsqlParameter
            {
                Value = luckPerms.SyncedAt ?? (object)DBNull.Value
            });
            update.Parameters.AddWithValue(userId);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var updateTier = new NpgsqlCommand(
            "UPDATE launcher.users SET access_tier = $2, updated_at = $3 WHERE id = $1;",
            connection,
            transaction))
        {
            updateTier.Parameters.AddWithValue(userId);
            updateTier.Parameters.AddWithValue(luckPerms.AccessTier.ToString());
            updateTier.Parameters.AddWithValue(now);
            await updateTier.ExecuteNonQueryAsync(cancellationToken);
        }

        await WriteAccountAuditAsync(
            connection,
            transaction,
            userId,
            "auth.minecraft.linked",
            sourceIp,
            new
            {
                MinecraftUuid = identity.MinecraftUuid,
                identity.MinecraftName,
                luckPerms.PrimaryGroup,
                AccessTier = luckPerms.AccessTier.ToString()
            },
            cancellationToken);
        var linkedAccount = await ReadAccountAsync(
            connection,
            transaction,
            userId,
            lockUser: false,
            cancellationToken)
            ?? throw new HechaoAccountNotFoundException();
        await transaction.CommitAsync(cancellationToken);
        return linkedAccount;
    }

    public async Task<AuthSessionResponse> CreateSessionAsync(
        VerifiedMinecraftIdentity identity,
        IPAddress? sourceIp,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var tokens = tokenGenerator.Create();
        var accessExpiresAt = now.AddMinutes(_options.AccessTokenMinutes);
        var refreshExpiresAt = now.AddDays(_options.RefreshTokenDays);
        var sessionId = Guid.NewGuid();

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await LockIdentityAsync(connection, transaction, identity.MinecraftUuid, cancellationToken);
        var luckPerms = await ReadLuckPermsAccessAsync(
            connection,
            transaction,
            identity.MinecraftUuid,
            cancellationToken);

        var userId = await FindUserIdAsync(
            connection,
            transaction,
            identity.MinecraftUuid,
            cancellationToken);

        if (userId is null)
        {
            userId = Guid.NewGuid();
            await InsertUserAndIdentityAsync(
                connection,
                transaction,
                userId.Value,
                identity,
                luckPerms,
                now,
                cancellationToken);
        }
        else
        {
            await UpdateUserAndIdentityAsync(
                connection,
                transaction,
                userId.Value,
                identity,
                luckPerms,
                now,
                cancellationToken);
        }

        await InsertSessionAsync(
            connection,
            transaction,
            sessionId,
            userId.Value,
            tokens,
            accessExpiresAt,
            refreshExpiresAt,
            sourceIp,
            HashUserAgent(userAgent),
            now,
            cancellationToken);

        await RevokeExcessSessionsAsync(
            connection,
            transaction,
            userId.Value,
            now,
            cancellationToken);

        await WriteLoginAuditAsync(
            connection,
            transaction,
            userId.Value,
            identity.MinecraftUuid,
            luckPerms,
            sourceIp,
            cancellationToken);

        var account = await ReadAccountAsync(
            connection,
            transaction,
            userId.Value,
            lockUser: false,
            cancellationToken)
            ?? throw new HechaoAccountNotFoundException();
        await transaction.CommitAsync(cancellationToken);
        return CreateSessionResponse(
            tokens,
            accessExpiresAt,
            refreshExpiresAt,
            account);
    }

    public async Task<AuthenticatedSession?> AuthenticateAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        if (!IsTokenShapeValid(accessToken))
        {
            return null;
        }

        const string sql = """
            SELECT s.id,
                   u.id, u.username, u.display_name, u.email,
                   i.minecraft_uuid, i.minecraft_name,
                   COALESCE(i.luckperms_primary_group, 'default'),
                   u.access_tier, i.luckperms_synced_at, u.created_at
            FROM launcher.auth_sessions s
            JOIN launcher.users u ON u.id = s.user_id
            LEFT JOIN launcher.minecraft_identities i ON i.user_id = u.id
            WHERE s.access_token_hash = $1
              AND s.revoked_at IS NULL
              AND s.access_expires_at > now()
              AND NOT u.is_disabled;
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(SessionTokenGenerator.Hash(accessToken));

        AuthenticatedSession? result = null;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                result = new AuthenticatedSession(
                    reader.GetGuid(0),
                    ReadAccount(reader, offset: 1));
            }
        }

        if (result is not null)
        {
            await using var touch = new NpgsqlCommand(
                """
                UPDATE launcher.auth_sessions
                SET last_seen_at = now()
                WHERE id = $1 AND last_seen_at < now() - interval '5 minutes';
                """,
                connection);
            touch.Parameters.AddWithValue(result.SessionId);
            await touch.ExecuteNonQueryAsync(cancellationToken);
        }

        return result;
    }

    public async Task<AuthSessionResponse?> RefreshSessionAsync(
        string refreshToken,
        CancellationToken cancellationToken)
    {
        if (!IsTokenShapeValid(refreshToken))
        {
            return null;
        }

        const string selectSql = """
            SELECT s.id,
                   u.id, u.username, u.display_name, u.email,
                   i.minecraft_uuid, i.minecraft_name,
                   COALESCE(i.luckperms_primary_group, 'default'),
                   u.access_tier, i.luckperms_synced_at, u.created_at
            FROM launcher.auth_sessions s
            JOIN launcher.users u ON u.id = s.user_id
            LEFT JOIN launcher.minecraft_identities i ON i.user_id = u.id
            WHERE s.refresh_token_hash = $1
              AND s.revoked_at IS NULL
              AND s.refresh_expires_at > now()
              AND NOT u.is_disabled
            FOR UPDATE OF s;
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var select = new NpgsqlCommand(selectSql, connection, transaction);
        select.Parameters.AddWithValue(SessionTokenGenerator.Hash(refreshToken));

        AuthenticatedSession? session = null;
        await using (var reader = await select.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                session = new AuthenticatedSession(
                    reader.GetGuid(0),
                    ReadAccount(reader, offset: 1));
            }
        }

        if (session is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        var tokens = tokenGenerator.Create();
        var accessExpiresAt = now.AddMinutes(_options.AccessTokenMinutes);
        var refreshExpiresAt = now.AddDays(_options.RefreshTokenDays);

        await using var update = new NpgsqlCommand(
            """
            UPDATE launcher.auth_sessions
            SET access_token_hash = $2,
                refresh_token_hash = $3,
                access_expires_at = $4,
                refresh_expires_at = $5,
                last_seen_at = $6
            WHERE id = $1;
            """,
            connection,
            transaction);
        update.Parameters.AddWithValue(session.SessionId);
        update.Parameters.AddWithValue(SessionTokenGenerator.Hash(tokens.AccessToken));
        update.Parameters.AddWithValue(SessionTokenGenerator.Hash(tokens.RefreshToken));
        update.Parameters.AddWithValue(accessExpiresAt);
        update.Parameters.AddWithValue(refreshExpiresAt);
        update.Parameters.AddWithValue(now);
        await update.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return CreateSessionResponse(
            tokens,
            accessExpiresAt,
            refreshExpiresAt,
            session.Account);
    }

    public async Task RevokeSessionAsync(string accessToken, CancellationToken cancellationToken)
    {
        if (!IsTokenShapeValid(accessToken))
        {
            return;
        }

        await using var command = dataSource.CreateCommand(
            """
            UPDATE launcher.auth_sessions
            SET revoked_at = now()
            WHERE access_token_hash = $1 AND revoked_at IS NULL;
            """);
        command.Parameters.AddWithValue(SessionTokenGenerator.Hash(accessToken));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task LockIdentityAsync(
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

    private static async Task<LuckPermsAccess> ReadLuckPermsAccessAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid minecraftUuid,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT COALESCE(s.primary_group, 'default'),
                   s.source_captured_at,
                   COALESCE(m.access_tier, 'Member')
            FROM (SELECT 1) AS seed
            LEFT JOIN launcher.luckperms_player_snapshots s ON s.minecraft_uuid = $1
            LEFT JOIN launcher.luckperms_group_tier_mappings m
                ON m.primary_group = COALESCE(s.primary_group, 'default');
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(minecraftUuid);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new LuckPermsAccess(
            reader.GetString(0),
            reader.IsDBNull(1) ? null : ToDateTimeOffset(reader.GetDateTime(1)),
            Enum.Parse<AccessTier>(reader.GetString(2), ignoreCase: true));
    }

    private static async Task<Guid?> FindUserIdAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid minecraftUuid,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT user_id FROM launcher.minecraft_identities WHERE minecraft_uuid = $1 FOR UPDATE;",
            connection,
            transaction);
        command.Parameters.AddWithValue(minecraftUuid);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is Guid userId ? userId : null;
    }

    private static async Task<bool> TryTransferLegacyIdentityAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid legacyUserId,
        Guid targetUserId,
        Guid minecraftUuid,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var selectLegacyUser = new NpgsqlCommand(
            """
            SELECT username, password_hash
            FROM launcher.users
            WHERE id = $1 AND NOT is_disabled
            FOR UPDATE;
            """,
            connection,
            transaction);
        selectLegacyUser.Parameters.AddWithValue(legacyUserId);

        string? username = null;
        string? passwordHash = null;
        await using (var reader = await selectLegacyUser.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                username = reader.GetString(0);
                passwordHash = reader.IsDBNull(1) ? null : reader.GetString(1);
            }
        }

        if (passwordHash is not null ||
            username is null ||
            !username.StartsWith("legacy_", StringComparison.Ordinal))
        {
            return false;
        }

        await using var transfer = new NpgsqlCommand(
            """
            UPDATE launcher.minecraft_identities
            SET user_id = $2, updated_at = $4
            WHERE minecraft_uuid = $1 AND user_id = $3;

            UPDATE launcher.auth_sessions
            SET revoked_at = $4
            WHERE user_id = $3 AND revoked_at IS NULL;

            UPDATE launcher.users
            SET is_disabled = true, updated_at = $4
            WHERE id = $3;
            """,
            connection,
            transaction);
        transfer.Parameters.AddWithValue(minecraftUuid);
        transfer.Parameters.AddWithValue(targetUserId);
        transfer.Parameters.AddWithValue(legacyUserId);
        transfer.Parameters.AddWithValue(now);
        return await transfer.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    private static async Task InsertUserAndIdentityAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid userId,
        VerifiedMinecraftIdentity identity,
        LuckPermsAccess luckPerms,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using (var insertUser = new NpgsqlCommand(
            """
            INSERT INTO launcher.users
                (id, username, display_name, access_tier, created_at, updated_at)
            VALUES ($1, $2, $3, $4, $5, $5);
            """,
            connection,
            transaction))
        {
            insertUser.Parameters.AddWithValue(userId);
            insertUser.Parameters.AddWithValue($"legacy_{userId:N}");
            insertUser.Parameters.AddWithValue(identity.MinecraftName);
            insertUser.Parameters.AddWithValue(luckPerms.AccessTier.ToString());
            insertUser.Parameters.AddWithValue(now);
            await insertUser.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var insertIdentity = new NpgsqlCommand(
            """
            INSERT INTO launcher.minecraft_identities
                (minecraft_uuid, user_id, minecraft_name, verified_at, updated_at,
                 luckperms_primary_group, luckperms_synced_at)
            VALUES ($1, $2, $3, $4, $4, $5, $6);
            """,
            connection,
            transaction);
        insertIdentity.Parameters.AddWithValue(identity.MinecraftUuid);
        insertIdentity.Parameters.AddWithValue(userId);
        insertIdentity.Parameters.AddWithValue(identity.MinecraftName);
        insertIdentity.Parameters.AddWithValue(now);
        insertIdentity.Parameters.AddWithValue(luckPerms.PrimaryGroup);
        insertIdentity.Parameters.Add(new NpgsqlParameter { Value = luckPerms.SyncedAt ?? (object)DBNull.Value });
        await insertIdentity.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpdateUserAndIdentityAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid userId,
        VerifiedMinecraftIdentity identity,
        LuckPermsAccess luckPerms,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            UPDATE launcher.users
            SET display_name = $2, access_tier = $3, updated_at = $4
            WHERE id = $1;

            UPDATE launcher.minecraft_identities
            SET minecraft_name = $2,
                verified_at = $4,
                updated_at = $4,
                luckperms_primary_group = $5,
                luckperms_synced_at = $6
            WHERE minecraft_uuid = $7;
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(identity.MinecraftName);
        command.Parameters.AddWithValue(luckPerms.AccessTier.ToString());
        command.Parameters.AddWithValue(now);
        command.Parameters.AddWithValue(luckPerms.PrimaryGroup);
        command.Parameters.Add(new NpgsqlParameter { Value = luckPerms.SyncedAt ?? (object)DBNull.Value });
        command.Parameters.AddWithValue(identity.MinecraftUuid);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertSessionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid sessionId,
        Guid userId,
        SessionTokenPair tokens,
        DateTimeOffset accessExpiresAt,
        DateTimeOffset refreshExpiresAt,
        IPAddress? sourceIp,
        byte[]? userAgentHash,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO launcher.auth_sessions
                (id, user_id, access_token_hash, refresh_token_hash,
                 access_expires_at, refresh_expires_at, created_at, last_seen_at,
                 source_ip, user_agent_hash)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $7, $8, $9);
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(sessionId);
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(SessionTokenGenerator.Hash(tokens.AccessToken));
        command.Parameters.AddWithValue(SessionTokenGenerator.Hash(tokens.RefreshToken));
        command.Parameters.AddWithValue(accessExpiresAt);
        command.Parameters.AddWithValue(refreshExpiresAt);
        command.Parameters.AddWithValue(now);
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Inet,
            Value = sourceIp ?? (object)DBNull.Value
        });
        command.Parameters.Add(new NpgsqlParameter { Value = userAgentHash ?? (object)DBNull.Value });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task RevokeExcessSessionsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            UPDATE launcher.auth_sessions
            SET revoked_at = $2
            WHERE id IN (
                SELECT id
                FROM launcher.auth_sessions
                WHERE user_id = $1 AND revoked_at IS NULL
                ORDER BY created_at DESC
                OFFSET $3
            );
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(now);
        command.Parameters.AddWithValue(MaximumActiveSessionsPerUser);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task WriteLoginAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid userId,
        Guid minecraftUuid,
        LuckPermsAccess luckPerms,
        IPAddress? sourceIp,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO launcher.audit_logs
                (actor_user_id, action, target_type, target_id, source_ip, after_data)
            VALUES ($1, 'auth.session.created', 'minecraft_identity', $2, $3, $4);
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(minecraftUuid.ToString("D"));
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Inet,
            Value = sourceIp ?? (object)DBNull.Value
        });
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Jsonb,
            Value = JsonSerializer.Serialize(new
            {
                luckPerms.PrimaryGroup,
                AccessTier = luckPerms.AccessTier.ToString()
            })
        });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task WriteAccountAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid userId,
        string action,
        IPAddress? sourceIp,
        object afterData,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO launcher.audit_logs
                (actor_user_id, action, target_type, target_id, source_ip, after_data)
            VALUES ($1, $2, 'hechao_account', $3, $4, $5);
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(action);
        command.Parameters.AddWithValue(userId.ToString("D"));
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Inet,
            Value = sourceIp ?? (object)DBNull.Value
        });
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Jsonb,
            Value = JsonSerializer.Serialize(afterData)
        });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<HechaoAccount?> ReadAccountAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid userId,
        bool lockUser,
        CancellationToken cancellationToken)
    {
        var sql = $"""
            SELECT u.id, u.username, u.display_name, u.email,
                   i.minecraft_uuid, i.minecraft_name,
                   COALESCE(i.luckperms_primary_group, 'default'),
                   u.access_tier, i.luckperms_synced_at, u.created_at
            FROM launcher.users u
            LEFT JOIN launcher.minecraft_identities i ON i.user_id = u.id
            WHERE u.id = $1 AND NOT u.is_disabled
            {(lockUser ? "FOR UPDATE OF u" : string.Empty)};
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadAccount(reader, offset: 0)
            : null;
    }

    private static HechaoAccount ReadAccount(NpgsqlDataReader reader, int offset)
    {
        return new HechaoAccount(
            reader.GetGuid(offset),
            reader.GetString(offset + 1),
            reader.GetString(offset + 2),
            reader.IsDBNull(offset + 3) ? null : reader.GetString(offset + 3),
            reader.IsDBNull(offset + 4) ? null : reader.GetGuid(offset + 4),
            reader.IsDBNull(offset + 5) ? null : reader.GetString(offset + 5),
            reader.GetString(offset + 6),
            Enum.Parse<AccessTier>(reader.GetString(offset + 7), ignoreCase: true),
            reader.IsDBNull(offset + 8)
                ? null
                : ToDateTimeOffset(reader.GetDateTime(offset + 8)),
            ToDateTimeOffset(reader.GetDateTime(offset + 9)));
    }

    private static AuthSessionResponse CreateSessionResponse(
        SessionTokenPair tokens,
        DateTimeOffset accessExpiresAt,
        DateTimeOffset refreshExpiresAt,
        HechaoAccount account)
    {
        return new AuthSessionResponse(
            tokens.AccessToken,
            accessExpiresAt,
            tokens.RefreshToken,
            refreshExpiresAt,
            account);
    }

    private static byte[]? HashUserAgent(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return null;
        }

        var normalized = userAgent.Length <= 512 ? userAgent : userAgent[..512];
        return SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
    }

    private static bool IsTokenShapeValid(string token)
    {
        return token.Length == 43 && token.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
    }

    private static DateTimeOffset ToDateTimeOffset(DateTime value)
    {
        return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }

    private sealed record LuckPermsAccess(
        string PrimaryGroup,
        DateTimeOffset? SyncedAt,
        AccessTier AccessTier);
}

public sealed record AuthenticatedSession(Guid SessionId, HechaoAccount Account);

public sealed class HechaoAccountConflictException(string field) : Exception
{
    public string Field { get; } = field;
}

public sealed class HechaoAccountNotFoundException : Exception;
public sealed class MinecraftIdentityAlreadyLinkedException : Exception;
public sealed class HechaoAccountMinecraftLinkConflictException : Exception;

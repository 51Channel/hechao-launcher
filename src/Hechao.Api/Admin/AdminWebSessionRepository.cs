using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hechao.Contracts;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;
using OtpNet;

namespace Hechao.Api.Admin;

public sealed class AdminWebSessionRepository
{
    private const int MaximumActiveSessionsPerUser = 5;
    private readonly NpgsqlDataSource _dataSource;
    private readonly AdminWebTokenGenerator _tokenGenerator;
    private readonly AdminTotpService _totpService;
    private readonly IDataProtector _mfaProtector;
    private readonly AdminWebOptions _options;
    private readonly TimeProvider _timeProvider;

    public AdminWebSessionRepository(
        NpgsqlDataSource dataSource,
        AdminWebTokenGenerator tokenGenerator,
        AdminTotpService totpService,
        IDataProtectionProvider dataProtectionProvider,
        IOptions<AdminWebOptions> options,
        TimeProvider timeProvider)
    {
        _dataSource = dataSource;
        _tokenGenerator = tokenGenerator;
        _totpService = totpService;
        _mfaProtector = dataProtectionProvider.CreateProtector(
            "Hechao.AdminWeb.TotpSecret.v1");
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    public async Task<AdminLoginTicketResult?> CreateLoginTicketAsync(
        Guid userId,
        IPAddress? sourceIp,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var expiresAt = now.AddSeconds(_options.TicketSeconds);
        var token = _tokenGenerator.Create();
        var ticketId = Guid.NewGuid();

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var consumePrevious = new NpgsqlCommand(
            """
            UPDATE launcher.admin_login_tickets
            SET consumed_at = $2
            WHERE user_id = $1
              AND consumed_at IS NULL;
            """,
            connection,
            transaction))
        {
            consumePrevious.Parameters.AddWithValue(userId);
            consumePrevious.Parameters.AddWithValue(now);
            await consumePrevious.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var insert = new NpgsqlCommand(
            """
            INSERT INTO launcher.admin_login_tickets
                (id, user_id, token_hash, expires_at, created_at, source_ip, user_agent_hash)
            SELECT $1, u.id, $2, $3, $4, $5, $6
            FROM launcher.users u
            WHERE u.id = $7
              AND NOT u.is_disabled
              AND u.access_tier = 'Administrator';
            """,
            connection,
            transaction))
        {
            insert.Parameters.AddWithValue(ticketId);
            insert.Parameters.AddWithValue(AdminWebTokenGenerator.Hash(token));
            insert.Parameters.AddWithValue(expiresAt);
            insert.Parameters.AddWithValue(now);
            AddInetParameter(insert, sourceIp);
            AddNullableBytesParameter(insert, HashUserAgent(userAgent));
            insert.Parameters.AddWithValue(userId);
            if (await insert.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }
        }

        await WriteAuditAsync(
            connection,
            transaction,
            userId,
            sourceIp,
            "admin.login_ticket.created",
            "admin_login_ticket",
            ticketId.ToString("D"),
            new { expiresAt },
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new AdminLoginTicketResult(token, expiresAt);
    }

    public async Task<AdminTicketRedeemResult> RedeemLoginTicketAsync(
        string ticket,
        IPAddress? sourceIp,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        if (!AdminWebTokenGenerator.IsShapeValid(ticket))
        {
            return AdminTicketRedeemResult.Invalid;
        }

        const string selectSql = """
            SELECT ticket.id, ticket.user_id, ticket.source_ip,
                   user_account.display_name, identity.minecraft_uuid,
                   identity.minecraft_name, identity.luckperms_primary_group,
                   user_account.access_tier, identity.luckperms_synced_at,
                   EXISTS (
                       SELECT 1
                       FROM launcher.admin_mfa_credentials credential
                       WHERE credential.user_id = ticket.user_id
                   )
            FROM launcher.admin_login_tickets ticket
            JOIN launcher.users user_account ON user_account.id = ticket.user_id
            JOIN launcher.minecraft_identities identity ON identity.user_id = ticket.user_id
            WHERE ticket.token_hash = $1
              AND ticket.consumed_at IS NULL
              AND ticket.expires_at > now()
              AND NOT user_account.is_disabled
              AND user_account.access_tier = 'Administrator'
            FOR UPDATE OF ticket;
            """;

        var now = _timeProvider.GetUtcNow();
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var select = new NpgsqlCommand(selectSql, connection, transaction);
        select.Parameters.AddWithValue(AdminWebTokenGenerator.Hash(ticket));

        Guid ticketId;
        Guid userId;
        IPAddress? ticketSourceIp;
        AuthenticatedPlayer player;
        bool mfaConfigured;
        await using (var reader = await select.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
            {
                return AdminTicketRedeemResult.Invalid;
            }

            ticketId = reader.GetGuid(0);
            userId = reader.GetGuid(1);
            ticketSourceIp = reader.IsDBNull(2)
                ? null
                : reader.GetFieldValue<IPAddress>(2);
            player = new AuthenticatedPlayer(
                userId,
                reader.GetGuid(4),
                reader.GetString(5),
                reader.GetString(6),
                Enum.Parse<AccessTier>(reader.GetString(7), ignoreCase: true),
                reader.IsDBNull(8) ? null : ToDateTimeOffset(reader.GetDateTime(8)));
            mfaConfigured = reader.GetBoolean(9);
        }

        if (ticketSourceIp is not null && !ticketSourceIp.Equals(sourceIp))
        {
            return AdminTicketRedeemResult.SourceMismatch;
        }

        await using (var consume = new NpgsqlCommand(
            """
            UPDATE launcher.admin_login_tickets
            SET consumed_at = $2
            WHERE id = $1 AND consumed_at IS NULL;
            """,
            connection,
            transaction))
        {
            consume.Parameters.AddWithValue(ticketId);
            consume.Parameters.AddWithValue(now);
            if (await consume.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                return AdminTicketRedeemResult.Invalid;
            }
        }

        var sessionId = Guid.NewGuid();
        var sessionToken = _tokenGenerator.Create();
        var expiresAt = now.AddMinutes(_options.SessionMinutes);
        await using (var insert = new NpgsqlCommand(
            """
            INSERT INTO launcher.admin_web_sessions
                (id, user_id, token_hash, expires_at, created_at, last_seen_at,
                 source_ip, user_agent_hash)
            VALUES ($1, $2, $3, $4, $5, $5, $6, $7);
            """,
            connection,
            transaction))
        {
            insert.Parameters.AddWithValue(sessionId);
            insert.Parameters.AddWithValue(userId);
            insert.Parameters.AddWithValue(AdminWebTokenGenerator.Hash(sessionToken));
            insert.Parameters.AddWithValue(expiresAt);
            insert.Parameters.AddWithValue(now);
            AddInetParameter(insert, sourceIp);
            AddNullableBytesParameter(insert, HashUserAgent(userAgent));
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await RevokeExcessSessionsAsync(
            connection,
            transaction,
            userId,
            now,
            cancellationToken);
        await WriteAuditAsync(
            connection,
            transaction,
            userId,
            sourceIp,
            "admin.web_session.created",
            "admin_web_session",
            sessionId.ToString("D"),
            new { expiresAt, mfaConfigured },
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var state = new AdminWebAuthenticationState(
            sessionId,
            player,
            mfaConfigured,
            MfaVerifiedAt: null,
            expiresAt);
        return new AdminTicketRedeemResult(
            AdminTicketRedeemStatus.Success,
            sessionToken,
            state);
    }

    public async Task<AdminWebAuthenticationState?> AuthenticateAsync(
        string? sessionToken,
        CancellationToken cancellationToken)
    {
        if (!AdminWebTokenGenerator.IsShapeValid(sessionToken))
        {
            return null;
        }

        const string sql = """
            SELECT session.id, user_account.id, identity.minecraft_uuid,
                   identity.minecraft_name, identity.luckperms_primary_group,
                   user_account.access_tier, identity.luckperms_synced_at,
                   EXISTS (
                       SELECT 1
                       FROM launcher.admin_mfa_credentials credential
                       WHERE credential.user_id = session.user_id
                   ),
                   session.mfa_verified_at, session.expires_at
            FROM launcher.admin_web_sessions session
            JOIN launcher.users user_account ON user_account.id = session.user_id
            JOIN launcher.minecraft_identities identity ON identity.user_id = session.user_id
            WHERE session.token_hash = $1
              AND session.revoked_at IS NULL
              AND session.expires_at > now()
              AND NOT user_account.is_disabled
              AND user_account.access_tier = 'Administrator';
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(AdminWebTokenGenerator.Hash(sessionToken!));

        AdminWebAuthenticationState? state = null;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                var player = new AuthenticatedPlayer(
                    reader.GetGuid(1),
                    reader.GetGuid(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    Enum.Parse<AccessTier>(reader.GetString(5), ignoreCase: true),
                    reader.IsDBNull(6) ? null : ToDateTimeOffset(reader.GetDateTime(6)));
                state = new AdminWebAuthenticationState(
                    reader.GetGuid(0),
                    player,
                    reader.GetBoolean(7),
                    reader.IsDBNull(8) ? null : ToDateTimeOffset(reader.GetDateTime(8)),
                    ToDateTimeOffset(reader.GetDateTime(9)));
            }
        }

        if (state is not null)
        {
            await using var touch = new NpgsqlCommand(
                """
                UPDATE launcher.admin_web_sessions
                SET last_seen_at = now()
                WHERE id = $1
                  AND last_seen_at < now() - interval '5 minutes';
                """,
                connection);
            touch.Parameters.AddWithValue(state.SessionId);
            await touch.ExecuteNonQueryAsync(cancellationToken);
        }

        return state;
    }

    public async Task<AdminMfaEnrollmentResult> BeginMfaEnrollmentAsync(
        AdminWebAuthenticationState state,
        IPAddress? sourceIp,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var expiresAt = now.AddMinutes(_options.EnrollmentMinutes);
        var setup = _totpService.CreateSetup(
            _options.TotpIssuer,
            state.Player.MinecraftName);
        var protectedSecret = _mfaProtector.Protect(setup.SecretKey);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var exists = new NpgsqlCommand(
            "SELECT 1 FROM launcher.admin_mfa_credentials WHERE user_id = $1;",
            connection,
            transaction))
        {
            exists.Parameters.AddWithValue(state.Player.UserId);
            if (await exists.ExecuteScalarAsync(cancellationToken) is not null)
            {
                return AdminMfaEnrollmentResult.AlreadyConfigured;
            }
        }

        await using (var upsert = new NpgsqlCommand(
            """
            INSERT INTO launcher.admin_mfa_enrollments
                (user_id, secret_protected, expires_at, created_at)
            VALUES ($1, $2, $3, $4)
            ON CONFLICT (user_id) DO UPDATE
            SET secret_protected = EXCLUDED.secret_protected,
                expires_at = EXCLUDED.expires_at,
                created_at = EXCLUDED.created_at;
            """,
            connection,
            transaction))
        {
            upsert.Parameters.AddWithValue(state.Player.UserId);
            upsert.Parameters.AddWithValue(protectedSecret);
            upsert.Parameters.AddWithValue(expiresAt);
            upsert.Parameters.AddWithValue(now);
            await upsert.ExecuteNonQueryAsync(cancellationToken);
        }

        await WriteAuditAsync(
            connection,
            transaction,
            state.Player.UserId,
            sourceIp,
            "admin.mfa.enrollment.started",
            "user",
            state.Player.UserId.ToString("D"),
            new { expiresAt },
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new AdminMfaEnrollmentResult(
            AdminMfaOperationStatus.Success,
            new AdminMfaEnrollment(
                setup.SecretKey,
                setup.OtpAuthUri,
                setup.QrCodeDataUri,
                expiresAt));
    }

    public async Task<AdminMfaVerificationResult> CompleteMfaEnrollmentAsync(
        AdminWebAuthenticationState state,
        string? suppliedCode,
        IPAddress? sourceIp,
        CancellationToken cancellationToken)
    {
        const string selectSql = """
            SELECT secret_protected, expires_at
            FROM launcher.admin_mfa_enrollments
            WHERE user_id = $1
            FOR UPDATE;
            """;

        var now = _timeProvider.GetUtcNow();
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var select = new NpgsqlCommand(selectSql, connection, transaction);
        select.Parameters.AddWithValue(state.Player.UserId);

        string protectedSecret;
        DateTimeOffset expiresAt;
        await using (var reader = await select.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
            {
                return AdminMfaVerificationResult.NotConfigured;
            }

            protectedSecret = reader.GetString(0);
            expiresAt = ToDateTimeOffset(reader.GetDateTime(1));
        }

        if (expiresAt <= now)
        {
            return AdminMfaVerificationResult.Expired;
        }

        var secret = UnprotectSecret(protectedSecret);
        if (secret is null)
        {
            return AdminMfaVerificationResult.Unavailable;
        }

        var verification = _totpService.Verify(secret, suppliedCode);
        if (!verification.IsValid)
        {
            return AdminMfaVerificationResult.InvalidCode;
        }

        var recoveryCodes = _totpService.CreateRecoveryCodes();
        await using (var insertCredential = new NpgsqlCommand(
            """
            INSERT INTO launcher.admin_mfa_credentials
                (user_id, secret_protected, recovery_code_hashes,
                 last_accepted_time_window, enabled_at, updated_at)
            VALUES ($1, $2, $3, $4, $5, $5);
            """,
            connection,
            transaction))
        {
            insertCredential.Parameters.AddWithValue(state.Player.UserId);
            insertCredential.Parameters.AddWithValue(protectedSecret);
            AddJsonParameter(
                insertCredential,
                JsonSerializer.Serialize(recoveryCodes.Hashes));
            insertCredential.Parameters.AddWithValue(verification.TimeWindowUsed);
            insertCredential.Parameters.AddWithValue(now);
            await insertCredential.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var deleteEnrollment = new NpgsqlCommand(
            "DELETE FROM launcher.admin_mfa_enrollments WHERE user_id = $1;",
            connection,
            transaction))
        {
            deleteEnrollment.Parameters.AddWithValue(state.Player.UserId);
            await deleteEnrollment.ExecuteNonQueryAsync(cancellationToken);
        }

        if (!await MarkSessionMfaVerifiedAsync(
                connection,
                transaction,
                state,
                now,
                cancellationToken))
        {
            return AdminMfaVerificationResult.InvalidSession;
        }

        await WriteAuditAsync(
            connection,
            transaction,
            state.Player.UserId,
            sourceIp,
            "admin.mfa.enabled",
            "user",
            state.Player.UserId.ToString("D"),
            new { recoveryCodeCount = recoveryCodes.Codes.Count },
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new AdminMfaVerificationResult(
            AdminMfaOperationStatus.Success,
            now,
            recoveryCodes.Codes,
            RecoveryCodeUsed: false);
    }

    public async Task<AdminMfaVerificationResult> VerifyMfaAsync(
        AdminWebAuthenticationState state,
        string? suppliedCode,
        IPAddress? sourceIp,
        CancellationToken cancellationToken)
    {
        const string selectSql = """
            SELECT secret_protected, recovery_code_hashes::text,
                   last_accepted_time_window
            FROM launcher.admin_mfa_credentials
            WHERE user_id = $1
            FOR UPDATE;
            """;

        var now = _timeProvider.GetUtcNow();
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var select = new NpgsqlCommand(selectSql, connection, transaction);
        select.Parameters.AddWithValue(state.Player.UserId);

        string protectedSecret;
        List<string> recoveryHashes;
        long? lastAcceptedTimeWindow;
        await using (var reader = await select.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
            {
                return AdminMfaVerificationResult.NotConfigured;
            }

            protectedSecret = reader.GetString(0);
            recoveryHashes = JsonSerializer.Deserialize<List<string>>(reader.GetString(1)) ?? [];
            lastAcceptedTimeWindow = reader.IsDBNull(2) ? null : reader.GetInt64(2);
        }

        var secret = UnprotectSecret(protectedSecret);
        if (secret is null)
        {
            return AdminMfaVerificationResult.Unavailable;
        }

        var verification = _totpService.Verify(secret, suppliedCode);
        var recoveryCodeUsed = false;
        var acceptedTimeWindow = lastAcceptedTimeWindow;
        if (verification.IsValid &&
            (lastAcceptedTimeWindow is null ||
             verification.TimeWindowUsed > lastAcceptedTimeWindow.Value))
        {
            acceptedTimeWindow = verification.TimeWindowUsed;
        }
        else
        {
            var normalizedRecoveryCode = AdminTotpService.NormalizeRecoveryCode(suppliedCode);
            if (normalizedRecoveryCode is null)
            {
                return AdminMfaVerificationResult.InvalidCode;
            }

            var candidateHash = AdminTotpService.HashRecoveryCode(normalizedRecoveryCode);
            var matchingIndex = recoveryHashes.FindIndex(expected =>
                AdminTotpService.FixedTimeEqualsRecoveryHash(expected, candidateHash));
            if (matchingIndex < 0)
            {
                return AdminMfaVerificationResult.InvalidCode;
            }

            recoveryHashes.RemoveAt(matchingIndex);
            recoveryCodeUsed = true;
        }

        await using (var updateCredential = new NpgsqlCommand(
            """
            UPDATE launcher.admin_mfa_credentials
            SET recovery_code_hashes = $2,
                last_accepted_time_window = $3,
                updated_at = $4
            WHERE user_id = $1;
            """,
            connection,
            transaction))
        {
            updateCredential.Parameters.AddWithValue(state.Player.UserId);
            AddJsonParameter(
                updateCredential,
                JsonSerializer.Serialize(recoveryHashes));
            updateCredential.Parameters.Add(new NpgsqlParameter
            {
                Value = acceptedTimeWindow ?? (object)DBNull.Value
            });
            updateCredential.Parameters.AddWithValue(now);
            await updateCredential.ExecuteNonQueryAsync(cancellationToken);
        }

        if (!await MarkSessionMfaVerifiedAsync(
                connection,
                transaction,
                state,
                now,
                cancellationToken))
        {
            return AdminMfaVerificationResult.InvalidSession;
        }

        await WriteAuditAsync(
            connection,
            transaction,
            state.Player.UserId,
            sourceIp,
            recoveryCodeUsed
                ? "admin.mfa.recovery_code_used"
                : "admin.mfa.verified",
            "admin_web_session",
            state.SessionId.ToString("D"),
            new { recoveryCodeUsed, recoveryCodesRemaining = recoveryHashes.Count },
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new AdminMfaVerificationResult(
            AdminMfaOperationStatus.Success,
            now,
            RecoveryCodes: null,
            recoveryCodeUsed);
    }

    public async Task RevokeSessionAsync(
        AdminWebAuthenticationState state,
        IPAddress? sourceIp,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var revoke = new NpgsqlCommand(
            """
            UPDATE launcher.admin_web_sessions
            SET revoked_at = $2
            WHERE id = $1 AND revoked_at IS NULL;
            """,
            connection,
            transaction))
        {
            revoke.Parameters.AddWithValue(state.SessionId);
            revoke.Parameters.AddWithValue(now);
            await revoke.ExecuteNonQueryAsync(cancellationToken);
        }

        await WriteAuditAsync(
            connection,
            transaction,
            state.Player.UserId,
            sourceIp,
            "admin.web_session.revoked",
            "admin_web_session",
            state.SessionId.ToString("D"),
            after: null,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private byte[]? UnprotectSecret(string protectedSecret)
    {
        try
        {
            return Base32Encoding.ToBytes(_mfaProtector.Unprotect(protectedSecret));
        }
        catch (CryptographicException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static async Task<bool> MarkSessionMfaVerifiedAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AdminWebAuthenticationState state,
        DateTimeOffset verifiedAt,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            UPDATE launcher.admin_web_sessions
            SET mfa_verified_at = $3,
                last_seen_at = $3
            WHERE id = $1
              AND user_id = $2
              AND revoked_at IS NULL
              AND expires_at > $3;
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(state.SessionId);
        command.Parameters.AddWithValue(state.Player.UserId);
        command.Parameters.AddWithValue(verifiedAt);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private static async Task RevokeExcessSessionsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid userId,
        DateTimeOffset revokedAt,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            UPDATE launcher.admin_web_sessions
            SET revoked_at = $2
            WHERE id IN (
                SELECT id
                FROM launcher.admin_web_sessions
                WHERE user_id = $1
                  AND revoked_at IS NULL
                ORDER BY created_at DESC
                OFFSET $3
            );
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(revokedAt);
        command.Parameters.AddWithValue(MaximumActiveSessionsPerUser);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task WriteAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid actorUserId,
        IPAddress? sourceIp,
        string action,
        string targetType,
        string targetId,
        object? after,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO launcher.audit_logs
                (actor_user_id, action, target_type, target_id, source_ip, after_data)
            VALUES ($1, $2, $3, $4, $5, $6);
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(actorUserId);
        command.Parameters.AddWithValue(action);
        command.Parameters.AddWithValue(targetType);
        command.Parameters.AddWithValue(targetId);
        AddInetParameter(command, sourceIp);
        var afterParameter = command.Parameters.Add("afterData", NpgsqlDbType.Jsonb);
        afterParameter.Value = after is null
            ? DBNull.Value
            : JsonSerializer.Serialize(after);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddInetParameter(NpgsqlCommand command, IPAddress? sourceIp)
    {
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Inet,
            Value = sourceIp ?? (object)DBNull.Value
        });
    }

    private static void AddNullableBytesParameter(NpgsqlCommand command, byte[]? value)
    {
        command.Parameters.Add(new NpgsqlParameter
        {
            Value = value ?? (object)DBNull.Value
        });
    }

    private static void AddJsonParameter(NpgsqlCommand command, string json)
    {
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Jsonb,
            Value = json
        });
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

    private static DateTimeOffset ToDateTimeOffset(DateTime value)
    {
        return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }
}

public sealed record AdminLoginTicketResult(
    string Token,
    DateTimeOffset ExpiresAt);

public enum AdminTicketRedeemStatus
{
    Success,
    Invalid,
    SourceMismatch
}

public sealed record AdminTicketRedeemResult(
    AdminTicketRedeemStatus Status,
    string? SessionToken = null,
    AdminWebAuthenticationState? State = null)
{
    public static AdminTicketRedeemResult Invalid { get; } =
        new(AdminTicketRedeemStatus.Invalid);

    public static AdminTicketRedeemResult SourceMismatch { get; } =
        new(AdminTicketRedeemStatus.SourceMismatch);
}

public sealed record AdminWebAuthenticationState(
    Guid SessionId,
    AuthenticatedPlayer Player,
    bool MfaConfigured,
    DateTimeOffset? MfaVerifiedAt,
    DateTimeOffset ExpiresAt)
{
    public bool MfaVerified => MfaVerifiedAt is not null;
}

public enum AdminMfaOperationStatus
{
    Success,
    AlreadyConfigured,
    NotConfigured,
    Expired,
    InvalidCode,
    InvalidSession,
    Unavailable
}

public sealed record AdminMfaEnrollment(
    string SecretKey,
    string OtpAuthUri,
    string QrCodeDataUri,
    DateTimeOffset ExpiresAt);

public sealed record AdminMfaEnrollmentResult(
    AdminMfaOperationStatus Status,
    AdminMfaEnrollment? Enrollment = null)
{
    public static AdminMfaEnrollmentResult AlreadyConfigured { get; } =
        new(AdminMfaOperationStatus.AlreadyConfigured);
}

public sealed record AdminMfaVerificationResult(
    AdminMfaOperationStatus Status,
    DateTimeOffset? VerifiedAt = null,
    IReadOnlyList<string>? RecoveryCodes = null,
    bool RecoveryCodeUsed = false)
{
    public static AdminMfaVerificationResult NotConfigured { get; } =
        new(AdminMfaOperationStatus.NotConfigured);

    public static AdminMfaVerificationResult Expired { get; } =
        new(AdminMfaOperationStatus.Expired);

    public static AdminMfaVerificationResult InvalidCode { get; } =
        new(AdminMfaOperationStatus.InvalidCode);

    public static AdminMfaVerificationResult InvalidSession { get; } =
        new(AdminMfaOperationStatus.InvalidSession);

    public static AdminMfaVerificationResult Unavailable { get; } =
        new(AdminMfaOperationStatus.Unavailable);
}

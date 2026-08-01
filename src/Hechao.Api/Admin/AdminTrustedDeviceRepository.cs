using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Hechao.Api.Admin;

public sealed class AdminTrustedDeviceRepository
{
    private const int MaximumActiveDevicesPerUser = 3;
    private readonly NpgsqlDataSource _dataSource;
    private readonly AdminWebTokenGenerator _tokenGenerator;
    private readonly AdminWebOptions _options;
    private readonly TimeProvider _timeProvider;

    public AdminTrustedDeviceRepository(
        NpgsqlDataSource dataSource,
        AdminWebTokenGenerator tokenGenerator,
        IOptions<AdminWebOptions> options,
        TimeProvider timeProvider)
    {
        _dataSource = dataSource;
        _tokenGenerator = tokenGenerator;
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    public async Task<AdminTrustedDeviceIssueResult?> CreateAsync(
        AdminWebAuthenticationState state,
        IPAddress? sourceIp,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        if (!state.MfaVerified)
        {
            return null;
        }

        var now = _timeProvider.GetUtcNow();
        var expiresAt = now.AddDays(_options.TrustedDeviceDays);
        var token = _tokenGenerator.Create();
        var deviceId = Guid.NewGuid();
        var userAgentHash = HashUserAgent(userAgent);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        if (userAgentHash is not null)
        {
            await using var revokeMatchingDevice = new NpgsqlCommand(
                """
                UPDATE launcher.admin_trusted_devices
                SET revoked_at = $3
                WHERE user_id = $1
                  AND user_agent_hash = $2
                  AND revoked_at IS NULL;
                """,
                connection,
                transaction);
            revokeMatchingDevice.Parameters.AddWithValue(state.Player.UserId);
            revokeMatchingDevice.Parameters.AddWithValue(userAgentHash);
            revokeMatchingDevice.Parameters.AddWithValue(now);
            await revokeMatchingDevice.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var insert = new NpgsqlCommand(
            """
            INSERT INTO launcher.admin_trusted_devices
                (id, user_id, token_hash, expires_at, created_at, last_used_at,
                 source_ip, last_source_ip, user_agent_hash)
            SELECT $1, session.user_id, $2, $3, $4, $4, $5, $5, $6
            FROM launcher.admin_web_sessions session
            JOIN launcher.users user_account ON user_account.id = session.user_id
            WHERE session.id = $7
              AND session.user_id = $8
              AND session.revoked_at IS NULL
              AND session.expires_at > $4
              AND session.mfa_verified_at IS NOT NULL
              AND NOT user_account.is_disabled
              AND user_account.access_tier = 'Administrator';
            """,
            connection,
            transaction))
        {
            insert.Parameters.AddWithValue(deviceId);
            insert.Parameters.AddWithValue(AdminWebTokenGenerator.Hash(token));
            insert.Parameters.AddWithValue(expiresAt);
            insert.Parameters.AddWithValue(now);
            AddInetParameter(insert, sourceIp);
            AddNullableBytesParameter(insert, userAgentHash);
            insert.Parameters.AddWithValue(state.SessionId);
            insert.Parameters.AddWithValue(state.Player.UserId);
            if (await insert.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }
        }

        await RevokeExcessDevicesAsync(
            connection,
            transaction,
            state.Player.UserId,
            now,
            cancellationToken);
        await WriteAuditAsync(
            connection,
            transaction,
            state.Player.UserId,
            sourceIp,
            "admin.trusted_device.created",
            "admin_trusted_device",
            deviceId.ToString("D"),
            new { expiresAt },
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new AdminTrustedDeviceIssueResult(token, expiresAt);
    }

    public async Task<AdminTrustedDeviceVerificationResult> VerifySessionAsync(
        AdminWebAuthenticationState state,
        string token,
        IPAddress? sourceIp,
        CancellationToken cancellationToken)
    {
        if (!AdminWebTokenGenerator.IsShapeValid(token))
        {
            return AdminTrustedDeviceVerificationResult.Invalid;
        }

        var now = _timeProvider.GetUtcNow();
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        Guid deviceId;
        DateTimeOffset deviceExpiresAt;
        await using (var select = new NpgsqlCommand(
            """
            SELECT device.id, device.expires_at
            FROM launcher.admin_trusted_devices device
            JOIN launcher.users user_account ON user_account.id = device.user_id
            WHERE device.token_hash = $1
              AND device.user_id = $2
              AND device.revoked_at IS NULL
              AND device.expires_at > $3
              AND NOT user_account.is_disabled
              AND user_account.access_tier = 'Administrator'
              AND EXISTS (
                  SELECT 1
                  FROM launcher.admin_mfa_credentials credential
                  WHERE credential.user_id = device.user_id
              )
            FOR UPDATE OF device;
            """,
            connection,
            transaction))
        {
            select.Parameters.AddWithValue(AdminWebTokenGenerator.Hash(token));
            select.Parameters.AddWithValue(state.Player.UserId);
            select.Parameters.AddWithValue(now);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return AdminTrustedDeviceVerificationResult.Invalid;
            }

            deviceId = reader.GetGuid(0);
            deviceExpiresAt = ToDateTimeOffset(reader.GetDateTime(1));
        }

        await using (var verifySession = new NpgsqlCommand(
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
            transaction))
        {
            verifySession.Parameters.AddWithValue(state.SessionId);
            verifySession.Parameters.AddWithValue(state.Player.UserId);
            verifySession.Parameters.AddWithValue(now);
            if (await verifySession.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return AdminTrustedDeviceVerificationResult.InvalidSession;
            }
        }

        await using (var touchDevice = new NpgsqlCommand(
            """
            UPDATE launcher.admin_trusted_devices
            SET last_used_at = $2,
                last_source_ip = $3
            WHERE id = $1;
            """,
            connection,
            transaction))
        {
            touchDevice.Parameters.AddWithValue(deviceId);
            touchDevice.Parameters.AddWithValue(now);
            AddInetParameter(touchDevice, sourceIp);
            await touchDevice.ExecuteNonQueryAsync(cancellationToken);
        }

        await WriteAuditAsync(
            connection,
            transaction,
            state.Player.UserId,
            sourceIp,
            "admin.trusted_device.used",
            "admin_trusted_device",
            deviceId.ToString("D"),
            new { sessionId = state.SessionId, expiresAt = deviceExpiresAt },
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new AdminTrustedDeviceVerificationResult(
            AdminTrustedDeviceVerificationStatus.Success,
            state with { MfaVerifiedAt = now },
            deviceExpiresAt);
    }

    public async Task RevokeAsync(
        Guid userId,
        string token,
        IPAddress? sourceIp,
        CancellationToken cancellationToken)
    {
        if (!AdminWebTokenGenerator.IsShapeValid(token))
        {
            return;
        }

        var now = _timeProvider.GetUtcNow();
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        Guid? deviceId = null;
        await using (var revoke = new NpgsqlCommand(
            """
            UPDATE launcher.admin_trusted_devices
            SET revoked_at = $3
            WHERE user_id = $1
              AND token_hash = $2
              AND revoked_at IS NULL
            RETURNING id;
            """,
            connection,
            transaction))
        {
            revoke.Parameters.AddWithValue(userId);
            revoke.Parameters.AddWithValue(AdminWebTokenGenerator.Hash(token));
            revoke.Parameters.AddWithValue(now);
            var result = await revoke.ExecuteScalarAsync(cancellationToken);
            if (result is Guid value)
            {
                deviceId = value;
            }
        }

        if (deviceId is not null)
        {
            await WriteAuditAsync(
                connection,
                transaction,
                userId,
                sourceIp,
                "admin.trusted_device.revoked",
                "admin_trusted_device",
                deviceId.Value.ToString("D"),
                null,
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task RevokeExcessDevicesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid userId,
        DateTimeOffset revokedAt,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            UPDATE launcher.admin_trusted_devices
            SET revoked_at = $2
            WHERE id IN (
                SELECT id
                FROM launcher.admin_trusted_devices
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
        command.Parameters.AddWithValue(MaximumActiveDevicesPerUser);
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
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO launcher.audit_logs
                (actor_user_id, action, target_type, target_id, source_ip, after_data)
            VALUES ($1, $2, $3, $4, $5, $6);
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(actorUserId);
        command.Parameters.AddWithValue(action);
        command.Parameters.AddWithValue(targetType);
        command.Parameters.AddWithValue(targetId);
        AddInetParameter(command, sourceIp);
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Jsonb,
            Value = after is null ? DBNull.Value : JsonSerializer.Serialize(after)
        });
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
            NpgsqlDbType = NpgsqlDbType.Bytea,
            Value = value ?? (object)DBNull.Value
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

public sealed record AdminTrustedDeviceIssueResult(
    string Token,
    DateTimeOffset ExpiresAt);

public enum AdminTrustedDeviceVerificationStatus
{
    Success,
    Invalid,
    InvalidSession
}

public sealed record AdminTrustedDeviceVerificationResult(
    AdminTrustedDeviceVerificationStatus Status,
    AdminWebAuthenticationState? State = null,
    DateTimeOffset? ExpiresAt = null)
{
    public static AdminTrustedDeviceVerificationResult Invalid { get; } =
        new(AdminTrustedDeviceVerificationStatus.Invalid);

    public static AdminTrustedDeviceVerificationResult InvalidSession { get; } =
        new(AdminTrustedDeviceVerificationStatus.InvalidSession);
}

using System.Net;
using System.Text.Json;
using Hechao.Contracts;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Hechao.Api.Velocity;

public sealed class VelocityAuthorizationRepository(
    NpgsqlDataSource dataSource,
    IOptions<VelocityAuthorizationOptions> options)
{
    private readonly VelocityAuthorizationOptions _options = options.Value;

    public async Task<VelocityLaunchGrantCreationResult> CreateLaunchGrantAsync(
        AuthenticatedPlayer authenticatedPlayer,
        string serverId,
        IPAddress? sourceIp,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var player = await ReadPlayerAsync(
            connection,
            transaction,
            authenticatedPlayer.MinecraftUuid,
            cancellationToken);
        if (player is null || player.UserId != authenticatedPlayer.UserId)
        {
            return DeniedGrant(VelocityAuthorizationReason.PlayerNotLinked);
        }

        var server = await ReadServerByIdAsync(
            connection,
            transaction,
            player.UserId,
            serverId,
            cancellationToken);
        var reason = VelocityAuthorizationRules.Evaluate(
            player,
            server,
            now,
            TimeSpan.FromMinutes(_options.MaximumLuckPermsAgeMinutes));
        if (reason != VelocityAuthorizationReason.Allowed)
        {
            return DeniedGrant(reason);
        }

        var accessibleServer = server!;
        var grantId = Guid.NewGuid();
        var expiresAt = now.AddMinutes(_options.LaunchGrantMinutes);

        await RevokeExistingGrantsAsync(
            connection,
            transaction,
            player.UserId,
            now,
            cancellationToken);
        await InsertGrantAsync(
            connection,
            transaction,
            grantId,
            player,
            accessibleServer,
            sourceIp,
            now,
            expiresAt,
            cancellationToken);
        await DeleteOldGrantsAsync(connection, transaction, now, cancellationToken);
        await WriteAuditAsync(
            connection,
            transaction,
            "velocity.launch_grant.created",
            player.MinecraftUuid,
            accessibleServer,
            VelocityAuthorizationReason.Allowed,
            initialConnection: true,
            proxyInstance: null,
            sourceIp,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return new VelocityLaunchGrantCreationResult(
            VelocityAuthorizationReason.Allowed,
            VelocityAuthorizationRules.GetMessage(VelocityAuthorizationReason.Allowed),
            new VelocityLaunchGrantResponse(grantId, accessibleServer.ServerId, expiresAt));
    }

    public async Task<VelocityAuthorizationResponse> AuthorizeAsync(
        VelocityAuthorizationRequest request,
        IPAddress? remoteAddress,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var target = request.VelocityTarget.ToLowerInvariant();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var player = await ReadPlayerAsync(
            connection,
            transaction,
            request.MinecraftUuid,
            cancellationToken);
        var server = await ReadServerByTargetAsync(
            connection,
            transaction,
            player?.UserId,
            target,
            cancellationToken);

        var reason = VelocityAuthorizationRules.Evaluate(
            player,
            server,
            now,
            TimeSpan.FromMinutes(_options.MaximumLuckPermsAgeMinutes));

        if (reason == VelocityAuthorizationReason.Allowed && request.InitialConnection)
        {
            reason = await ConsumeLaunchGrantAsync(
                connection,
                transaction,
                player!,
                target,
                request.ProxyInstance,
                remoteAddress,
                now,
                cancellationToken);
        }

        if (request.InitialConnection || reason != VelocityAuthorizationReason.Allowed)
        {
            await WriteAuditAsync(
                connection,
                transaction,
                reason == VelocityAuthorizationReason.Allowed
                    ? "velocity.launch_grant.consumed"
                    : "velocity.authorization.denied",
                request.MinecraftUuid,
                server,
                reason,
                request.InitialConnection,
                request.ProxyInstance,
                remoteAddress,
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new VelocityAuthorizationResponse(
            reason == VelocityAuthorizationReason.Allowed,
            reason,
            VelocityAuthorizationRules.GetMessage(reason),
            server?.ServerId,
            target,
            player?.AccessTier,
            player?.LuckPermsPrimaryGroup,
            now);
    }

    private static VelocityLaunchGrantCreationResult DeniedGrant(VelocityAuthorizationReason reason)
    {
        return new VelocityLaunchGrantCreationResult(
            reason,
            VelocityAuthorizationRules.GetMessage(reason),
            Grant: null);
    }

    private static async Task<VelocityPlayerAccess?> ReadPlayerAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid minecraftUuid,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT user_account.id,
                   identity.minecraft_uuid,
                   user_account.is_disabled,
                   user_account.access_tier,
                   identity.luckperms_primary_group,
                   identity.luckperms_synced_at
            FROM launcher.minecraft_identities identity
            JOIN launcher.users user_account ON user_account.id = identity.user_id
            WHERE identity.minecraft_uuid = $1;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(minecraftUuid);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new VelocityPlayerAccess(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetBoolean(2),
            Enum.Parse<AccessTier>(reader.GetString(3), ignoreCase: true),
            reader.GetString(4),
            reader.IsDBNull(5) ? null : ToDateTimeOffset(reader.GetDateTime(5)));
    }

    private static Task<VelocityServerAccess?> ReadServerByIdAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid? userId,
        string serverId,
        CancellationToken cancellationToken)
    {
        const string predicate = "server.id = $2";
        return ReadServerAsync(
            connection,
            transaction,
            userId,
            serverId,
            predicate,
            cancellationToken);
    }

    private static Task<VelocityServerAccess?> ReadServerByTargetAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid? userId,
        string velocityTarget,
        CancellationToken cancellationToken)
    {
        const string predicate = "lower(server.velocity_target) = $2";
        return ReadServerAsync(
            connection,
            transaction,
            userId,
            velocityTarget,
            predicate,
            cancellationToken);
    }

    private static async Task<VelocityServerAccess?> ReadServerAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid? userId,
        string lookupValue,
        string predicate,
        CancellationToken cancellationToken)
    {
        var sql = $$"""
            SELECT server.id,
                   server.velocity_target,
                   server.status,
                   server.minimum_tier,
                   access_override.decision
            FROM launcher.servers server
            LEFT JOIN launcher.server_access_overrides access_override
                ON access_override.user_id = $1::uuid
               AND access_override.server_id = server.id
               AND (access_override.expires_at IS NULL OR access_override.expires_at > now())
            WHERE {{predicate}}
              AND server.is_visible
            ORDER BY CASE server.status
                         WHEN 'Online' THEN 0
                         WHEN 'Maintenance' THEN 1
                         ELSE 2
                     END,
                     server.sort_order,
                     server.id
            LIMIT 1;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Uuid,
            Value = userId ?? (object)DBNull.Value
        });
        command.Parameters.AddWithValue(lookupValue);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new VelocityServerAccess(
            reader.GetString(0),
            reader.GetString(1),
            Enum.Parse<ServerStatus>(reader.GetString(2), ignoreCase: true),
            Enum.Parse<AccessTier>(reader.GetString(3), ignoreCase: true),
            reader.IsDBNull(4)
                ? ServerAccessOverride.None
                : Enum.Parse<ServerAccessOverride>(reader.GetString(4), ignoreCase: true));
    }

    private async Task<VelocityAuthorizationReason> ConsumeLaunchGrantAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        VelocityPlayerAccess player,
        string velocityTarget,
        string proxyInstance,
        IPAddress? remoteAddress,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        const string selectSql = """
            SELECT id, source_ip
            FROM launcher.velocity_launch_grants
            WHERE user_id = $1
              AND minecraft_uuid = $2
              AND consumed_at IS NULL
              AND revoked_at IS NULL
              AND expires_at > $3
            ORDER BY created_at DESC
            LIMIT 1
            FOR UPDATE SKIP LOCKED;
            """;

        Guid grantId;
        IPAddress? grantAddress;
        await using (var select = new NpgsqlCommand(selectSql, connection, transaction))
        {
            select.Parameters.AddWithValue(player.UserId);
            select.Parameters.AddWithValue(player.MinecraftUuid);
            select.Parameters.AddWithValue(now);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return VelocityAuthorizationReason.LaunchGrantRequired;
            }

            grantId = reader.GetGuid(0);
            grantAddress = reader.IsDBNull(1) ? null : reader.GetFieldValue<IPAddress>(1);
        }

        if (_options.RequireGrantIpMatch && !AddressesEqual(grantAddress, remoteAddress))
        {
            return VelocityAuthorizationReason.LaunchGrantIpMismatch;
        }

        const string updateSql = """
            UPDATE launcher.velocity_launch_grants
            SET consumed_at = $2,
                consumed_velocity_target = $3,
                proxy_instance = $4
            WHERE id = $1;
            """;

        await using var update = new NpgsqlCommand(updateSql, connection, transaction);
        update.Parameters.AddWithValue(grantId);
        update.Parameters.AddWithValue(now);
        update.Parameters.AddWithValue(velocityTarget);
        update.Parameters.AddWithValue(proxyInstance);
        await update.ExecuteNonQueryAsync(cancellationToken);
        return VelocityAuthorizationReason.Allowed;
    }

    private static async Task RevokeExistingGrantsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE launcher.velocity_launch_grants
            SET revoked_at = $2
            WHERE user_id = $1
              AND consumed_at IS NULL
              AND revoked_at IS NULL;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertGrantAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid grantId,
        VelocityPlayerAccess player,
        VelocityServerAccess server,
        IPAddress? sourceIp,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO launcher.velocity_launch_grants
                (id, user_id, minecraft_uuid, requested_server_id,
                 source_ip, created_at, expires_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7);
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(grantId);
        command.Parameters.AddWithValue(player.UserId);
        command.Parameters.AddWithValue(player.MinecraftUuid);
        command.Parameters.AddWithValue(server.ServerId);
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Inet,
            Value = sourceIp ?? (object)DBNull.Value
        });
        command.Parameters.AddWithValue(createdAt);
        command.Parameters.AddWithValue(expiresAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task DeleteOldGrantsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        const string sql = """
            DELETE FROM launcher.velocity_launch_grants
            WHERE expires_at < $1;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(now.AddDays(-7));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task WriteAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string action,
        Guid minecraftUuid,
        VelocityServerAccess? server,
        VelocityAuthorizationReason reason,
        bool initialConnection,
        string? proxyInstance,
        IPAddress? sourceIp,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO launcher.audit_logs
                (action, target_type, target_id, source_ip, after_data)
            VALUES ($1, 'minecraft_identity', $2, $3, $4);
            """;

        var auditData = JsonSerializer.Serialize(new
        {
            ServerId = server?.ServerId,
            VelocityTarget = server?.VelocityTarget,
            Reason = reason.ToString(),
            InitialConnection = initialConnection,
            ProxyInstance = proxyInstance
        });

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(action);
        command.Parameters.AddWithValue(minecraftUuid.ToString("D"));
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Inet,
            Value = sourceIp ?? (object)DBNull.Value
        });
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Jsonb,
            Value = auditData
        });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static bool AddressesEqual(IPAddress? left, IPAddress? right)
    {
        if (left is null || right is null)
        {
            return false;
        }

        var normalizedLeft = left.IsIPv4MappedToIPv6 ? left.MapToIPv4() : left;
        var normalizedRight = right.IsIPv4MappedToIPv6 ? right.MapToIPv4() : right;
        return normalizedLeft.Equals(normalizedRight);
    }

    private static DateTimeOffset ToDateTimeOffset(DateTime value)
    {
        return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }
}

public sealed record VelocityLaunchGrantCreationResult(
    VelocityAuthorizationReason Reason,
    string Message,
    VelocityLaunchGrantResponse? Grant);

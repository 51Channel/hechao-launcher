using System.Net;
using System.Text.Json;
using Hechao.Api.Catalog;
using Hechao.Api.ServerControl;
using Hechao.Contracts;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Hechao.Api.Velocity;

public sealed class VelocityAuthorizationRepository(
    NpgsqlDataSource dataSource,
    IOptions<VelocityAuthorizationOptions> options,
    IOptions<ServerControlOptions> controlOptions)
{
    private readonly VelocityAuthorizationOptions _options = options.Value;
    private readonly TimeSpan _controlFreshness =
        TimeSpan.FromSeconds(controlOptions.Value.AgentFreshnessSeconds);

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
            sessionServerId: null,
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
        var requestedTarget = request.VelocityTarget.ToLowerInvariant();
        var effectiveTarget = requestedTarget;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var player = await ReadPlayerAsync(
            connection,
            transaction,
            request.MinecraftUuid,
            cancellationToken);
        VelocityServerAccess? server = null;
        VelocityAuthorizationReason reason;
        if (request.InitialConnection)
        {
            reason = VelocityAuthorizationRules.Evaluate(
                player,
                server,
                now,
                TimeSpan.FromMinutes(_options.MaximumLuckPermsAgeMinutes));
            if (reason == VelocityAuthorizationReason.ServerUnknown)
            {
                var grant = await ReadPendingLaunchGrantAsync(
                    connection,
                    transaction,
                    player!,
                    now,
                    cancellationToken);
                if (grant is null)
                {
                    reason = VelocityAuthorizationReason.LaunchGrantRequired;
                }
                else
                {
                    server = await ReadServerByIdAsync(
                        connection,
                        transaction,
                        player!.UserId,
                        grant.ServerId,
                        cancellationToken);
                    reason = VelocityAuthorizationRules.Evaluate(
                        player,
                        server,
                        now,
                        TimeSpan.FromMinutes(_options.MaximumLuckPermsAgeMinutes));
                    if (server is not null)
                    {
                        effectiveTarget = server.VelocityTarget.ToLowerInvariant();
                    }

                    if (reason == VelocityAuthorizationReason.Allowed &&
                        _options.RequireGrantIpMatch &&
                        !AddressesEqual(grant.SourceIp, remoteAddress))
                    {
                        reason = VelocityAuthorizationReason.LaunchGrantIpMismatch;
                    }

                    if (reason == VelocityAuthorizationReason.Allowed)
                    {
                        await ConsumeLaunchGrantAsync(
                            connection,
                            transaction,
                            grant.GrantId,
                            effectiveTarget,
                            request.ProxyInstance,
                            now,
                            cancellationToken);
                    }
                }
            }
        }
        else
        {
            server = await ReadServerByTargetAsync(
                connection,
                transaction,
                player?.UserId,
                requestedTarget,
                cancellationToken);
            reason = VelocityAuthorizationRules.Evaluate(
                player,
                server,
                now,
                TimeSpan.FromMinutes(_options.MaximumLuckPermsAgeMinutes));
            if (reason == VelocityAuthorizationReason.Allowed)
            {
                var sessionServer = string.IsNullOrWhiteSpace(request.SessionServerId)
                    ? null
                    : await ReadServerByIdAsync(
                        connection,
                        transaction,
                        userId: null,
                        request.SessionServerId,
                        cancellationToken);
                reason = VelocityAuthorizationRules.EvaluateClientCompatibility(
                    sessionServer,
                    server!);
            }
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
                request.SessionServerId,
                remoteAddress,
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new VelocityAuthorizationResponse(
            reason == VelocityAuthorizationReason.Allowed,
            reason,
            VelocityAuthorizationRules.GetMessage(reason),
            server?.ServerId,
            effectiveTarget,
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
                   EXISTS (
                       SELECT 1
                       FROM launcher.minecraft_identity_bans identity_ban
                       WHERE identity_ban.minecraft_uuid = identity.minecraft_uuid
                         AND identity_ban.revoked_at IS NULL
                         AND (identity_ban.expires_at IS NULL OR identity_ban.expires_at > now())
                   ),
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
            reader.GetBoolean(3),
            Enum.Parse<AccessTier>(reader.GetString(4), ignoreCase: true),
            reader.GetString(5),
            reader.IsDBNull(6) ? null : ToDateTimeOffset(reader.GetDateTime(6)));
    }

    private Task<VelocityServerAccess?> ReadServerByIdAsync(
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

    private Task<VelocityServerAccess?> ReadServerByTargetAsync(
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

    private async Task<VelocityServerAccess?> ReadServerAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid? userId,
        string lookupValue,
        string predicate,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var sql = $$"""
            SELECT server.id,
                   server.velocity_target,
                   server.status,
                   server.minimum_tier,
                   access_override.decision,
                   server.opens_at,
                   server.closes_at,
                   server.minecraft_version,
                   server.loader,
                   server.client_profile_id,
                   server.allow_protocol_translation,
                   control_target.reported_online,
                   control_target.last_seen_at,
                   server.activity_plan_status,
                   server.activity_package_import_id,
                   control_target.deployed_package_import_id
            FROM launcher.servers server
            LEFT JOIN launcher.server_access_overrides access_override
                ON access_override.user_id = $1::uuid
               AND access_override.server_id = server.id
               AND (access_override.expires_at IS NULL OR access_override.expires_at > now())
            LEFT JOIN launcher.server_control_targets control_target
                ON control_target.server_id = CASE
                    WHEN server.activity_plan_status IS NOT NULL THEN 'activity'
                    ELSE server.id
                END
            WHERE {{predicate}}
              AND server.is_visible
              AND server.server_role = 'Player'
            ORDER BY CASE
                         WHEN server.status = 'Online'
                              AND (server.opens_at IS NULL OR server.opens_at <= now())
                              AND (server.closes_at IS NULL OR server.closes_at > now())
                              AND (control_target.server_id IS NULL
                                   OR (control_target.reported_online
                                       AND control_target.last_seen_at >= $3))
                              AND (server.activity_plan_status IS NULL
                                   OR control_target.deployed_package_import_id =
                                      server.activity_package_import_id)
                             THEN 0
                         WHEN server.status = 'Maintenance' THEN 1
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
        command.Parameters.AddWithValue(now - _controlFreshness);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

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
        if (!reader.IsDBNull(11))
        {
            controlObservation = new ServerControlObservation(
                reader.GetBoolean(11),
                new DateTimeOffset(reader.GetDateTime(12)));
        }

        var effectiveStatus = ServerControlAvailabilityRules.Resolve(
            scheduledStatus,
            controlObservation,
            now,
            _controlFreshness).Status;
        effectiveStatus = CatalogRepository.ResolveActivityDeploymentStatus(
            effectiveStatus,
            !reader.IsDBNull(13),
            reader.IsDBNull(14) ? null : reader.GetGuid(14),
            reader.IsDBNull(15) ? null : reader.GetGuid(15));
        return new VelocityServerAccess(
            reader.GetString(0),
            reader.GetString(1),
            effectiveStatus,
            Enum.Parse<AccessTier>(reader.GetString(3), ignoreCase: true),
            reader.IsDBNull(4)
                ? ServerAccessOverride.None
                : Enum.Parse<ServerAccessOverride>(reader.GetString(4), ignoreCase: true),
            reader.GetString(7),
            reader.GetString(8),
            reader.GetString(9),
            reader.GetBoolean(10));
    }

    private static async Task<PendingLaunchGrant?> ReadPendingLaunchGrantAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        VelocityPlayerAccess player,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        const string selectSql = """
            SELECT id, requested_server_id, source_ip
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

        await using (var select = new NpgsqlCommand(selectSql, connection, transaction))
        {
            select.Parameters.AddWithValue(player.UserId);
            select.Parameters.AddWithValue(player.MinecraftUuid);
            select.Parameters.AddWithValue(now);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            return new PendingLaunchGrant(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetFieldValue<IPAddress>(2));
        }
    }

    private static async Task ConsumeLaunchGrantAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid grantId,
        string velocityTarget,
        string proxyInstance,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
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
        string? sessionServerId,
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
            ProxyInstance = proxyInstance,
            SessionServerId = sessionServerId
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

internal sealed record PendingLaunchGrant(
    Guid GrantId,
    string ServerId,
    IPAddress? SourceIp);

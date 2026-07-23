using Hechao.Api.Monitoring;
using Hechao.Contracts;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Hechao.Api.Catalog;

public sealed class CatalogRepository(
    NpgsqlDataSource dataSource,
    IOptions<ServerHeartbeatOptions> heartbeatOptions)
{
    private readonly TimeSpan _heartbeatFreshness =
        TimeSpan.FromSeconds(heartbeatOptions.Value.FreshnessSeconds);

    public async Task<LauncherCatalogSnapshot> GetSnapshotAsync(
        Guid? userId,
        AccessTier? accessTier,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var servers = await ReadServersAsync(
            connection,
            userId,
            accessTier,
            _heartbeatFreshness,
            cancellationToken);
        var profileIds = servers
            .Select(server => server.ClientProfileId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var profiles = await ReadProfilesAsync(connection, profileIds, cancellationToken);
        return new LauncherCatalogSnapshot(DateTimeOffset.UtcNow, servers, profiles);
    }

    public async Task<ClientProfileSummary?> GetAccessibleProfileAsync(
        Guid userId,
        AccessTier accessTier,
        string profileId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT profile.id, profile.display_name, profile.version,
                   profile.download_bytes, profile.sha256, profile.published_at
            FROM launcher.client_profiles profile
            WHERE profile.id = $3
              AND profile.is_active
              AND EXISTS (
                  SELECT 1
                  FROM launcher.servers server
                  LEFT JOIN launcher.server_access_overrides access_override
                      ON access_override.user_id = $1
                     AND access_override.server_id = server.id
                     AND (access_override.expires_at IS NULL OR access_override.expires_at > now())
                  WHERE server.client_profile_id = profile.id
                    AND server.is_visible
                    AND (
                        access_override.decision = 'Allow'
                        OR (
                            access_override.decision IS DISTINCT FROM 'Deny'
                            AND CASE $2
                                WHEN 'Member' THEN 0
                                WHEN 'Participant' THEN 1
                                WHEN 'Collaborator' THEN 2
                                WHEN 'Administrator' THEN 3
                                ELSE -1
                            END >= CASE server.minimum_tier
                                WHEN 'Member' THEN 0
                                WHEN 'Participant' THEN 1
                                WHEN 'Collaborator' THEN 2
                                WHEN 'Administrator' THEN 3
                                ELSE 100
                            END
                        )
                    )
              );
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(accessTier.ToString());
        command.Parameters.AddWithValue(profileId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ClientProfileSummary(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt64(3),
            reader.GetString(4),
            new DateTimeOffset(reader.GetDateTime(5)));
    }

    private static async Task<IReadOnlyList<ClientProfileSummary>> ReadProfilesAsync(
        NpgsqlConnection connection,
        string[] profileIds,
        CancellationToken cancellationToken)
    {
        if (profileIds.Length == 0)
        {
            return [];
        }

        const string sql = """
            SELECT id, display_name, version, download_bytes, sha256, published_at
            FROM launcher.client_profiles
            WHERE is_active AND id = ANY($1)
            ORDER BY id;
            """;

        var profiles = new List<ClientProfileSummary>();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(profileIds);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            profiles.Add(new ClientProfileSummary(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt64(3),
                reader.GetString(4),
                new DateTimeOffset(reader.GetDateTime(5))));
        }

        return profiles;
    }

    private static async Task<IReadOnlyList<ServerSummary>> ReadServersAsync(
        NpgsqlConnection connection,
        Guid? userId,
        AccessTier? accessTier,
        TimeSpan heartbeatFreshness,
        CancellationToken cancellationToken)
    {
        const string anonymousSql = """
            SELECT server.id, server.display_name, server.short_name, server.icon_glyph,
                   server.status, server.online_players, server.max_players,
                   server.minecraft_version, server.loader, server.minimum_tier,
                   server.client_profile_id, heartbeat.is_online, heartbeat.online_players,
                   heartbeat.max_players, heartbeat.received_at
            FROM launcher.servers server
            LEFT JOIN launcher.velocity_target_heartbeats heartbeat
                ON heartbeat.velocity_target = server.velocity_target
            WHERE server.is_visible
            ORDER BY server.sort_order, server.id;
            """;

        const string authenticatedSql = """
            SELECT server.id, server.display_name, server.short_name, server.icon_glyph,
                   server.status, server.online_players, server.max_players,
                   server.minecraft_version, server.loader, server.minimum_tier,
                   server.client_profile_id, heartbeat.is_online, heartbeat.online_players,
                   heartbeat.max_players, heartbeat.received_at
            FROM launcher.servers server
            LEFT JOIN launcher.velocity_target_heartbeats heartbeat
                ON heartbeat.velocity_target = server.velocity_target
            LEFT JOIN launcher.server_access_overrides access_override
                ON access_override.user_id = $1
               AND access_override.server_id = server.id
               AND (access_override.expires_at IS NULL OR access_override.expires_at > now())
            WHERE server.is_visible
              AND (
                  access_override.decision = 'Allow'
                  OR (
                      access_override.decision IS DISTINCT FROM 'Deny'
                      AND CASE $2
                          WHEN 'Member' THEN 0
                          WHEN 'Participant' THEN 1
                          WHEN 'Collaborator' THEN 2
                          WHEN 'Administrator' THEN 3
                          ELSE -1
                      END >= CASE server.minimum_tier
                          WHEN 'Member' THEN 0
                          WHEN 'Participant' THEN 1
                          WHEN 'Collaborator' THEN 2
                          WHEN 'Administrator' THEN 3
                          ELSE 100
                      END
                  )
              )
            ORDER BY server.sort_order, server.id;
            """;

        var servers = new List<ServerSummary>();
        await using var command = new NpgsqlCommand(
            userId is null || accessTier is null ? anonymousSql : authenticatedSql,
            connection);
        if (userId is not null && accessTier is not null)
        {
            command.Parameters.AddWithValue(userId.Value);
            command.Parameters.AddWithValue(accessTier.Value.ToString());
        }

        var now = DateTimeOffset.UtcNow;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var configuredStatus = Enum.Parse<ServerStatus>(reader.GetString(4), ignoreCase: true);
            ServerHeartbeatObservation? heartbeat = null;
            if (!reader.IsDBNull(11))
            {
                heartbeat = new ServerHeartbeatObservation(
                    reader.GetBoolean(11),
                    reader.GetInt32(12),
                    reader.GetInt32(13),
                    new DateTimeOffset(reader.GetDateTime(14)));
            }

            var runtimeStatus = ServerRuntimeStatusResolver.Resolve(
                configuredStatus,
                reader.GetInt32(5),
                reader.GetInt32(6),
                heartbeat,
                now,
                heartbeatFreshness);
            servers.Add(new ServerSummary(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                runtimeStatus.Status,
                runtimeStatus.OnlinePlayers,
                runtimeStatus.MaxPlayers,
                reader.GetString(7),
                Enum.Parse<ModLoaderKind>(reader.GetString(8), ignoreCase: true),
                Enum.Parse<AccessTier>(reader.GetString(9), ignoreCase: true),
                reader.GetString(10)));
        }

        return servers;
    }
}

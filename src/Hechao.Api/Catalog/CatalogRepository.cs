using Hechao.Api.Monitoring;
using Hechao.Api.ServerControl;
using Hechao.Contracts;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Hechao.Api.Catalog;

public sealed class CatalogRepository(
    NpgsqlDataSource dataSource,
    IOptions<ServerHeartbeatOptions> heartbeatOptions,
    IOptions<ServerControlOptions> controlOptions)
{
    private readonly TimeSpan _heartbeatFreshness =
        TimeSpan.FromSeconds(heartbeatOptions.Value.FreshnessSeconds);
    private readonly TimeSpan _controlFreshness =
        TimeSpan.FromSeconds(controlOptions.Value.AgentFreshnessSeconds);

    public async Task<LauncherCatalogSnapshot> GetSnapshotAsync(
        Guid? userId,
        AccessTier? accessTier,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        if (userId is not null &&
            await IsMinecraftIdentityBannedAsync(
                connection,
                userId.Value,
                cancellationToken))
        {
            return new LauncherCatalogSnapshot(
                DateTimeOffset.UtcNow,
                Array.Empty<ServerSummary>(),
                Array.Empty<ClientProfileSummary>());
        }

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
        var profiles = await ReadProfilesAsync(
            connection,
            profileIds,
            userId,
            accessTier,
            cancellationToken);
        return new LauncherCatalogSnapshot(DateTimeOffset.UtcNow, servers, profiles);
    }

    public async Task<ClientProfileSummary?> GetAccessibleProfileAsync(
        Guid userId,
        AccessTier accessTier,
        string profileId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT profile.id
            FROM launcher.client_profiles profile
            WHERE profile.id = $3
              AND profile.is_active
              AND NOT EXISTS (
                  SELECT 1
                  FROM launcher.minecraft_identities identity
                  JOIN launcher.minecraft_identity_bans identity_ban
                      ON identity_ban.minecraft_uuid = identity.minecraft_uuid
                  WHERE identity.user_id = $1
                    AND identity_ban.revoked_at IS NULL
                    AND (identity_ban.expires_at IS NULL OR identity_ban.expires_at > now())
              )
              AND EXISTS (
                  SELECT 1
                  FROM launcher.servers server
                  LEFT JOIN launcher.server_access_overrides access_override
                      ON access_override.user_id = $1
                     AND access_override.server_id = server.id
                     AND (access_override.expires_at IS NULL OR access_override.expires_at > now())
                  WHERE server.client_profile_id = profile.id
                    AND server.is_visible
                    AND server.server_role = 'Player'
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
        var accessible =
            await command.ExecuteScalarAsync(cancellationToken) is not null;
        if (!accessible)
        {
            return null;
        }

        return await ReadProfileAsync(
            connection,
            profileId,
            userId,
            accessTier,
            cancellationToken);
    }

    private static async Task<bool> IsMinecraftIdentityBannedAsync(
        NpgsqlConnection connection,
        Guid userId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT 1
            FROM launcher.minecraft_identities identity
            JOIN launcher.minecraft_identity_bans identity_ban
                ON identity_ban.minecraft_uuid = identity.minecraft_uuid
            WHERE identity.user_id = $1
              AND identity_ban.revoked_at IS NULL
              AND (identity_ban.expires_at IS NULL OR identity_ban.expires_at > now());
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(userId);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private static async Task<IReadOnlyList<ClientProfileSummary>> ReadProfilesAsync(
        NpgsqlConnection connection,
        string[] profileIds,
        Guid? userId,
        AccessTier? accessTier,
        CancellationToken cancellationToken)
    {
        if (profileIds.Length == 0)
        {
            return [];
        }

        const string sql = """
            SELECT profile.id, profile.display_name, channel.channel,
                   channel.rollout_percentage, release.version,
                   release.download_bytes, release.manifest_sha256,
                   release.published_at, release.is_paused
            FROM launcher.client_profiles profile
            JOIN launcher.client_profile_channels channel
                ON channel.profile_id = profile.id
            JOIN launcher.client_profile_releases release
                ON release.manifest_sha256 = channel.release_sha256
            WHERE profile.is_active AND profile.id = ANY($1)
            ORDER BY profile.id, channel.channel;
            """;

        var profiles = new Dictionary<string, ProfileCandidateBuilder>(
            StringComparer.Ordinal);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(profileIds);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var profileId = reader.GetString(0);
            if (!profiles.TryGetValue(profileId, out var builder))
            {
                builder = new ProfileCandidateBuilder(
                    profileId,
                    reader.GetString(1));
                profiles.Add(profileId, builder);
            }

            builder.Candidates.Add(ReadReleaseCandidate(reader, offset: 2));
        }

        return profiles.Values
            .Select(item => item.Resolve(userId, accessTier))
            .Where(item => item is not null)
            .Select(item => item!)
            .OrderBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private static async Task<ClientProfileSummary?> ReadProfileAsync(
        NpgsqlConnection connection,
        string profileId,
        Guid? userId,
        AccessTier? accessTier,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT profile.id, profile.display_name, channel.channel,
                   channel.rollout_percentage, release.version,
                   release.download_bytes, release.manifest_sha256,
                   release.published_at, release.is_paused
            FROM launcher.client_profiles profile
            JOIN launcher.client_profile_channels channel
                ON channel.profile_id = profile.id
            JOIN launcher.client_profile_releases release
                ON release.manifest_sha256 = channel.release_sha256
            WHERE profile.is_active AND profile.id = $1
            ORDER BY channel.channel;
            """;
        ProfileCandidateBuilder? builder = null;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(profileId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            builder ??= new ProfileCandidateBuilder(
                reader.GetString(0),
                reader.GetString(1));
            builder.Candidates.Add(ReadReleaseCandidate(reader, offset: 2));
        }

        return builder?.Resolve(userId, accessTier);
    }

    private static ClientProfileReleaseCandidate ReadReleaseCandidate(
        NpgsqlDataReader reader,
        int offset)
    {
        return new ClientProfileReleaseCandidate(
            Enum.Parse<ClientProfileReleaseChannel>(
                reader.GetString(offset),
                ignoreCase: true),
            reader.GetInt32(offset + 1),
            reader.GetString(offset + 2),
            reader.GetInt64(offset + 3),
            reader.GetString(offset + 4),
            new DateTimeOffset(reader.GetDateTime(offset + 5)),
            reader.GetBoolean(offset + 6));
    }

    private async Task<IReadOnlyList<ServerSummary>> ReadServersAsync(
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
                   heartbeat.max_players, heartbeat.received_at, server.announcement,
                   server.opens_at, server.closes_at,
                   control_target.reported_online, control_target.last_seen_at,
                   server.velocity_target
            FROM launcher.servers server
            LEFT JOIN launcher.velocity_target_heartbeats heartbeat
                ON heartbeat.velocity_target = server.velocity_target
            LEFT JOIN launcher.server_control_targets control_target
                ON control_target.server_id = server.id
            WHERE server.is_visible
              AND server.server_role = 'Player'
            ORDER BY server.sort_order, server.id;
            """;

        const string authenticatedSql = """
            SELECT server.id, server.display_name, server.short_name, server.icon_glyph,
                   server.status, server.online_players, server.max_players,
                   server.minecraft_version, server.loader, server.minimum_tier,
                   server.client_profile_id, heartbeat.is_online, heartbeat.online_players,
                   heartbeat.max_players, heartbeat.received_at, server.announcement,
                   server.opens_at, server.closes_at,
                   control_target.reported_online, control_target.last_seen_at,
                   server.velocity_target
            FROM launcher.servers server
            LEFT JOIN launcher.velocity_target_heartbeats heartbeat
                ON heartbeat.velocity_target = server.velocity_target
            LEFT JOIN launcher.server_control_targets control_target
                ON control_target.server_id = server.id
            LEFT JOIN launcher.server_access_overrides access_override
                ON access_override.user_id = $1
               AND access_override.server_id = server.id
               AND (access_override.expires_at IS NULL OR access_override.expires_at > now())
            WHERE server.is_visible
              AND server.server_role = 'Player'
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
            DateTimeOffset? opensAt = reader.IsDBNull(16)
                ? null
                : new DateTimeOffset(reader.GetDateTime(16));
            DateTimeOffset? closesAt = reader.IsDBNull(17)
                ? null
                : new DateTimeOffset(reader.GetDateTime(17));
            var scheduledStatus = ServerAvailabilityRules.ResolveStatus(
                configuredStatus,
                opensAt,
                closesAt,
                now);
            ServerControlObservation? controlObservation = null;
            if (!reader.IsDBNull(18))
            {
                controlObservation = new ServerControlObservation(
                    reader.GetBoolean(18),
                    new DateTimeOffset(reader.GetDateTime(19)));
            }

            var controlledStatus = ServerControlAvailabilityRules.Resolve(
                scheduledStatus,
                controlObservation,
                now,
                _controlFreshness);
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
                controlledStatus.Status,
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
                reader.GetString(10),
                reader.GetString(15),
                opensAt,
                closesAt,
                ResolveCatalogSection(reader.GetString(20))));
        }

        return servers;
    }

    internal static ServerCatalogSection ResolveCatalogSection(string velocityTarget) =>
        velocityTarget == "activity"
            ? ServerCatalogSection.Activity
            : ServerCatalogSection.Permanent;

    private sealed class ProfileCandidateBuilder(
        string profileId,
        string displayName)
    {
        public List<ClientProfileReleaseCandidate> Candidates { get; } = [];

        public ClientProfileSummary? Resolve(
            Guid? userId,
            AccessTier? accessTier)
        {
            var release = ClientProfileReleaseResolver.Resolve(
                profileId,
                userId,
                accessTier,
                Candidates);
            return release is null
                ? null
                : new ClientProfileSummary(
                    profileId,
                    displayName,
                    release.Version,
                    release.DownloadBytes,
                    release.ManifestSha256,
                    release.PublishedAt);
        }
    }
}

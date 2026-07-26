using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hechao.Api.Distribution;
using Hechao.Contracts;
using Npgsql;
using NpgsqlTypes;

namespace Hechao.Api.Admin;

public enum AdminProfileMutationStatus
{
    Success,
    NotFound,
    RevisionConflict,
    DuplicateId,
    DuplicateVersion,
    ReleaseNotFound,
    ReleasePaused,
    ProductionReleaseRequired,
    NoRollbackTarget
}

public sealed record AdminProfileMutationResult(
    AdminProfileMutationStatus Status,
    AdminClientProfileDetail? Detail = null);

public sealed class AdminProfileReleaseRepository(NpgsqlDataSource dataSource)
{
    private static readonly JsonSerializerOptions AuditJsonOptions =
        CreateAuditJsonOptions();

    public async Task<IReadOnlyList<AdminClientProfileRecord>> GetProfilesAsync(
        CancellationToken cancellationToken)
    {
        const string profileSql = """
            SELECT profile.id, profile.display_name, profile.version,
                   profile.download_bytes, profile.sha256, profile.published_at,
                   profile.is_active, profile.updated_at, profile.revision,
                   count(release.manifest_sha256)::integer
            FROM launcher.client_profiles profile
            LEFT JOIN launcher.client_profile_releases release
                ON release.profile_id = profile.id
            GROUP BY profile.id
            ORDER BY profile.is_active DESC, profile.display_name, profile.id;
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var builders = new List<ProfileBuilder>();
        await using (var command = new NpgsqlCommand(profileSql, connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                builders.Add(new ProfileBuilder(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetInt64(3),
                    reader.GetString(4),
                    new DateTimeOffset(reader.GetDateTime(5)),
                    reader.GetBoolean(6),
                    new DateTimeOffset(reader.GetDateTime(7)),
                    reader.GetInt64(8),
                    reader.GetInt32(9)));
            }
        }

        if (builders.Count == 0)
        {
            return [];
        }

        const string channelSql = """
            SELECT channel.profile_id, channel.channel, channel.release_sha256,
                   release.version, channel.rollout_percentage, channel.revision,
                   channel.updated_at
            FROM launcher.client_profile_channels channel
            LEFT JOIN launcher.client_profile_releases release
                ON release.manifest_sha256 = channel.release_sha256
            ORDER BY channel.profile_id,
                     CASE channel.channel
                         WHEN 'test' THEN 0
                         WHEN 'gray' THEN 1
                         ELSE 2
                     END;
            """;
        var byId = builders.ToDictionary(item => item.Id, StringComparer.Ordinal);
        await using (var command = new NpgsqlCommand(channelSql, connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                if (!byId.TryGetValue(reader.GetString(0), out var profile))
                {
                    continue;
                }

                profile.Channels.Add(ReadChannel(reader, offset: 1));
            }
        }

        return builders.Select(item => item.Build()).ToArray();
    }

    public async Task<AdminClientProfileDetail?> GetDetailAsync(
        string profileId,
        CancellationToken cancellationToken)
    {
        var profile = (await GetProfilesAsync(cancellationToken))
            .SingleOrDefault(item =>
                string.Equals(item.Id, profileId, StringComparison.Ordinal));
        if (profile is null)
        {
            return null;
        }

        var releases = await GetReleasesAsync(profileId, cancellationToken);
        return new AdminClientProfileDetail(profile, releases);
    }

    public async Task<AdminProfileMutationResult> CreateProfileAsync(
        AdminClientProfileCreateRequest request,
        Guid actorUserId,
        IPAddress? sourceIp,
        CancellationToken cancellationToken)
    {
        const string insertProfileSql = """
            INSERT INTO launcher.client_profiles
                (id, display_name, version, download_bytes, sha256, published_at,
                 is_active)
            VALUES ($1, $2, 'unpublished', 0, '', now(), false);
            """;
        const string insertChannelsSql = """
            INSERT INTO launcher.client_profile_channels
                (profile_id, channel, release_sha256, rollout_percentage)
            VALUES
                ($1, 'test', NULL, 0),
                ($1, 'gray', NULL, 0),
                ($1, 'production', NULL, 100);
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using (var command = new NpgsqlCommand(
                             insertProfileSql,
                             connection,
                             transaction))
            {
                command.Parameters.AddWithValue(request.Id);
                command.Parameters.AddWithValue(request.DisplayName.Trim());
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        catch (PostgresException exception) when (
            exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return new AdminProfileMutationResult(
                AdminProfileMutationStatus.DuplicateId);
        }

        await using (var command = new NpgsqlCommand(
                         insertChannelsSql,
                         connection,
                         transaction))
        {
            command.Parameters.AddWithValue(request.Id);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await WriteAuditAsync(
            connection,
            transaction,
            actorUserId,
            sourceIp,
            "catalog.client_profile.created",
            "client_profile",
            request.Id,
            before: null,
            after: request,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new AdminProfileMutationResult(AdminProfileMutationStatus.Success);
    }

    public async Task<AdminProfileMutationResult> UpdateProfileAsync(
        string profileId,
        AdminClientProfileUpdateRequest request,
        Guid actorUserId,
        IPAddress? sourceIp,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var before = await ReadProfileMutationStateAsync(
            connection,
            transaction,
            profileId,
            cancellationToken);
        if (before is null)
        {
            return new AdminProfileMutationResult(AdminProfileMutationStatus.NotFound);
        }

        if (before.Revision != request.ExpectedRevision)
        {
            return new AdminProfileMutationResult(
                AdminProfileMutationStatus.RevisionConflict);
        }

        if (request.IsActive &&
            !await HasActiveProductionReleaseAsync(
                connection,
                transaction,
                profileId,
                cancellationToken))
        {
            return new AdminProfileMutationResult(
                AdminProfileMutationStatus.ProductionReleaseRequired);
        }

        const string sql = """
            UPDATE launcher.client_profiles
            SET display_name = $1,
                is_active = $2,
                revision = revision + 1,
                updated_at = now()
            WHERE id = $3;
            """;
        await using (var command = new NpgsqlCommand(sql, connection, transaction))
        {
            command.Parameters.AddWithValue(request.DisplayName.Trim());
            command.Parameters.AddWithValue(request.IsActive);
            command.Parameters.AddWithValue(profileId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await WriteAuditAsync(
            connection,
            transaction,
            actorUserId,
            sourceIp,
            before.IsActive == request.IsActive
                ? "catalog.client_profile.updated"
                : request.IsActive
                    ? "catalog.client_profile.enabled"
                    : "catalog.client_profile.disabled",
            "client_profile",
            profileId,
            before,
            request,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new AdminProfileMutationResult(AdminProfileMutationStatus.Success);
    }

    public async Task<AdminProfileMutationResult> ImportReleaseAsync(
        ValidatedProfileReleaseManifest manifest,
        Guid actorUserId,
        IPAddress? sourceIp,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (!await LockProfileExistsAsync(
                connection,
                transaction,
                manifest.ProfileId,
                cancellationToken))
        {
            return new AdminProfileMutationResult(AdminProfileMutationStatus.NotFound);
        }

        const string existingSql = """
            SELECT manifest_sha256, version, minecraft_version, java_version,
                   loader, loader_version
            FROM launcher.client_profile_releases
            WHERE manifest_sha256 = $1 OR (profile_id = $2 AND version = $3)
            FOR UPDATE;
            """;
        var existingManifest = false;
        var hydrateLegacyManifest = false;
        await using (var command = new NpgsqlCommand(
                         existingSql,
                         connection,
                         transaction))
        {
            command.Parameters.AddWithValue(manifest.ManifestSha256);
            command.Parameters.AddWithValue(manifest.ProfileId);
            command.Parameters.AddWithValue(manifest.Version);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (string.Equals(
                        reader.GetString(0),
                        manifest.ManifestSha256,
                        StringComparison.Ordinal))
                {
                    existingManifest = true;
                    hydrateLegacyManifest =
                        reader.GetString(2) == "legacy" &&
                        reader.GetString(3) == "legacy" &&
                        reader.GetString(4) == "legacy" &&
                        reader.GetString(5) == "legacy";
                    break;
                }

                return new AdminProfileMutationResult(
                    AdminProfileMutationStatus.DuplicateVersion);
            }
        }

        if (existingManifest)
        {
            if (hydrateLegacyManifest)
            {
                const string hydrateSql = """
                    UPDATE launcher.client_profile_releases
                    SET download_bytes = $1,
                        file_count = $2,
                        minecraft_version = $3,
                        java_version = $4,
                        loader = $5,
                        loader_version = $6,
                        published_at = $7,
                        created_by = $8
                    WHERE manifest_sha256 = $9
                      AND profile_id = $10
                      AND version = $11
                      AND minecraft_version = 'legacy'
                      AND java_version = 'legacy'
                      AND loader = 'legacy'
                      AND loader_version = 'legacy';
                    """;
                await using var hydrateCommand = new NpgsqlCommand(
                    hydrateSql,
                    connection,
                    transaction);
                hydrateCommand.Parameters.AddWithValue(manifest.DownloadBytes);
                hydrateCommand.Parameters.AddWithValue(manifest.FileCount);
                hydrateCommand.Parameters.AddWithValue(manifest.MinecraftVersion);
                hydrateCommand.Parameters.AddWithValue(manifest.JavaVersion);
                hydrateCommand.Parameters.AddWithValue(manifest.Loader);
                hydrateCommand.Parameters.AddWithValue(manifest.LoaderVersion);
                hydrateCommand.Parameters.AddWithValue(manifest.PublishedAt);
                hydrateCommand.Parameters.AddWithValue(actorUserId);
                hydrateCommand.Parameters.AddWithValue(manifest.ManifestSha256);
                hydrateCommand.Parameters.AddWithValue(manifest.ProfileId);
                hydrateCommand.Parameters.AddWithValue(manifest.Version);
                await hydrateCommand.ExecuteNonQueryAsync(cancellationToken);

                await WriteAuditAsync(
                    connection,
                    transaction,
                    actorUserId,
                    sourceIp,
                    "catalog.client_profile_release.hydrated",
                    "client_profile_release",
                    $"{manifest.ProfileId}:{manifest.ManifestSha256}",
                    before: new { Metadata = "legacy" },
                    after: new
                    {
                        manifest.ProfileId,
                        manifest.Version,
                        manifest.ManifestSha256,
                        manifest.DownloadBytes,
                        manifest.FileCount,
                        manifest.MinecraftVersion,
                        manifest.JavaVersion,
                        manifest.Loader,
                        manifest.LoaderVersion
                    },
                    cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return new AdminProfileMutationResult(
                AdminProfileMutationStatus.Success);
        }

        const string insertSql = """
            INSERT INTO launcher.client_profile_releases
                (
                    manifest_sha256, profile_id, version, download_bytes, file_count,
                    minecraft_version, java_version, loader, loader_version,
                    published_at, created_by
                )
            VALUES
                ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11);
            """;
        await using (var command = new NpgsqlCommand(
                         insertSql,
                         connection,
                         transaction))
        {
            command.Parameters.AddWithValue(manifest.ManifestSha256);
            command.Parameters.AddWithValue(manifest.ProfileId);
            command.Parameters.AddWithValue(manifest.Version);
            command.Parameters.AddWithValue(manifest.DownloadBytes);
            command.Parameters.AddWithValue(manifest.FileCount);
            command.Parameters.AddWithValue(manifest.MinecraftVersion);
            command.Parameters.AddWithValue(manifest.JavaVersion);
            command.Parameters.AddWithValue(manifest.Loader);
            command.Parameters.AddWithValue(manifest.LoaderVersion);
            command.Parameters.AddWithValue(manifest.PublishedAt);
            command.Parameters.AddWithValue(actorUserId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await WriteAuditAsync(
            connection,
            transaction,
            actorUserId,
            sourceIp,
            "catalog.client_profile_release.imported",
            "client_profile_release",
            $"{manifest.ProfileId}:{manifest.ManifestSha256}",
            before: null,
            after: new
            {
                manifest.ProfileId,
                manifest.Version,
                manifest.ManifestSha256,
                manifest.DownloadBytes,
                manifest.FileCount,
                manifest.KeyId
            },
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new AdminProfileMutationResult(AdminProfileMutationStatus.Success);
    }

    public async Task<AdminProfileMutationResult> SetChannelAsync(
        string profileId,
        ClientProfileReleaseChannel channel,
        AdminClientProfileChannelUpdateRequest request,
        Guid actorUserId,
        IPAddress? sourceIp,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var databaseChannel = AdminProfileReleaseRules.ToDatabaseValue(channel);
        var before = await ReadChannelForUpdateAsync(
            connection,
            transaction,
            profileId,
            databaseChannel,
            cancellationToken);
        if (before is null)
        {
            return new AdminProfileMutationResult(AdminProfileMutationStatus.NotFound);
        }

        if (before.Revision != request.ExpectedRevision)
        {
            return new AdminProfileMutationResult(
                AdminProfileMutationStatus.RevisionConflict);
        }

        AdminClientProfileReleaseRecord? release = null;
        if (request.ManifestSha256 is not null)
        {
            release = await ReadReleaseForUpdateAsync(
                connection,
                transaction,
                profileId,
                request.ManifestSha256,
                cancellationToken);
            if (release is null)
            {
                return new AdminProfileMutationResult(
                    AdminProfileMutationStatus.ReleaseNotFound);
            }

            if (release.IsPaused)
            {
                return new AdminProfileMutationResult(
                    AdminProfileMutationStatus.ReleasePaused);
            }
        }

        await UpdateChannelAsync(
            connection,
            transaction,
            profileId,
            channel,
            request.ManifestSha256,
            request.RolloutPercentage,
            actorUserId,
            cancellationToken);
        if (channel == ClientProfileReleaseChannel.Production)
        {
            await SyncProductionProfileAsync(
                connection,
                transaction,
                profileId,
                release,
                cancellationToken);
        }

        var after = new
        {
            Channel = channel,
            request.ManifestSha256,
            request.RolloutPercentage
        };
        await WriteAuditAsync(
            connection,
            transaction,
            actorUserId,
            sourceIp,
            "catalog.client_profile_channel.updated",
            "client_profile_channel",
            $"{profileId}:{databaseChannel}",
            before,
            after,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new AdminProfileMutationResult(AdminProfileMutationStatus.Success);
    }

    public async Task<AdminProfileMutationResult> RollbackChannelAsync(
        string profileId,
        ClientProfileReleaseChannel channel,
        AdminClientProfileChannelRollbackRequest request,
        Guid actorUserId,
        IPAddress? sourceIp,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var databaseChannel = AdminProfileReleaseRules.ToDatabaseValue(channel);
        var before = await ReadChannelForUpdateAsync(
            connection,
            transaction,
            profileId,
            databaseChannel,
            cancellationToken);
        if (before is null)
        {
            return new AdminProfileMutationResult(AdminProfileMutationStatus.NotFound);
        }

        if (before.Revision != request.ExpectedRevision)
        {
            return new AdminProfileMutationResult(
                AdminProfileMutationStatus.RevisionConflict);
        }

        if (before.ManifestSha256 is null)
        {
            return new AdminProfileMutationResult(
                AdminProfileMutationStatus.NoRollbackTarget);
        }

        var release = await ReadPreviousReleaseAsync(
            connection,
            transaction,
            profileId,
            before.ManifestSha256,
            cancellationToken);
        if (release is null)
        {
            return new AdminProfileMutationResult(
                AdminProfileMutationStatus.NoRollbackTarget);
        }

        await UpdateChannelAsync(
            connection,
            transaction,
            profileId,
            channel,
            release.ManifestSha256,
            before.RolloutPercentage,
            actorUserId,
            cancellationToken);
        if (channel == ClientProfileReleaseChannel.Production)
        {
            await SyncProductionProfileAsync(
                connection,
                transaction,
                profileId,
                release,
                cancellationToken);
        }

        await WriteAuditAsync(
            connection,
            transaction,
            actorUserId,
            sourceIp,
            "catalog.client_profile_channel.rolled_back",
            "client_profile_channel",
            $"{profileId}:{databaseChannel}",
            before,
            new { release.ManifestSha256, release.Version },
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new AdminProfileMutationResult(AdminProfileMutationStatus.Success);
    }

    public async Task<AdminProfileMutationResult> SetReleasePauseAsync(
        string profileId,
        string manifestSha256,
        AdminClientProfileReleasePauseRequest request,
        Guid actorUserId,
        IPAddress? sourceIp,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var before = await ReadReleaseForUpdateAsync(
            connection,
            transaction,
            profileId,
            manifestSha256,
            cancellationToken);
        if (before is null)
        {
            return new AdminProfileMutationResult(AdminProfileMutationStatus.NotFound);
        }

        if (before.Revision != request.ExpectedRevision)
        {
            return new AdminProfileMutationResult(
                AdminProfileMutationStatus.RevisionConflict);
        }

        if (before.IsPaused == request.IsPaused)
        {
            await transaction.CommitAsync(cancellationToken);
            return new AdminProfileMutationResult(AdminProfileMutationStatus.Success);
        }

        const string pauseSql = """
            UPDATE launcher.client_profile_releases
            SET is_paused = $1,
                pause_reason = $2,
                paused_at = CASE WHEN $1 THEN now() ELSE NULL END,
                paused_by = CASE WHEN $1 THEN $3 ELSE NULL END,
                revision = revision + 1
            WHERE manifest_sha256 = $4;
            """;
        await using (var command = new NpgsqlCommand(pauseSql, connection, transaction))
        {
            command.Parameters.AddWithValue(request.IsPaused);
            command.Parameters.AddWithValue(
                request.IsPaused ? request.Reason.Trim() : string.Empty);
            command.Parameters.AddWithValue(actorUserId);
            command.Parameters.AddWithValue(manifestSha256);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var rolledBackChannels = new List<object>();
        if (request.IsPaused)
        {
            var channels = await ReadChannelsPointingToReleaseAsync(
                connection,
                transaction,
                profileId,
                manifestSha256,
                cancellationToken);
            foreach (var channelState in channels)
            {
                var fallback = await ReadPreviousReleaseAsync(
                    connection,
                    transaction,
                    profileId,
                    manifestSha256,
                    cancellationToken);
                await UpdateChannelAsync(
                    connection,
                    transaction,
                    profileId,
                    channelState.Channel,
                    fallback?.ManifestSha256,
                    channelState.RolloutPercentage,
                    actorUserId,
                    cancellationToken);
                if (channelState.Channel == ClientProfileReleaseChannel.Production)
                {
                    await SyncProductionProfileAsync(
                        connection,
                        transaction,
                        profileId,
                        fallback,
                        cancellationToken);
                }

                rolledBackChannels.Add(new
                {
                    channelState.Channel,
                    From = manifestSha256,
                    To = fallback?.ManifestSha256
                });
            }
        }

        await WriteAuditAsync(
            connection,
            transaction,
            actorUserId,
            sourceIp,
            request.IsPaused
                ? "catalog.client_profile_release.paused"
                : "catalog.client_profile_release.resumed",
            "client_profile_release",
            $"{profileId}:{manifestSha256}",
            before,
            new
            {
                request.IsPaused,
                Reason = request.IsPaused ? request.Reason.Trim() : string.Empty,
                RolledBackChannels = rolledBackChannels
            },
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new AdminProfileMutationResult(AdminProfileMutationStatus.Success);
    }

    private async Task<IReadOnlyList<AdminClientProfileReleaseRecord>> GetReleasesAsync(
        string profileId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT release.profile_id, release.manifest_sha256, release.version,
                   release.download_bytes, release.file_count,
                   release.minecraft_version, release.java_version, release.loader,
                   release.loader_version, release.published_at, release.is_paused,
                   release.pause_reason, release.revision, release.created_at,
                   actor.display_name
            FROM launcher.client_profile_releases release
            LEFT JOIN launcher.users actor ON actor.id = release.created_by
            WHERE release.profile_id = $1
            ORDER BY release.published_at DESC, release.created_at DESC,
                     release.manifest_sha256;
            """;
        var releases = new List<AdminClientProfileReleaseRecord>();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(profileId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            releases.Add(ReadRelease(reader));
        }

        return releases;
    }

    private static async Task<ProfileMutationState?> ReadProfileMutationStateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string profileId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT display_name, is_active, revision
            FROM launcher.client_profiles
            WHERE id = $1
            FOR UPDATE;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(profileId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new ProfileMutationState(
                reader.GetString(0),
                reader.GetBoolean(1),
                reader.GetInt64(2))
            : null;
    }

    private static async Task<bool> HasActiveProductionReleaseAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string profileId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT 1
            FROM launcher.client_profile_channels channel
            JOIN launcher.client_profile_releases release
                ON release.manifest_sha256 = channel.release_sha256
            WHERE channel.profile_id = $1
              AND channel.channel = 'production'
              AND NOT release.is_paused
            FOR SHARE OF channel, release;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(profileId);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private static async Task<bool> LockProfileExistsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string profileId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT 1 FROM launcher.client_profiles WHERE id = $1 FOR UPDATE;",
            connection,
            transaction);
        command.Parameters.AddWithValue(profileId);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private static async Task<AdminClientProfileChannelRecord?> ReadChannelForUpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string profileId,
        string channel,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT channel.channel, channel.release_sha256, release.version,
                   channel.rollout_percentage, channel.revision, channel.updated_at
            FROM launcher.client_profile_channels channel
            LEFT JOIN launcher.client_profile_releases release
                ON release.manifest_sha256 = channel.release_sha256
            WHERE channel.profile_id = $1 AND channel.channel = $2
            FOR UPDATE OF channel;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(profileId);
        command.Parameters.AddWithValue(channel);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadChannel(reader)
            : null;
    }

    private static async Task<AdminClientProfileReleaseRecord?> ReadReleaseForUpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string profileId,
        string manifestSha256,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT release.profile_id, release.manifest_sha256, release.version,
                   release.download_bytes, release.file_count,
                   release.minecraft_version, release.java_version, release.loader,
                   release.loader_version, release.published_at, release.is_paused,
                   release.pause_reason, release.revision, release.created_at,
                   actor.display_name
            FROM launcher.client_profile_releases release
            LEFT JOIN launcher.users actor ON actor.id = release.created_by
            WHERE release.profile_id = $1 AND release.manifest_sha256 = $2
            FOR UPDATE OF release;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(profileId);
        command.Parameters.AddWithValue(manifestSha256);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadRelease(reader)
            : null;
    }

    private static async Task<AdminClientProfileReleaseRecord?> ReadPreviousReleaseAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string profileId,
        string currentManifestSha256,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT release.profile_id, release.manifest_sha256, release.version,
                   release.download_bytes, release.file_count,
                   release.minecraft_version, release.java_version, release.loader,
                   release.loader_version, release.published_at, release.is_paused,
                   release.pause_reason, release.revision, release.created_at,
                   actor.display_name
            FROM launcher.client_profile_releases current_release
            JOIN launcher.client_profile_releases release
                ON release.profile_id = current_release.profile_id
               AND (
                   release.published_at < current_release.published_at
                   OR (
                       release.published_at = current_release.published_at
                       AND (
                           release.created_at < current_release.created_at
                           OR (
                               release.created_at = current_release.created_at
                               AND release.manifest_sha256 <
                                   current_release.manifest_sha256
                           )
                       )
                   )
               )
               AND NOT release.is_paused
            LEFT JOIN launcher.users actor ON actor.id = release.created_by
            WHERE current_release.profile_id = $1
              AND current_release.manifest_sha256 = $2
            ORDER BY release.published_at DESC, release.created_at DESC,
                     release.manifest_sha256 DESC
            LIMIT 1
            FOR SHARE OF release;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(profileId);
        command.Parameters.AddWithValue(currentManifestSha256);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadRelease(reader)
            : null;
    }

    private static async Task<IReadOnlyList<ChannelMutationState>>
        ReadChannelsPointingToReleaseAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            string profileId,
            string manifestSha256,
            CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT channel, rollout_percentage
            FROM launcher.client_profile_channels
            WHERE profile_id = $1 AND release_sha256 = $2
            ORDER BY channel
            FOR UPDATE;
            """;
        var result = new List<ChannelMutationState>();
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(profileId);
        command.Parameters.AddWithValue(manifestSha256);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new ChannelMutationState(
                Enum.Parse<ClientProfileReleaseChannel>(
                    reader.GetString(0),
                    ignoreCase: true),
                reader.GetInt32(1)));
        }

        return result;
    }

    private static async Task UpdateChannelAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string profileId,
        ClientProfileReleaseChannel channel,
        string? manifestSha256,
        int rolloutPercentage,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE launcher.client_profile_channels
            SET release_sha256 = $1,
                rollout_percentage = $2,
                revision = revision + 1,
                updated_by = $3,
                updated_at = now()
            WHERE profile_id = $4 AND channel = $5;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        AdminPostgresParameters.AddPositional(
            command.Parameters,
            NpgsqlDbType.Text,
            manifestSha256);
        command.Parameters.AddWithValue(rolloutPercentage);
        command.Parameters.AddWithValue(actorUserId);
        command.Parameters.AddWithValue(profileId);
        command.Parameters.AddWithValue(
            AdminProfileReleaseRules.ToDatabaseValue(channel));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task SyncProductionProfileAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string profileId,
        AdminClientProfileReleaseRecord? release,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE launcher.client_profiles
            SET version = $1,
                download_bytes = $2,
                sha256 = $3,
                published_at = $4,
                is_active = CASE WHEN $5 THEN is_active ELSE false END,
                revision = revision + 1,
                updated_at = now()
            WHERE id = $6;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(release?.Version ?? "unpublished");
        command.Parameters.AddWithValue(release?.DownloadBytes ?? 0L);
        command.Parameters.AddWithValue(release?.ManifestSha256 ?? string.Empty);
        command.Parameters.AddWithValue(release?.PublishedAt ?? DateTimeOffset.UtcNow);
        command.Parameters.AddWithValue(release is not null);
        command.Parameters.AddWithValue(profileId);
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
            before is null
                ? null
                : JsonSerializer.Serialize(before, AuditJsonOptions));
        AdminPostgresParameters.AddPositional(
            command.Parameters,
            NpgsqlDbType.Jsonb,
            after is null
                ? null
                : JsonSerializer.Serialize(after, AuditJsonOptions));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static AdminClientProfileChannelRecord ReadChannel(
        NpgsqlDataReader reader,
        int offset = 0)
    {
        return new AdminClientProfileChannelRecord(
            Enum.Parse<ClientProfileReleaseChannel>(
                reader.GetString(offset),
                ignoreCase: true),
            reader.IsDBNull(offset + 1) ? null : reader.GetString(offset + 1),
            reader.IsDBNull(offset + 2) ? null : reader.GetString(offset + 2),
            reader.GetInt32(offset + 3),
            reader.GetInt64(offset + 4),
            new DateTimeOffset(reader.GetDateTime(offset + 5)));
    }

    private static AdminClientProfileReleaseRecord ReadRelease(
        NpgsqlDataReader reader)
    {
        return new AdminClientProfileReleaseRecord(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt64(3),
            reader.GetInt32(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetString(8),
            new DateTimeOffset(reader.GetDateTime(9)),
            reader.GetBoolean(10),
            reader.GetString(11),
            reader.GetInt64(12),
            new DateTimeOffset(reader.GetDateTime(13)),
            reader.IsDBNull(14) ? null : reader.GetString(14));
    }

    private static JsonSerializerOptions CreateAuditJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private sealed class ProfileBuilder(
        string id,
        string displayName,
        string version,
        long downloadBytes,
        string sha256,
        DateTimeOffset publishedAt,
        bool isActive,
        DateTimeOffset updatedAt,
        long revision,
        int releaseCount)
    {
        public string Id { get; } = id;
        public List<AdminClientProfileChannelRecord> Channels { get; } = [];

        public AdminClientProfileRecord Build() =>
            new(
                Id,
                displayName,
                version,
                downloadBytes,
                sha256,
                publishedAt,
                isActive,
                updatedAt,
                revision,
                releaseCount,
                Channels);
    }

    private sealed record ProfileMutationState(
        string DisplayName,
        bool IsActive,
        long Revision);

    private sealed record ChannelMutationState(
        ClientProfileReleaseChannel Channel,
        int RolloutPercentage);
}

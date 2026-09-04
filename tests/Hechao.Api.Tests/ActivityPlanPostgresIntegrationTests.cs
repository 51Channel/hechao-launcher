using System.Text.Json;
using System.Text.Json.Serialization;
using Hechao.Api.ActivityPlans;
using Hechao.Api.Database;
using Hechao.Api.PackageImports;
using Hechao.Api.ServerControl;
using Hechao.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Hechao.Api.Tests;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class ActivityPlanPostgresIntegrationTests
{
    [PostgresFact]
    public async Task UnboundDraft_CanBeArchivedRestoredBoundAndPublished()
    {
        var connectionString =
            Environment.GetEnvironmentVariable(PostgresFactAttribute.ConnectionVariable)!;
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        Assert.StartsWith(
            "hechao_economy_test_",
            builder.Database,
            StringComparison.Ordinal);

        await using var dataSource = NpgsqlDataSource.Create(builder.ConnectionString);
        await using (var connection = await dataSource.OpenConnectionAsync())
        {
            await using var reset = new NpgsqlCommand(
                "DROP SCHEMA IF EXISTS launcher CASCADE;",
                connection);
            await reset.ExecuteNonQueryAsync();
        }

        var migrator = new DatabaseMigrator(
            dataSource,
            NullLogger<DatabaseMigrator>.Instance);
        await migrator.ApplyAsync();

        var actorUserId = Guid.NewGuid();
        var packageImportId = Guid.NewGuid();
        await SeedProductionPackageAsync(
            dataSource,
            actorUserId,
            packageImportId);

        var repository = new ActivityPlanRepository(
            dataSource,
            Options.Create(new ServerControlOptions()),
            new PackageImportStorage(
                Options.Create(new PackageImportOptions()),
                NullLogger<PackageImportStorage>.Instance));
        var now = DateTimeOffset.Parse("2026-09-04T04:00:00Z");
        var createRequest = new AdminActivityPlanCreateRequest(
            "集成测试活动",
            "先建立企划，稍后绑定客户端。",
            now.AddDays(7),
            now.AddDays(7).AddHours(3),
            30,
            AccessTier.Participant,
            PackageImportId: null);

        var created = await repository.CreateAsync(
            createRequest,
            actorUserId,
            sourceIp: null,
            now,
            CancellationToken.None);
        Assert.Equal(ActivityPlanMutationStatus.Success, created.Status);
        Assert.NotNull(created.Plan);
        Assert.Null(created.Plan.PackageImportId);
        Assert.Equal(ActivityPlanStatus.Draft, created.Plan.Status);
        Assert.False(created.Plan.ProductionReady);
        Assert.Equal(1, created.Plan.Revision);

        var publishWithoutPackage = await repository.PublishAsync(
            created.Plan.Id,
            created.Plan.Revision,
            actorUserId,
            sourceIp: null,
            now,
            CancellationToken.None);
        Assert.Equal(
            ActivityPlanMutationStatus.PackageBindingRequired,
            publishWithoutPackage.Status);

        var deployWithoutPackage = await repository.DeployAsync(
            created.Plan.Id,
            new AdminActivityPlanDeployRequest(
                created.Plan.Revision,
                $"DEPLOY {created.Plan.Id}",
                "integration"),
            actorUserId,
            sourceIp: null,
            now,
            CancellationToken.None);
        Assert.Equal(
            ActivityPlanMutationStatus.PackageBindingRequired,
            deployWithoutPackage.Status);

        var archived = await repository.ArchiveAsync(
            created.Plan.Id,
            created.Plan.Revision,
            "integration",
            actorUserId,
            sourceIp: null,
            now,
            CancellationToken.None);
        Assert.Equal(ActivityPlanStatus.Archived, archived.Plan!.Status);
        Assert.Equal(2, archived.Plan.Revision);

        var restored = await repository.RestoreAsync(
            created.Plan.Id,
            archived.Plan.Revision,
            actorUserId,
            sourceIp: null,
            now,
            CancellationToken.None);
        Assert.Equal(ActivityPlanStatus.Draft, restored.Plan!.Status);
        Assert.Equal(3, restored.Plan.Revision);

        var bound = await repository.UpdateAsync(
            created.Plan.Id,
            new AdminActivityPlanUpdateRequest(
                createRequest.Title,
                createRequest.Announcement,
                createRequest.OpensAt,
                createRequest.ClosesAt,
                createRequest.MaximumPlayers,
                createRequest.MinimumTier,
                packageImportId,
                restored.Plan.Revision),
            actorUserId,
            sourceIp: null,
            now,
            CancellationToken.None);
        Assert.Equal(ActivityPlanMutationStatus.Success, bound.Status);
        Assert.Equal(packageImportId, bound.Plan!.PackageImportId);
        Assert.Equal(created.Plan.Id, bound.Plan.Id);
        Assert.Equal(created.Plan.CreatedAt, bound.Plan.CreatedAt);
        Assert.Equal(4, bound.Plan.Revision);
        Assert.True(bound.Plan.ProductionReady);

        var published = await repository.PublishAsync(
            bound.Plan.Id,
            bound.Plan.Revision,
            actorUserId,
            sourceIp: null,
            now,
            CancellationToken.None);
        Assert.Equal(ActivityPlanMutationStatus.Success, published.Status);
        Assert.Equal(ActivityPlanStatus.Published, published.Plan!.Status);
        Assert.Equal(5, published.Plan.Revision);

        await using var verification = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT
                (SELECT count(*) FROM launcher.unbound_activity_plans WHERE id = $1),
                (SELECT count(*) FROM launcher.servers WHERE id = $1),
                (SELECT count(*)
                 FROM information_schema.columns
                 WHERE table_schema = 'launcher'
                   AND table_name = 'servers'
                   AND column_name IN ('client_profile_id', 'minecraft_version', 'loader')
                   AND is_nullable = 'NO');
            """,
            verification);
        command.Parameters.AddWithValue(created.Plan.Id);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(0L, reader.GetInt64(0));
        Assert.Equal(1L, reader.GetInt64(1));
        Assert.Equal(3L, reader.GetInt64(2));
    }

    private static async Task SeedProductionPackageAsync(
        NpgsqlDataSource dataSource,
        Guid actorUserId,
        Guid packageImportId)
    {
        const string profileId = "integration-activity-neoforge";
        var sourceSha256 = new string('a', 64);
        var manifestSha256 = new string('b', 64);
        var now = DateTimeOffset.Parse("2026-09-04T03:00:00Z");
        var analysis = new PackageImportAnalysisRecord(
            "standard",
            new PackageImportDetectedMetadataRecord(
                profileId,
                "集成测试客户端",
                "1.0.0",
                "1.21.11",
                21,
                "NeoForge",
                "21.11.42",
                30,
                "run.bat"),
            new PackageImportPartRecord(manifestSha256, 1024, 2048, 1),
            new PackageImportPartRecord(sourceSha256, 1024, 2048, 1),
            1,
            1,
            0,
            [],
            []);
        var plan = new PackageImportDeploymentPlanRecord(
            profileId,
            "集成测试客户端",
            "1.0.0",
            "activity",
            PreserveWorldData: true,
            SyncServerCatalog: false,
            "集成测试活动服",
            AccessTier.Participant,
            4096);
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        jsonOptions.Converters.Add(new JsonStringEnumConverter());

        await using var connection = await dataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var user = new NpgsqlCommand(
                         """
                         INSERT INTO launcher.users
                             (id, username, display_name, access_tier)
                         VALUES ($1, 'integration_admin',
                                 'Integration Administrator', 'Administrator');
                         """,
                         connection,
                         transaction))
        {
            user.Parameters.AddWithValue(actorUserId);
            await user.ExecuteNonQueryAsync();
        }

        await using (var profile = new NpgsqlCommand(
                         """
                         INSERT INTO launcher.client_profiles
                             (id, display_name, version, download_bytes, sha256,
                              published_at, is_active)
                         VALUES ($1, '集成测试客户端', '1.0.0', 1024, $2, $3, true);
                         """,
                         connection,
                         transaction))
        {
            profile.Parameters.AddWithValue(profileId);
            profile.Parameters.AddWithValue(manifestSha256);
            profile.Parameters.AddWithValue(now);
            await profile.ExecuteNonQueryAsync();
        }

        await using (var release = new NpgsqlCommand(
                         """
                         INSERT INTO launcher.client_profile_releases
                             (manifest_sha256, profile_id, version, download_bytes,
                              file_count, minecraft_version, java_version, loader,
                              loader_version, published_at)
                         VALUES ($1, $2, '1.0.0', 1024, 1, '1.21.11', '21',
                                 'NeoForge', '21.11.42', $3);
                         """,
                         connection,
                         transaction))
        {
            release.Parameters.AddWithValue(manifestSha256);
            release.Parameters.AddWithValue(profileId);
            release.Parameters.AddWithValue(now);
            await release.ExecuteNonQueryAsync();
        }

        await using (var channel = new NpgsqlCommand(
                         """
                         INSERT INTO launcher.client_profile_channels
                             (profile_id, channel, release_sha256, rollout_percentage)
                         VALUES ($1, 'production', $2, 100);
                         """,
                         connection,
                         transaction))
        {
            channel.Parameters.AddWithValue(profileId);
            channel.Parameters.AddWithValue(manifestSha256);
            await channel.ExecuteNonQueryAsync();
        }

        await using (var package = new NpgsqlCommand(
                         """
                         INSERT INTO launcher.package_imports
                             (id, file_name, expected_upload_bytes, uploaded_bytes,
                              source_sha256, status, analysis, plan, manifest_sha256,
                              created_by, created_at, updated_at, completed_at)
                         VALUES ($1, 'integration-package.zip', 1024, 1024, $2,
                                 'Completed', $3, $4, $5, $6, $7, $7, $7);
                         """,
                         connection,
                         transaction))
        {
            package.Parameters.AddWithValue(packageImportId);
            package.Parameters.AddWithValue(sourceSha256);
            package.Parameters.AddWithValue(
                NpgsqlDbType.Jsonb,
                JsonSerializer.Serialize(analysis, jsonOptions));
            package.Parameters.AddWithValue(
                NpgsqlDbType.Jsonb,
                JsonSerializer.Serialize(plan, jsonOptions));
            package.Parameters.AddWithValue(manifestSha256);
            package.Parameters.AddWithValue(actorUserId);
            package.Parameters.AddWithValue(now);
            await package.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PostgresIntegrationCollection
{
    public const string Name = "Postgres integration";
}

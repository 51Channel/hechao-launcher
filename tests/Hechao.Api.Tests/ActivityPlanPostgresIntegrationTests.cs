using System.Text.Json;
using System.Text.Json.Serialization;
using Hechao.Api.ActivityPlans;
using Hechao.Api.Admin;
using Hechao.Api.Catalog;
using Hechao.Api.Database;
using Hechao.Api.Monitoring;
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
    private const string ProfileId = "integration-activity-neoforge";
    private const string PackageTargetServerId = "minigame-package-default";
    private const string CommercialStreetServerId = "minigame-commercial-street";

    [PostgresFact]
    public async Task BoundPlanUsesPhysicalSlotWithoutDuplicatingCatalogEntry()
    {
        await using var dataSource = await CreateMigratedDataSourceAsync();
        var actorUserId = Guid.NewGuid();
        var packageImportId = Guid.NewGuid();
        await SeedProductionPackageAndTargetsAsync(
            dataSource,
            actorUserId,
            packageImportId);

        var repository = CreateRepository(dataSource);
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
        Assert.Null(created.Plan.TargetServerId);
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
        Assert.Equal(PackageTargetServerId, bound.Plan.TargetServerId);
        Assert.Equal(created.Plan.Id, bound.Plan.Id);
        Assert.Equal(created.Plan.CreatedAt, bound.Plan.CreatedAt);
        Assert.Equal(4, bound.Plan.Revision);
        Assert.True(bound.Plan.ProductionReady);

        var retargeted = await repository.UpdateAsync(
            bound.Plan.Id,
            new AdminActivityPlanUpdateRequest(
                bound.Plan.Title,
                bound.Plan.Announcement,
                bound.Plan.OpensAt,
                bound.Plan.ClosesAt,
                bound.Plan.MaximumPlayers,
                bound.Plan.MinimumTier,
                packageImportId,
                bound.Plan.Revision,
                CommercialStreetServerId),
            actorUserId,
            sourceIp: null,
            now,
            CancellationToken.None);
        Assert.Equal(ActivityPlanMutationStatus.Success, retargeted.Status);
        Assert.Equal(CommercialStreetServerId, retargeted.Plan!.TargetServerId);
        Assert.Equal(5, retargeted.Plan.Revision);

        var legacyClientUpdate = await repository.UpdateAsync(
            retargeted.Plan.Id,
            new AdminActivityPlanUpdateRequest(
                retargeted.Plan.Title,
                retargeted.Plan.Announcement,
                retargeted.Plan.OpensAt,
                retargeted.Plan.ClosesAt,
                retargeted.Plan.MaximumPlayers,
                retargeted.Plan.MinimumTier,
                packageImportId,
                retargeted.Plan.Revision),
            actorUserId,
            sourceIp: null,
            now,
            CancellationToken.None);
        Assert.Equal(ActivityPlanMutationStatus.Success, legacyClientUpdate.Status);
        Assert.Equal(CommercialStreetServerId, legacyClientUpdate.Plan!.TargetServerId);
        Assert.Equal(6, legacyClientUpdate.Plan.Revision);

        var published = await repository.PublishAsync(
            legacyClientUpdate.Plan.Id,
            legacyClientUpdate.Plan.Revision,
            actorUserId,
            sourceIp: null,
            now,
            CancellationToken.None);
        Assert.Equal(ActivityPlanMutationStatus.Success, published.Status);
        Assert.Equal(ActivityPlanStatus.Published, published.Plan!.Status);
        Assert.Equal(CommercialStreetServerId, published.Plan.TargetServerId);
        Assert.Equal(7, published.Plan.Revision);

        var adminCatalog = new AdminCatalogRepository(
            dataSource,
            Options.Create(new ServerControlOptions()));
        var adminServers = await adminCatalog.GetServersAsync(CancellationToken.None);
        Assert.Contains(adminServers, server => server.Id == CommercialStreetServerId);
        Assert.DoesNotContain(adminServers, server => server.Id == published.Plan.Id);

        var playerCatalog = new CatalogRepository(
            dataSource,
            Options.Create(new ServerHeartbeatOptions()),
            Options.Create(new ServerControlOptions()));
        var snapshot = await playerCatalog.GetSnapshotAsync(
            userId: null,
            accessTier: null,
            CancellationToken.None);
        Assert.Contains(snapshot.Servers, server => server.Id == published.Plan.Id);
        Assert.DoesNotContain(snapshot.Servers, server => server.Id == CommercialStreetServerId);

        await using var verification = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT activity_target_server_id,
                   velocity_target,
                   activity_plan_status,
                   (SELECT count(*)
                    FROM launcher.unbound_activity_plans
                    WHERE id = $1),
                   (SELECT count(*)
                    FROM launcher.servers
                    WHERE id = $1),
                   (SELECT count(*)
                    FROM information_schema.columns
                    WHERE table_schema = 'launcher'
                      AND table_name = 'servers'
                      AND column_name IN (
                          'client_profile_id',
                          'minecraft_version',
                          'loader')
                      AND is_nullable = 'NO')
            FROM launcher.servers
            WHERE id = $1;
            """,
            verification);
        command.Parameters.AddWithValue(published.Plan.Id);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(CommercialStreetServerId, reader.GetString(0));
        Assert.Equal(CommercialStreetServerId, reader.GetString(1));
        Assert.Equal("Published", reader.GetString(2));
        Assert.Equal(0L, reader.GetInt64(3));
        Assert.Equal(1L, reader.GetInt64(4));
        Assert.Equal(3L, reader.GetInt64(5));
    }

    [PostgresFact]
    public async Task PublishedSchedulesConflictPerPhysicalTargetOnly()
    {
        await using var dataSource = await CreateMigratedDataSourceAsync();
        var actorUserId = Guid.NewGuid();
        var packageImportId = Guid.NewGuid();
        await SeedProductionPackageAndTargetsAsync(
            dataSource,
            actorUserId,
            packageImportId);

        var repository = CreateRepository(dataSource);
        var now = DateTimeOffset.Parse("2026-09-04T04:00:00Z");
        var opensAt = now.AddDays(10);

        var first = await repository.CreateAsync(
            new AdminActivityPlanCreateRequest(
                "默认槽活动",
                "占用默认小游戏槽。",
                opensAt,
                opensAt.AddHours(3),
                30,
                AccessTier.Participant,
                packageImportId,
                PackageTargetServerId),
            actorUserId,
            sourceIp: null,
            now,
            CancellationToken.None);
        Assert.Equal(ActivityPlanMutationStatus.Success, first.Status);
        var firstPublished = await repository.PublishAsync(
            first.Plan!.Id,
            first.Plan.Revision,
            actorUserId,
            sourceIp: null,
            now,
            CancellationToken.None);
        Assert.Equal(ActivityPlanMutationStatus.Success, firstPublished.Status);

        var sameTarget = await repository.CreateAsync(
            new AdminActivityPlanCreateRequest(
                "同槽重叠活动",
                "应被同槽排期拒绝。",
                opensAt.AddHours(1),
                opensAt.AddHours(4),
                30,
                AccessTier.Participant,
                packageImportId,
                PackageTargetServerId),
            actorUserId,
            sourceIp: null,
            now,
            CancellationToken.None);
        Assert.Equal(ActivityPlanMutationStatus.Success, sameTarget.Status);
        var sameTargetPublish = await repository.PublishAsync(
            sameTarget.Plan!.Id,
            sameTarget.Plan.Revision,
            actorUserId,
            sourceIp: null,
            now,
            CancellationToken.None);
        Assert.Equal(ActivityPlanMutationStatus.ScheduleConflict, sameTargetPublish.Status);
        Assert.Equal(first.Plan.Id, sameTargetPublish.Conflict!.Id);

        var otherTarget = await repository.CreateAsync(
            new AdminActivityPlanCreateRequest(
                "商业街并行活动",
                "不同承载槽允许同一时段发布。",
                opensAt.AddHours(1),
                opensAt.AddHours(4),
                30,
                AccessTier.Participant,
                packageImportId,
                CommercialStreetServerId),
            actorUserId,
            sourceIp: null,
            now,
            CancellationToken.None);
        Assert.Equal(ActivityPlanMutationStatus.Success, otherTarget.Status);
        var otherTargetPublish = await repository.PublishAsync(
            otherTarget.Plan!.Id,
            otherTarget.Plan.Revision,
            actorUserId,
            sourceIp: null,
            now,
            CancellationToken.None);
        Assert.Equal(ActivityPlanMutationStatus.Success, otherTargetPublish.Status);

        await using var verification = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT activity_target_server_id, count(*)
            FROM launcher.servers
            WHERE activity_plan_status = 'Published'
            GROUP BY activity_target_server_id
            ORDER BY activity_target_server_id;
            """,
            verification);
        await using var reader = await command.ExecuteReaderAsync();
        var publishedByTarget = new Dictionary<string, long>(StringComparer.Ordinal);
        while (await reader.ReadAsync())
        {
            publishedByTarget.Add(reader.GetString(0), reader.GetInt64(1));
        }

        Assert.Equal(2, publishedByTarget.Count);
        Assert.Equal(1, publishedByTarget[PackageTargetServerId]);
        Assert.Equal(1, publishedByTarget[CommercialStreetServerId]);
    }

    private static ActivityPlanRepository CreateRepository(NpgsqlDataSource dataSource) =>
        new(
            dataSource,
            Options.Create(new ServerControlOptions()),
            new PackageImportStorage(
                Options.Create(new PackageImportOptions()),
                NullLogger<PackageImportStorage>.Instance));

    private static async Task<NpgsqlDataSource> CreateMigratedDataSourceAsync()
    {
        var connectionString =
            Environment.GetEnvironmentVariable(PostgresFactAttribute.ConnectionVariable)!;
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        Assert.StartsWith(
            "hechao_economy_test_",
            builder.Database,
            StringComparison.Ordinal);

        var dataSource = NpgsqlDataSource.Create(builder.ConnectionString);
        try
        {
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
            return dataSource;
        }
        catch
        {
            await dataSource.DisposeAsync();
            throw;
        }
    }

    private static async Task SeedProductionPackageAndTargetsAsync(
        NpgsqlDataSource dataSource,
        Guid actorUserId,
        Guid packageImportId)
    {
        var sourceSha256 = new string('a', 64);
        var manifestSha256 = new string('b', 64);
        var now = DateTimeOffset.Parse("2026-09-04T03:00:00Z");
        var analysis = new PackageImportAnalysisRecord(
            "standard",
            new PackageImportDetectedMetadataRecord(
                ProfileId,
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
            ProfileId,
            "集成测试客户端",
            "1.0.0",
            PackageTargetServerId,
            PreserveWorldData: true,
            SyncServerCatalog: false,
            "集成测试活动服",
            AccessTier.Participant,
            4096);
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        jsonOptions.Converters.Add(new JsonStringEnumConverter());
        var firstSlotOperationId = Guid.NewGuid();
        var commercialStreetOperationId = Guid.NewGuid();

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
            profile.Parameters.AddWithValue(ProfileId);
            profile.Parameters.AddWithValue(manifestSha256);
            profile.Parameters.AddWithValue(now);
            await profile.ExecuteNonQueryAsync();
        }

        await using (var servers = new NpgsqlCommand(
                         """
                         INSERT INTO launcher.servers
                             (id, display_name, short_name, icon_glyph, status,
                              online_players, max_players, minecraft_version, loader,
                              minimum_tier, client_profile_id, velocity_target,
                              sort_order, is_visible, announcement)
                         VALUES
                             ($2, '整合包默认小游戏槽', '默认槽', '游', 'Online',
                              0, 30, '1.21.11', 'NeoForge', 'Participant', $1,
                              $2, 10, false, ''),
                             ($3, '商业街建筑对决', '商业街', '街', 'Online',
                              0, 30, '1.21.11', 'NeoForge', 'Participant', $1,
                              $3, 20, true, '真实独立小游戏槽。');
                         """,
                         connection,
                         transaction))
        {
            servers.Parameters.AddWithValue(ProfileId);
            servers.Parameters.AddWithValue(PackageTargetServerId);
            servers.Parameters.AddWithValue(CommercialStreetServerId);
            await servers.ExecuteNonQueryAsync();
        }

        await using (var targets = new NpgsqlCommand(
                         """
                         INSERT INTO launcher.server_control_targets
                             (server_id, agent_id, agent_version, conflict_group, port,
                              reported_online, process_id, settings,
                              allowed_command_prefixes, last_seen_at, updated_at,
                              package_deployment_enabled, server_files_present,
                              host_total_memory_mib)
                         VALUES
                             ('activity', 'owl5', 'integration', 'owl5-activity-slot',
                              25568, false, NULL, NULL, ARRAY['*'], $1, $1,
                              true, true, 32768),
                             ($2, 'owl5', 'integration', NULL, 25601,
                              false, NULL, NULL, ARRAY['*'], $1, $1,
                              true, true, 32768),
                             ($3, 'owl5', 'integration', NULL, 25602,
                              false, NULL, NULL, ARRAY['*'], $1, $1,
                              true, true, 32768);
                         """,
                         connection,
                         transaction))
        {
            targets.Parameters.AddWithValue(now);
            targets.Parameters.AddWithValue(PackageTargetServerId);
            targets.Parameters.AddWithValue(CommercialStreetServerId);
            await targets.ExecuteNonQueryAsync();
        }

        await using (var operations = new NpgsqlCommand(
                         """
                         INSERT INTO launcher.server_control_operations
                             (id, target_server_id, action, status, reason,
                              requested_by, requested_at, completed_at)
                         VALUES
                             ($1, $2, 'CreateDeploymentSlot', 'Succeeded',
                              'integration slot seed', $3, $4, $4),
                             ($5, $6, 'CreateDeploymentSlot', 'Succeeded',
                              'integration slot seed', $3, $4, $4);
                         """,
                         connection,
                         transaction))
        {
            operations.Parameters.AddWithValue(firstSlotOperationId);
            operations.Parameters.AddWithValue(PackageTargetServerId);
            operations.Parameters.AddWithValue(actorUserId);
            operations.Parameters.AddWithValue(now);
            operations.Parameters.AddWithValue(commercialStreetOperationId);
            operations.Parameters.AddWithValue(CommercialStreetServerId);
            await operations.ExecuteNonQueryAsync();
        }

        await using (var slots = new NpgsqlCommand(
                         """
                         INSERT INTO launcher.deployment_slots
                             (server_id, display_name, template_server_id, status,
                              operation_id, created_by, created_at, provisioned_at,
                              updated_at, slot_kind, backend_port, velocity_target)
                         VALUES
                             ($1, '整合包默认小游戏槽', 'activity', 'Ready', $2, $3,
                              $4, $4, $4, 'Minigame', 25601, $1),
                             ($5, '商业街建筑对决', 'activity', 'Ready', $6, $3,
                              $4, $4, $4, 'Minigame', 25602, $5);
                         """,
                         connection,
                         transaction))
        {
            slots.Parameters.AddWithValue(PackageTargetServerId);
            slots.Parameters.AddWithValue(firstSlotOperationId);
            slots.Parameters.AddWithValue(actorUserId);
            slots.Parameters.AddWithValue(now);
            slots.Parameters.AddWithValue(CommercialStreetServerId);
            slots.Parameters.AddWithValue(commercialStreetOperationId);
            await slots.ExecuteNonQueryAsync();
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
            release.Parameters.AddWithValue(ProfileId);
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
            channel.Parameters.AddWithValue(ProfileId);
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

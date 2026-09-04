using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hechao.Api.Admin;
using Hechao.Api.Catalog;
using Hechao.Api.PackageImports;
using Hechao.Api.ServerControl;
using Hechao.Contracts;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Hechao.Api.ActivityPlans;

public enum ActivityPlanMutationStatus
{
    Success,
    NotFound,
    RevisionConflict,
    InvalidState,
    PackageNotFound,
    PackageBindingRequired,
    TargetBindingRequired,
    TargetNotFound,
    PackageProfileArchived,
    PackageNotProductionReady,
    ScheduleConflict,
    DeploymentArtifactMissing,
    DeploymentTargetUnavailable,
    DeploymentTargetOnline,
    DeploymentOperationInProgress
}

public sealed record ActivityPlanScheduleConflict(
    string Id,
    string Title,
    DateTimeOffset OpensAt,
    DateTimeOffset ClosesAt);

public sealed record ActivityPlanMutationResult(
    ActivityPlanMutationStatus Status,
    AdminActivityPlanRecord? Plan = null,
    ActivityPlanScheduleConflict? Conflict = null);

public sealed record ActivityPlanDeploymentResult(
    ActivityPlanMutationStatus Status,
    AdminServerControlQueueResult? Queue = null);

public sealed class ActivityPlanRepository(
    NpgsqlDataSource dataSource,
    IOptions<ServerControlOptions> serverControlOptions,
    PackageImportStorage storage)
{
    private const long ScheduleAdvisoryLock = 721220028;
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly TimeSpan controlFreshness = TimeSpan.FromSeconds(
        serverControlOptions.Value.AgentFreshnessSeconds);

    public async Task<AdminActivityPlanListResponse> GetOverviewAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var plans = (await ReadPlansAsync(connection, now, cancellationToken))
            .Concat(await ReadUnboundPlansAsync(connection, cancellationToken))
            .OrderBy(plan => plan.OpensAt)
            .ThenBy(plan => plan.ClosesAt)
            .ThenBy(plan => plan.Id, StringComparer.Ordinal)
            .ToArray();
        var packages = await ReadPackagesAsync(connection, cancellationToken);
        var targets = await ReadTargetsAsync(connection, now, cancellationToken);
        var slot = ToLegacySlot(targets.FirstOrDefault(target =>
            string.Equals(
                target.ServerId,
                PackageImportRules.ActivityServerId,
                StringComparison.Ordinal)));
        var unmanagedSchedules = await ReadUnmanagedSchedulesAsync(
            connection,
            cancellationToken);
        return new AdminActivityPlanListResponse(
            now,
            plans,
            packages,
            slot,
            targets,
            unmanagedSchedules);
    }

    public async Task<ActivityPlanMutationResult> CreateAsync(
        AdminActivityPlanCreateRequest request,
        Guid actorUserId,
        IPAddress? sourceIp,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        PackageSnapshot? package = null;
        if (request.PackageImportId is Guid packageImportId)
        {
            package = await ReadPackageAsync(
                connection,
                transaction,
                packageImportId,
                lockRows: true,
                cancellationToken);
            if (package is null)
            {
                return new ActivityPlanMutationResult(
                    ActivityPlanMutationStatus.PackageNotFound);
            }

            if (package.Record.ProfileArchived)
            {
                return new ActivityPlanMutationResult(
                    ActivityPlanMutationStatus.PackageProfileArchived);
            }
        }

        var target = await ResolveTargetAsync(
            connection,
            transaction,
            request.TargetServerId,
            existingTargetServerId: null,
            package,
            cancellationToken);
        if (ResolveTargetServerId(
                request.TargetServerId,
                existingTargetServerId: null,
                package) is not null && target is null)
        {
            return new ActivityPlanMutationResult(
                ActivityPlanMutationStatus.TargetNotFound);
        }

        if (package is not null && target is null)
        {
            return new ActivityPlanMutationResult(
                ActivityPlanMutationStatus.TargetBindingRequired);
        }

        var planId = CreatePlanId(request.OpensAt);
        var shortName = TakeTextElements(request.Title, 12);
        if (package is null)
        {
            const string insertUnbound = """
                INSERT INTO launcher.unbound_activity_plans
                    (id, display_name, short_name, announcement, opens_at,
                     closes_at, max_players, minimum_tier, target_server_id,
                     activity_plan_status, created_at, updated_at)
                VALUES
                    ($1, $2, $3, $4, $5, $6, $7, $8, $9, 'Draft', $10, $10);
                """;
            await using var command = new NpgsqlCommand(
                insertUnbound,
                connection,
                transaction);
            command.Parameters.AddWithValue(planId);
            command.Parameters.AddWithValue(request.Title.Trim());
            command.Parameters.AddWithValue(shortName);
            command.Parameters.AddWithValue(request.Announcement.Trim());
            command.Parameters.AddWithValue(request.OpensAt.ToUniversalTime());
            command.Parameters.AddWithValue(request.ClosesAt.ToUniversalTime());
            command.Parameters.AddWithValue(request.MaximumPlayers);
            command.Parameters.AddWithValue(request.MinimumTier.ToString());
            AdminPostgresParameters.AddPositional(
                command.Parameters,
                NpgsqlDbType.Text,
                target?.ServerId);
            command.Parameters.AddWithValue(now);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        else
        {
            const string insertBound = """
                INSERT INTO launcher.servers
                    (id, display_name, short_name, icon_glyph, status,
                     online_players, max_players, minecraft_version, loader,
                      minimum_tier, client_profile_id, velocity_target,
                      allow_protocol_translation, server_role, monitoring_enabled,
                      sort_order, is_visible, announcement, opens_at, closes_at,
                      activity_package_import_id, activity_target_server_id,
                      activity_plan_status, created_at, updated_at)
                VALUES
                    ($1, $2, $3, 'activity', 'Online', 0, $4, $5, $6, $7,
                     $8, $9, false, 'Player', false, 30000, false, $10,
                     $11, $12, $13, $14, 'Draft', $15, $15);
                """;
            await using var command = new NpgsqlCommand(
                insertBound,
                connection,
                transaction);
            command.Parameters.AddWithValue(planId);
            command.Parameters.AddWithValue(request.Title.Trim());
            command.Parameters.AddWithValue(shortName);
            command.Parameters.AddWithValue(request.MaximumPlayers);
            command.Parameters.AddWithValue(package.Record.MinecraftVersion);
            command.Parameters.AddWithValue(package.Record.Loader.ToString());
            command.Parameters.AddWithValue(request.MinimumTier.ToString());
            command.Parameters.AddWithValue(package.Record.ProfileId);
            command.Parameters.AddWithValue(target!.VelocityTarget);
            command.Parameters.AddWithValue(request.Announcement.Trim());
            command.Parameters.AddWithValue(request.OpensAt.ToUniversalTime());
            command.Parameters.AddWithValue(request.ClosesAt.ToUniversalTime());
            command.Parameters.AddWithValue(package.Record.ImportId);
            command.Parameters.AddWithValue(target.ServerId);
            command.Parameters.AddWithValue(now);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await WriteAuditAsync(
            connection,
            transaction,
            actorUserId,
            sourceIp,
            "activity_plan.created",
            planId,
            before: null,
            after: new
            {
                request.Title,
                request.OpensAt,
                request.ClosesAt,
                request.PackageImportId,
                TargetServerId = target?.ServerId,
                Status = ActivityPlanStatus.Draft
            },
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ActivityPlanMutationResult(
            ActivityPlanMutationStatus.Success,
            await GetPlanAsync(planId, now, cancellationToken));
    }

    public async Task<ActivityPlanMutationResult> UpdateAsync(
        string planId,
        AdminActivityPlanUpdateRequest request,
        Guid actorUserId,
        IPAddress? sourceIp,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable,
            cancellationToken);
        var state = await ReadPlanStateAsync(
            connection,
            transaction,
            planId,
            cancellationToken);
        if (state is null)
        {
            return new ActivityPlanMutationResult(ActivityPlanMutationStatus.NotFound);
        }

        if (state.Revision != request.ExpectedRevision)
        {
            return new ActivityPlanMutationResult(
                ActivityPlanMutationStatus.RevisionConflict);
        }

        if (state.Status == ActivityPlanStatus.Archived)
        {
            return new ActivityPlanMutationResult(
                ActivityPlanMutationStatus.InvalidState);
        }

        PackageSnapshot? package = null;
        if (request.PackageImportId is Guid packageImportId)
        {
            package = await ReadPackageAsync(
                connection,
                transaction,
                packageImportId,
                lockRows: true,
                cancellationToken);
            if (package is null)
            {
                return new ActivityPlanMutationResult(
                    ActivityPlanMutationStatus.PackageNotFound);
            }

            if (package.Record.ProfileArchived)
            {
                return new ActivityPlanMutationResult(
                    ActivityPlanMutationStatus.PackageProfileArchived);
            }
        }

        if (!state.IsUnbound && package is null)
        {
            return new ActivityPlanMutationResult(
                ActivityPlanMutationStatus.PackageBindingRequired);
        }

        var target = await ResolveTargetAsync(
            connection,
            transaction,
            request.TargetServerId,
            state.TargetServerId,
            package,
            cancellationToken);
        if (ResolveTargetServerId(
                request.TargetServerId,
                state.TargetServerId,
                package) is not null && target is null)
        {
            return new ActivityPlanMutationResult(
                ActivityPlanMutationStatus.TargetNotFound);
        }

        if (package is not null && target is null)
        {
            return new ActivityPlanMutationResult(
                ActivityPlanMutationStatus.TargetBindingRequired);
        }

        if (state.Status == ActivityPlanStatus.Published)
        {
            if (package is null)
            {
                return new ActivityPlanMutationResult(
                    ActivityPlanMutationStatus.PackageBindingRequired);
            }

            if (!package.Record.ProductionReady)
            {
                return new ActivityPlanMutationResult(
                    ActivityPlanMutationStatus.PackageNotProductionReady);
            }

            if (target is null)
            {
                return new ActivityPlanMutationResult(
                    ActivityPlanMutationStatus.TargetBindingRequired);
            }

            await AcquireScheduleLockAsync(connection, transaction, cancellationToken);
            var conflict = await FindScheduleConflictAsync(
                connection,
                transaction,
                planId,
                target.ServerId,
                request.OpensAt,
                request.ClosesAt,
                cancellationToken);
            if (conflict is not null)
            {
                return new ActivityPlanMutationResult(
                    ActivityPlanMutationStatus.ScheduleConflict,
                    Conflict: conflict);
            }
        }

        var shortName = TakeTextElements(request.Title, 12);
        if (state.IsUnbound)
        {
            if (package is null)
            {
                const string updateUnbound = """
                    UPDATE launcher.unbound_activity_plans
                    SET display_name = $1,
                        short_name = $2,
                        max_players = $3,
                        minimum_tier = $4,
                        announcement = $5,
                        opens_at = $6,
                        closes_at = $7,
                        target_server_id = $8,
                        revision = revision + 1,
                        updated_at = $9
                    WHERE id = $10 AND revision = $11;
                    """;
                await using var command = new NpgsqlCommand(
                    updateUnbound,
                    connection,
                    transaction);
                command.Parameters.AddWithValue(request.Title.Trim());
                command.Parameters.AddWithValue(shortName);
                command.Parameters.AddWithValue(request.MaximumPlayers);
                command.Parameters.AddWithValue(request.MinimumTier.ToString());
                command.Parameters.AddWithValue(request.Announcement.Trim());
                command.Parameters.AddWithValue(request.OpensAt.ToUniversalTime());
                command.Parameters.AddWithValue(request.ClosesAt.ToUniversalTime());
                AdminPostgresParameters.AddPositional(
                    command.Parameters,
                    NpgsqlDbType.Text,
                    target?.ServerId);
                command.Parameters.AddWithValue(now);
                command.Parameters.AddWithValue(planId);
                command.Parameters.AddWithValue(request.ExpectedRevision);
                if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
                {
                    return new ActivityPlanMutationResult(
                        ActivityPlanMutationStatus.RevisionConflict);
                }
            }
            else
            {
                const string deleteUnbound = """
                    DELETE FROM launcher.unbound_activity_plans
                    WHERE id = $1 AND revision = $2;
                    """;
                await using (var delete = new NpgsqlCommand(
                                 deleteUnbound,
                                 connection,
                                 transaction))
                {
                    delete.Parameters.AddWithValue(planId);
                    delete.Parameters.AddWithValue(request.ExpectedRevision);
                    if (await delete.ExecuteNonQueryAsync(cancellationToken) != 1)
                    {
                        return new ActivityPlanMutationResult(
                            ActivityPlanMutationStatus.RevisionConflict);
                    }
                }

                const string bindPlan = """
                    INSERT INTO launcher.servers
                        (id, display_name, short_name, icon_glyph, status,
                         online_players, max_players, minecraft_version, loader,
                         minimum_tier, client_profile_id, velocity_target,
                         allow_protocol_translation, server_role, monitoring_enabled,
                         sort_order, is_visible, announcement, opens_at, closes_at,
                         activity_package_import_id, activity_target_server_id,
                         activity_plan_status, revision, created_at, updated_at)
                    VALUES
                        ($1, $2, $3, 'activity', 'Online', 0, $4, $5, $6, $7,
                         $8, $9, false, 'Player', false, 30000, false, $10,
                         $11, $12, $13, $14, 'Draft', $15, $16, $17);
                    """;
                await using var insert = new NpgsqlCommand(
                    bindPlan,
                    connection,
                    transaction);
                insert.Parameters.AddWithValue(planId);
                insert.Parameters.AddWithValue(request.Title.Trim());
                insert.Parameters.AddWithValue(shortName);
                insert.Parameters.AddWithValue(request.MaximumPlayers);
                insert.Parameters.AddWithValue(package.Record.MinecraftVersion);
                insert.Parameters.AddWithValue(package.Record.Loader.ToString());
                insert.Parameters.AddWithValue(request.MinimumTier.ToString());
                insert.Parameters.AddWithValue(package.Record.ProfileId);
                insert.Parameters.AddWithValue(target!.VelocityTarget);
                insert.Parameters.AddWithValue(request.Announcement.Trim());
                insert.Parameters.AddWithValue(request.OpensAt.ToUniversalTime());
                insert.Parameters.AddWithValue(request.ClosesAt.ToUniversalTime());
                insert.Parameters.AddWithValue(package.Record.ImportId);
                insert.Parameters.AddWithValue(target.ServerId);
                insert.Parameters.AddWithValue(request.ExpectedRevision + 1);
                insert.Parameters.AddWithValue(state.CreatedAt);
                insert.Parameters.AddWithValue(now);
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }

            await WriteAuditAsync(
                connection,
                transaction,
                actorUserId,
                sourceIp,
                "activity_plan.updated",
                planId,
                state,
                new
                {
                    request.Title,
                    request.OpensAt,
                    request.ClosesAt,
                    request.PackageImportId,
                    TargetServerId = target?.ServerId,
                    request.MaximumPlayers,
                    request.MinimumTier
                },
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new ActivityPlanMutationResult(
                ActivityPlanMutationStatus.Success,
                await GetPlanAsync(planId, now, cancellationToken));
        }

        const string sql = """
            UPDATE launcher.servers
            SET display_name = $1,
                short_name = $2,
                max_players = $3,
                minecraft_version = $4,
                loader = $5,
                minimum_tier = $6,
                client_profile_id = $7,
                announcement = $8,
                opens_at = $9,
                closes_at = $10,
                activity_package_import_id = $11,
                activity_target_server_id = $12,
                velocity_target = $13,
                revision = revision + 1,
                updated_at = $14
            WHERE id = $15
              AND activity_plan_status IS NOT NULL
              AND revision = $16;
            """;
        try
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue(request.Title.Trim());
            command.Parameters.AddWithValue(shortName);
            command.Parameters.AddWithValue(request.MaximumPlayers);
            command.Parameters.AddWithValue(package!.Record.MinecraftVersion);
            command.Parameters.AddWithValue(package.Record.Loader.ToString());
            command.Parameters.AddWithValue(request.MinimumTier.ToString());
            command.Parameters.AddWithValue(package.Record.ProfileId);
            command.Parameters.AddWithValue(request.Announcement.Trim());
            command.Parameters.AddWithValue(request.OpensAt.ToUniversalTime());
            command.Parameters.AddWithValue(request.ClosesAt.ToUniversalTime());
            command.Parameters.AddWithValue(package.Record.ImportId);
            command.Parameters.AddWithValue(target!.ServerId);
            command.Parameters.AddWithValue(target.VelocityTarget);
            command.Parameters.AddWithValue(now);
            command.Parameters.AddWithValue(planId);
            command.Parameters.AddWithValue(request.ExpectedRevision);
            if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                return new ActivityPlanMutationResult(
                    ActivityPlanMutationStatus.RevisionConflict);
            }
        }
        catch (PostgresException exception) when (
            exception.SqlState == PostgresErrorCodes.ExclusionViolation)
        {
            return new ActivityPlanMutationResult(
                ActivityPlanMutationStatus.ScheduleConflict);
        }

        await WriteAuditAsync(
            connection,
            transaction,
            actorUserId,
            sourceIp,
            "activity_plan.updated",
            planId,
            state,
            new
            {
                request.Title,
                request.OpensAt,
                request.ClosesAt,
                request.PackageImportId,
                TargetServerId = target?.ServerId,
                request.MaximumPlayers,
                request.MinimumTier
            },
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ActivityPlanMutationResult(
            ActivityPlanMutationStatus.Success,
            await GetPlanAsync(planId, now, cancellationToken));
    }

    public Task<ActivityPlanMutationResult> PublishAsync(
        string planId,
        long expectedRevision,
        Guid actorUserId,
        IPAddress? sourceIp,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        ChangeStatusAsync(
            planId,
            expectedRevision,
            ActivityPlanStatus.Draft,
            ActivityPlanStatus.Published,
            actorUserId,
            sourceIp,
            now,
            cancellationToken);

    public Task<ActivityPlanMutationResult> WithdrawAsync(
        string planId,
        long expectedRevision,
        Guid actorUserId,
        IPAddress? sourceIp,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        ChangeStatusAsync(
            planId,
            expectedRevision,
            ActivityPlanStatus.Published,
            ActivityPlanStatus.Draft,
            actorUserId,
            sourceIp,
            now,
            cancellationToken);

    public Task<ActivityPlanMutationResult> ArchiveAsync(
        string planId,
        long expectedRevision,
        string reason,
        Guid actorUserId,
        IPAddress? sourceIp,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        ChangeStatusAsync(
            planId,
            expectedRevision,
            expectedStatus: null,
            ActivityPlanStatus.Archived,
            actorUserId,
            sourceIp,
            now,
            cancellationToken,
            reason.Trim());

    public Task<ActivityPlanMutationResult> RestoreAsync(
        string planId,
        long expectedRevision,
        Guid actorUserId,
        IPAddress? sourceIp,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        ChangeStatusAsync(
            planId,
            expectedRevision,
            ActivityPlanStatus.Archived,
            ActivityPlanStatus.Draft,
            actorUserId,
            sourceIp,
            now,
            cancellationToken);

    public async Task<ActivityPlanDeploymentResult> DeployAsync(
        string planId,
        AdminActivityPlanDeployRequest request,
        Guid actorUserId,
        IPAddress? sourceIp,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable,
            cancellationToken);
        var state = await ReadPlanStateAsync(
            connection,
            transaction,
            planId,
            cancellationToken);
        if (state is null)
        {
            return new ActivityPlanDeploymentResult(
                ActivityPlanMutationStatus.NotFound);
        }

        if (state.Revision != request.ExpectedRevision)
        {
            return new ActivityPlanDeploymentResult(
                ActivityPlanMutationStatus.RevisionConflict);
        }

        if (state.Status == ActivityPlanStatus.Archived)
        {
            return new ActivityPlanDeploymentResult(
                ActivityPlanMutationStatus.InvalidState);
        }

        if (state.PackageImportId is not Guid packageImportId)
        {
            return new ActivityPlanDeploymentResult(
                ActivityPlanMutationStatus.PackageBindingRequired);
        }

        if (state.TargetServerId is null)
        {
            return new ActivityPlanDeploymentResult(
                ActivityPlanMutationStatus.TargetBindingRequired);
        }

        var package = await ReadPackageAsync(
            connection,
            transaction,
            packageImportId,
            lockRows: true,
            cancellationToken);
        if (package is null || package.Analysis.Server is null)
        {
            return new ActivityPlanDeploymentResult(
                ActivityPlanMutationStatus.PackageNotFound);
        }

        if (package.Record.ProfileArchived)
        {
            return new ActivityPlanDeploymentResult(
                ActivityPlanMutationStatus.PackageProfileArchived);
        }

        if (!storage.ServerArchiveExists(package.Record.ImportId))
        {
            return new ActivityPlanDeploymentResult(
                ActivityPlanMutationStatus.DeploymentArtifactMissing);
        }

        var target = await ReadDeploymentTargetAsync(
            connection,
            transaction,
            state.TargetServerId,
            cancellationToken);
        if (target is null ||
            !IsConfiguredTarget(target) ||
            now - target.LastSeenAt > controlFreshness)
        {
            return new ActivityPlanDeploymentResult(
                ActivityPlanMutationStatus.DeploymentTargetUnavailable);
        }

        if (target.Online)
        {
            return new ActivityPlanDeploymentResult(
                ActivityPlanMutationStatus.DeploymentTargetOnline);
        }

        if (await HasActiveCommandAsync(
                connection,
                transaction,
                target.ServerId,
                cancellationToken))
        {
            return new ActivityPlanDeploymentResult(
                ActivityPlanMutationStatus.DeploymentOperationInProgress);
        }

        var initialMemoryMiB = target.Settings?.InitialMemoryMiB ??
            Math.Min(2048, package.Record.MaximumMemoryMiB);
        initialMemoryMiB = Math.Min(
            initialMemoryMiB,
            package.Record.MaximumMemoryMiB);
        if (initialMemoryMiB < 512 || initialMemoryMiB % 256 != 0)
        {
            return new ActivityPlanDeploymentResult(
                ActivityPlanMutationStatus.DeploymentTargetUnavailable);
        }

        var server = package.Analysis.Server;
        var deployment = new ServerPackageDeploymentRequest(
            package.Record.ImportId,
            package.Record.ProfileId,
            package.Record.Version,
            server.ArchiveBytes,
            server.Sha256,
            server.ExpandedBytes,
            server.FileCount,
            package.Record.PreserveWorldData,
            initialMemoryMiB,
            package.Record.MaximumMemoryMiB,
            package.Analysis.Metadata.JavaMajorVersion);
        var operationId = Guid.NewGuid();
        var operation = new AdminServerControlOperationRecord(
            operationId,
            target.ServerId,
            target.DisplayName,
            ServerControlAction.DeployPackage,
            ServerControlOperationStatus.Pending,
            request.Reason.Trim(),
            actorUserId,
            now,
            null,
            null,
            null,
            null,
            []);
        await InsertDeploymentAsync(
            connection,
            transaction,
            planId,
            target,
            deployment,
            operation,
            sourceIp,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ActivityPlanDeploymentResult(
            ActivityPlanMutationStatus.Success,
            new AdminServerControlQueueResult(operation, []));
    }

    private async Task<ActivityPlanMutationResult> ChangeStatusAsync(
        string planId,
        long expectedRevision,
        ActivityPlanStatus? expectedStatus,
        ActivityPlanStatus nextStatus,
        Guid actorUserId,
        IPAddress? sourceIp,
        DateTimeOffset now,
        CancellationToken cancellationToken,
        string? reason = null)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable,
            cancellationToken);
        var state = await ReadPlanStateAsync(
            connection,
            transaction,
            planId,
            cancellationToken);
        if (state is null)
        {
            return new ActivityPlanMutationResult(ActivityPlanMutationStatus.NotFound);
        }

        if (state.Revision != expectedRevision)
        {
            return new ActivityPlanMutationResult(
                ActivityPlanMutationStatus.RevisionConflict);
        }

        if (expectedStatus is not null && state.Status != expectedStatus ||
            expectedStatus is null && state.Status == ActivityPlanStatus.Archived)
        {
            return new ActivityPlanMutationResult(
                ActivityPlanMutationStatus.InvalidState);
        }

        if (nextStatus == ActivityPlanStatus.Published)
        {
            if (state.PackageImportId is not Guid packageImportId)
            {
                return new ActivityPlanMutationResult(
                    ActivityPlanMutationStatus.PackageBindingRequired);
            }

            var package = await ReadPackageAsync(
                connection,
                transaction,
                packageImportId,
                lockRows: true,
                cancellationToken);
            if (package is null)
            {
                return new ActivityPlanMutationResult(
                    ActivityPlanMutationStatus.PackageNotFound);
            }

            if (package.Record.ProfileArchived)
            {
                return new ActivityPlanMutationResult(
                    ActivityPlanMutationStatus.PackageProfileArchived);
            }

            if (!package.Record.ProductionReady)
            {
                return new ActivityPlanMutationResult(
                    ActivityPlanMutationStatus.PackageNotProductionReady);
            }

            if (state.TargetServerId is null)
            {
                return new ActivityPlanMutationResult(
                    ActivityPlanMutationStatus.TargetBindingRequired);
            }

            var target = await ReadDeploymentTargetAsync(
                connection,
                transaction,
                state.TargetServerId,
                cancellationToken);
            if (target is null || !IsConfiguredTarget(target))
            {
                return new ActivityPlanMutationResult(
                    ActivityPlanMutationStatus.TargetNotFound);
            }

            await AcquireScheduleLockAsync(connection, transaction, cancellationToken);
            var conflict = await FindScheduleConflictAsync(
                connection,
                transaction,
                planId,
                target.ServerId,
                state.OpensAt,
                state.ClosesAt,
                cancellationToken);
            if (conflict is not null)
            {
                return new ActivityPlanMutationResult(
                    ActivityPlanMutationStatus.ScheduleConflict,
                    Conflict: conflict);
            }
        }

        var sql = state.IsUnbound
            ? """
              UPDATE launcher.unbound_activity_plans
              SET activity_plan_status = $1,
                  revision = revision + 1,
                  updated_at = $2
              WHERE id = $3 AND revision = $4;
              """
            : """
              UPDATE launcher.servers
              SET activity_plan_status = $1,
                  is_visible = $2,
                  revision = revision + 1,
                  updated_at = $3
              WHERE id = $4
                AND activity_plan_status IS NOT NULL
                AND revision = $5;
              """;
        try
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue(nextStatus.ToString());
            if (state.IsUnbound)
            {
                command.Parameters.AddWithValue(now);
                command.Parameters.AddWithValue(planId);
                command.Parameters.AddWithValue(expectedRevision);
            }
            else
            {
                command.Parameters.AddWithValue(nextStatus == ActivityPlanStatus.Published);
                command.Parameters.AddWithValue(now);
                command.Parameters.AddWithValue(planId);
                command.Parameters.AddWithValue(expectedRevision);
            }

            if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                return new ActivityPlanMutationResult(
                    ActivityPlanMutationStatus.RevisionConflict);
            }
        }
        catch (PostgresException exception) when (
            exception.SqlState == PostgresErrorCodes.ExclusionViolation)
        {
            return new ActivityPlanMutationResult(
                ActivityPlanMutationStatus.ScheduleConflict);
        }

        await WriteAuditAsync(
            connection,
            transaction,
            actorUserId,
            sourceIp,
            $"activity_plan.{nextStatus.ToString().ToLowerInvariant()}",
            planId,
            state,
            new { Status = nextStatus, Reason = reason },
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ActivityPlanMutationResult(
            ActivityPlanMutationStatus.Success,
            await GetPlanAsync(planId, now, cancellationToken));
    }

    private async Task<AdminActivityPlanRecord?> GetPlanAsync(
        string planId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        const string where = "server.id = $1";
        var plans = await ReadPlansAsync(
            connection,
            now,
            cancellationToken,
            where,
            planId);
        if (plans.Count != 0)
        {
            return plans.Single();
        }

        return (await ReadUnboundPlansAsync(
            connection,
            cancellationToken,
            planId)).SingleOrDefault();
    }

    private async Task<IReadOnlyList<AdminActivityPlanRecord>> ReadPlansAsync(
        NpgsqlConnection connection,
        DateTimeOffset now,
        CancellationToken cancellationToken,
        string? additionalPredicate = null,
        string? planId = null)
    {
        var sql = $$"""
            SELECT server.id, server.display_name, server.announcement,
                   server.opens_at, server.closes_at, server.max_players,
                   server.minimum_tier, server.activity_package_import_id,
                   server.client_profile_id, package.plan::text,
                   package.analysis::text, server.activity_plan_status,
                   server.revision, server.created_at, server.updated_at,
                   EXISTS (
                       SELECT 1
                       FROM launcher.client_profile_channels channel
                       JOIN launcher.client_profile_releases release
                         ON release.profile_id = channel.profile_id
                        AND release.manifest_sha256 = channel.release_sha256
                       JOIN launcher.client_profiles profile
                         ON profile.id = channel.profile_id
                       WHERE channel.profile_id = server.client_profile_id
                         AND channel.channel = 'production'
                         AND channel.release_sha256 = package.manifest_sha256
                         AND NOT release.is_paused
                         AND profile.is_active
                         AND profile.archived_at IS NULL
                   ) AS production_ready,
                   target_server.id, target_server.display_name,
                   target_server.velocity_target,
                   target.reported_online, target.last_seen_at,
                   target.deployed_package_import_id
            FROM launcher.servers AS server
            JOIN launcher.package_imports AS package
              ON package.id = server.activity_package_import_id
            LEFT JOIN launcher.servers AS target_server
              ON target_server.id = server.activity_target_server_id
             AND target_server.activity_plan_status IS NULL
            LEFT JOIN launcher.server_control_targets AS target
              ON target.server_id = target_server.id
            WHERE server.activity_plan_status IS NOT NULL
              {{(additionalPredicate is null ? string.Empty : $"AND {additionalPredicate}")}}
            ORDER BY server.opens_at, server.closes_at, server.id;
            """;
        var result = new List<AdminActivityPlanRecord>();
        await using var command = new NpgsqlCommand(sql, connection);
        if (planId is not null)
        {
            command.Parameters.AddWithValue(planId);
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var packagePlan = Deserialize<PackageImportDeploymentPlanRecord>(
                reader.GetString(9));
            var analysis = Deserialize<PackageImportAnalysisRecord>(reader.GetString(10));
            if (!Enum.TryParse<ModLoaderKind>(
                    analysis.Metadata.Loader,
                    ignoreCase: true,
                    out var loader))
            {
                continue;
            }

            var opensAt = new DateTimeOffset(reader.GetDateTime(3));
            var closesAt = new DateTimeOffset(reader.GetDateTime(4));
            var packageImportId = reader.GetGuid(7);
            var status = Enum.Parse<ActivityPlanStatus>(
                reader.GetString(11),
                ignoreCase: true);
            var productionReady = reader.GetBoolean(15);
            var deploymentMatches = !reader.IsDBNull(21) &&
                reader.GetGuid(21) == packageImportId;
            var targetFresh = !reader.IsDBNull(20) &&
                now - new DateTimeOffset(reader.GetDateTime(20)) <= controlFreshness;
            var scheduled = status == ActivityPlanStatus.Published
                ? ServerAvailabilityRules.ResolveStatus(
                    ServerStatus.Online,
                    opensAt,
                    closesAt,
                    now)
                : ServerStatus.Closed;
            var effective = scheduled == ServerStatus.Online &&
                            productionReady &&
                            targetFresh &&
                            !reader.IsDBNull(19) &&
                            reader.GetBoolean(19) &&
                            deploymentMatches
                ? ServerStatus.Online
                : ServerStatus.Closed;
            result.Add(new AdminActivityPlanRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                opensAt,
                closesAt,
                reader.GetInt32(5),
                Enum.Parse<AccessTier>(reader.GetString(6), ignoreCase: true),
                packageImportId,
                reader.GetString(8),
                packagePlan.ProfileDisplayName,
                packagePlan.Version,
                analysis.Metadata.MinecraftVersion,
                loader,
                reader.IsDBNull(16) ? null : reader.GetString(16),
                reader.IsDBNull(17) ? null : reader.GetString(17),
                reader.IsDBNull(18) ? null : reader.GetString(18),
                status,
                effective,
                productionReady,
                deploymentMatches,
                reader.GetInt64(12),
                new DateTimeOffset(reader.GetDateTime(13)),
                new DateTimeOffset(reader.GetDateTime(14))));
        }

        return result;
    }

    private static async Task<IReadOnlyList<AdminActivityPlanRecord>>
        ReadUnboundPlansAsync(
            NpgsqlConnection connection,
            CancellationToken cancellationToken,
            string? planId = null)
    {
        var sql = planId is null
            ? """
              SELECT plan.id, plan.display_name, plan.announcement,
                     plan.opens_at, plan.closes_at, plan.max_players,
                     plan.minimum_tier, plan.activity_plan_status, plan.revision,
                     plan.created_at, plan.updated_at,
                     target.id, target.display_name, target.velocity_target
              FROM launcher.unbound_activity_plans AS plan
              LEFT JOIN launcher.servers AS target
                ON target.id = plan.target_server_id
              ORDER BY plan.opens_at, plan.closes_at, plan.id;
              """
            : """
              SELECT plan.id, plan.display_name, plan.announcement,
                     plan.opens_at, plan.closes_at, plan.max_players,
                     plan.minimum_tier, plan.activity_plan_status, plan.revision,
                     plan.created_at, plan.updated_at,
                     target.id, target.display_name, target.velocity_target
              FROM launcher.unbound_activity_plans AS plan
              LEFT JOIN launcher.servers AS target
                ON target.id = plan.target_server_id
              WHERE plan.id = $1
              ORDER BY plan.opens_at, plan.closes_at, plan.id;
              """;
        var result = new List<AdminActivityPlanRecord>();
        await using var command = new NpgsqlCommand(sql, connection);
        if (planId is not null)
        {
            command.Parameters.AddWithValue(planId);
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new AdminActivityPlanRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                new DateTimeOffset(reader.GetDateTime(3)),
                new DateTimeOffset(reader.GetDateTime(4)),
                reader.GetInt32(5),
                Enum.Parse<AccessTier>(reader.GetString(6), ignoreCase: true),
                PackageImportId: null,
                ProfileId: null,
                ProfileDisplayName: null,
                Version: null,
                MinecraftVersion: null,
                Loader: null,
                TargetServerId: reader.IsDBNull(11) ? null : reader.GetString(11),
                TargetDisplayName: reader.IsDBNull(12) ? null : reader.GetString(12),
                TargetVelocityTarget: reader.IsDBNull(13) ? null : reader.GetString(13),
                Status: Enum.Parse<ActivityPlanStatus>(reader.GetString(7), ignoreCase: true),
                EffectiveStatus: ServerStatus.Closed,
                ProductionReady: false,
                DeploymentMatches: false,
                Revision: reader.GetInt64(8),
                CreatedAt: new DateTimeOffset(reader.GetDateTime(9)),
                UpdatedAt: new DateTimeOffset(reader.GetDateTime(10))));
        }

        return result;
    }

    private static async Task<IReadOnlyList<AdminUnmanagedActivityScheduleRecord>>
        ReadUnmanagedSchedulesAsync(
            NpgsqlConnection connection,
            CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, display_name, announcement, opens_at, closes_at,
                   activity_package_import_id, client_profile_id, is_visible,
                   status, revision, updated_at
            FROM launcher.servers
            WHERE activity_plan_status IS NULL
              AND lower(velocity_target) = 'activity'
              AND (opens_at IS NOT NULL OR closes_at IS NOT NULL)
            ORDER BY opens_at NULLS LAST, closes_at NULLS LAST, id;
            """;
        var result = new List<AdminUnmanagedActivityScheduleRecord>();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            DateTimeOffset? opensAt = reader.IsDBNull(3)
                ? null
                : new DateTimeOffset(reader.GetDateTime(3));
            DateTimeOffset? closesAt = reader.IsDBNull(4)
                ? null
                : new DateTimeOffset(reader.GetDateTime(4));
            Guid? packageImportId = reader.IsDBNull(5)
                ? null
                : reader.GetGuid(5);
            result.Add(new AdminUnmanagedActivityScheduleRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                opensAt,
                closesAt,
                packageImportId,
                reader.GetString(6),
                reader.GetBoolean(7),
                Enum.Parse<ServerStatus>(reader.GetString(8), ignoreCase: true),
                reader.GetInt64(9),
                new DateTimeOffset(reader.GetDateTime(10)),
                UnmanagedActivityScheduleRules.GetIssues(
                    opensAt,
                    closesAt,
                    packageImportId)));
        }

        return result;
    }

    private static async Task<IReadOnlyList<AdminActivityPackageRecord>>
        ReadPackagesAsync(
            NpgsqlConnection connection,
            CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT package.id, package.plan::text, package.analysis::text,
                   package.manifest_sha256, package.completed_at,
                   profile.archived_at IS NOT NULL,
                   EXISTS (
                       SELECT 1
                       FROM launcher.client_profile_channels channel
                       JOIN launcher.client_profile_releases release
                         ON release.profile_id = channel.profile_id
                        AND release.manifest_sha256 = channel.release_sha256
                       WHERE channel.profile_id = profile.id
                         AND channel.channel = 'production'
                         AND channel.release_sha256 = package.manifest_sha256
                         AND NOT release.is_paused
                         AND profile.is_active
                         AND profile.archived_at IS NULL
                   ) AS production_ready
            FROM launcher.package_imports AS package
            JOIN launcher.client_profiles AS profile
              ON profile.id = package.plan ->> 'profileId'
            WHERE package.status = 'Completed'
              AND package.manifest_sha256 IS NOT NULL
              AND jsonb_typeof(package.analysis -> 'server') = 'object'
            ORDER BY package.completed_at DESC, package.id
            LIMIT 200;
            """;
        var result = new List<AdminActivityPackageRecord>();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var plan = Deserialize<PackageImportDeploymentPlanRecord>(reader.GetString(1));
            var analysis = Deserialize<PackageImportAnalysisRecord>(reader.GetString(2));
            if (!Enum.TryParse<ModLoaderKind>(
                    analysis.Metadata.Loader,
                    ignoreCase: true,
                    out var loader))
            {
                continue;
            }

            result.Add(new AdminActivityPackageRecord(
                reader.GetGuid(0),
                plan.ProfileId,
                plan.ProfileDisplayName,
                plan.Version,
                NormalizeTargetServerId(plan.TargetServerId) ??
                    PackageImportRules.ActivityServerId,
                reader.GetString(3),
                analysis.Metadata.MinecraftVersion,
                loader,
                analysis.Metadata.LoaderVersion,
                Math.Clamp(analysis.Metadata.MaximumPlayers ?? 30, 1, 1000),
                plan.MaximumMemoryMiB,
                plan.PreserveWorldData,
                reader.GetBoolean(6),
                reader.GetBoolean(5),
                new DateTimeOffset(reader.GetDateTime(4))));
        }

        return result;
    }

    private async Task<PackageSnapshot?> ReadPackageAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid importId,
        bool lockRows,
        CancellationToken cancellationToken)
    {
        var sql = $$"""
            SELECT package.id, package.plan::text, package.analysis::text,
                   package.manifest_sha256, package.completed_at,
                   profile.archived_at IS NOT NULL
            FROM launcher.package_imports AS package
            JOIN launcher.client_profiles AS profile
              ON profile.id = package.plan ->> 'profileId'
            WHERE package.id = $1
              AND package.status = 'Completed'
              AND package.manifest_sha256 IS NOT NULL
              AND jsonb_typeof(package.analysis -> 'server') = 'object'
            {{(lockRows ? "FOR SHARE OF package, profile" : string.Empty)}};
            """;
        Guid id;
        PackageImportDeploymentPlanRecord plan;
        PackageImportAnalysisRecord analysis;
        string manifestSha256;
        DateTimeOffset completedAt;
        bool profileArchived;
        await using (var command = new NpgsqlCommand(sql, connection, transaction))
        {
            command.Parameters.AddWithValue(importId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            id = reader.GetGuid(0);
            plan = Deserialize<PackageImportDeploymentPlanRecord>(reader.GetString(1));
            analysis = Deserialize<PackageImportAnalysisRecord>(reader.GetString(2));
            manifestSha256 = reader.GetString(3);
            completedAt = new DateTimeOffset(reader.GetDateTime(4));
            profileArchived = reader.GetBoolean(5);
        }

        if (!Enum.TryParse<ModLoaderKind>(
                analysis.Metadata.Loader,
                ignoreCase: true,
                out var loader))
        {
            return null;
        }

        var productionReady = await IsProductionReadyAsync(
            connection,
            transaction,
            plan.ProfileId,
            manifestSha256,
            lockRows,
            cancellationToken);
        var record = new AdminActivityPackageRecord(
            id,
            plan.ProfileId,
            plan.ProfileDisplayName,
            plan.Version,
            NormalizeTargetServerId(plan.TargetServerId) ??
                PackageImportRules.ActivityServerId,
            manifestSha256,
            analysis.Metadata.MinecraftVersion,
            loader,
            analysis.Metadata.LoaderVersion,
            Math.Clamp(analysis.Metadata.MaximumPlayers ?? 30, 1, 1000),
            plan.MaximumMemoryMiB,
            plan.PreserveWorldData,
            productionReady,
            profileArchived,
            completedAt);
        return new PackageSnapshot(record, analysis, plan);
    }

    private static async Task<bool> IsProductionReadyAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string profileId,
        string manifestSha256,
        bool lockRows,
        CancellationToken cancellationToken)
    {
        var sql = $$"""
            SELECT 1
            FROM launcher.client_profile_channels channel
            JOIN launcher.client_profile_releases release
              ON release.profile_id = channel.profile_id
             AND release.manifest_sha256 = channel.release_sha256
            JOIN launcher.client_profiles profile
              ON profile.id = channel.profile_id
            WHERE channel.profile_id = $1
              AND channel.channel = 'production'
              AND channel.release_sha256 = $2
              AND NOT release.is_paused
              AND profile.is_active
              AND profile.archived_at IS NULL
            {{(lockRows ? "FOR SHARE OF channel, release, profile" : string.Empty)}};
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(profileId);
        command.Parameters.AddWithValue(manifestSha256);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private static async Task<PlanState?> ReadPlanStateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string planId,
        CancellationToken cancellationToken)
    {
        const string boundSql = """
            SELECT display_name, opens_at, closes_at,
                   activity_package_import_id, activity_target_server_id,
                   activity_plan_status, revision, created_at
            FROM launcher.servers
            WHERE id = $1 AND activity_plan_status IS NOT NULL
            FOR UPDATE;
            """;
        await using (var command = new NpgsqlCommand(boundSql, connection, transaction))
        {
            command.Parameters.AddWithValue(planId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                return new PlanState(
                    Title: reader.GetString(0),
                    OpensAt: new DateTimeOffset(reader.GetDateTime(1)),
                    ClosesAt: new DateTimeOffset(reader.GetDateTime(2)),
                    PackageImportId: reader.GetGuid(3),
                    TargetServerId: reader.GetString(4),
                    Status: Enum.Parse<ActivityPlanStatus>(
                        reader.GetString(5),
                        ignoreCase: true),
                    Revision: reader.GetInt64(6),
                    IsUnbound: false,
                    CreatedAt: new DateTimeOffset(reader.GetDateTime(7)));
            }
        }

        const string unboundSql = """
            SELECT display_name, opens_at, closes_at,
                   target_server_id, activity_plan_status, revision, created_at
            FROM launcher.unbound_activity_plans
            WHERE id = $1
            FOR UPDATE;
            """;
        await using var unboundCommand = new NpgsqlCommand(
            unboundSql,
            connection,
            transaction);
        unboundCommand.Parameters.AddWithValue(planId);
        await using var unboundReader = await unboundCommand.ExecuteReaderAsync(
            cancellationToken);
        return await unboundReader.ReadAsync(cancellationToken)
            ? new PlanState(
                Title: unboundReader.GetString(0),
                OpensAt: new DateTimeOffset(unboundReader.GetDateTime(1)),
                ClosesAt: new DateTimeOffset(unboundReader.GetDateTime(2)),
                PackageImportId: null,
                TargetServerId: unboundReader.IsDBNull(3)
                    ? null
                    : unboundReader.GetString(3),
                Status: Enum.Parse<ActivityPlanStatus>(
                    unboundReader.GetString(4),
                    ignoreCase: true),
                Revision: unboundReader.GetInt64(5),
                IsUnbound: true,
                CreatedAt: new DateTimeOffset(unboundReader.GetDateTime(6)))
            : null;
    }

    private static async Task AcquireScheduleLockAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock($1);",
            connection,
            transaction);
        command.Parameters.AddWithValue(ScheduleAdvisoryLock);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<ActivityPlanScheduleConflict?>
        FindScheduleConflictAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            string planId,
            string targetServerId,
            DateTimeOffset opensAt,
            DateTimeOffset closesAt,
            CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, display_name, opens_at, closes_at
            FROM launcher.servers
            WHERE activity_plan_status = 'Published'
              AND id <> $1
              AND activity_target_server_id = $2
              AND opens_at < $4
              AND closes_at > $3
            ORDER BY opens_at, id
            LIMIT 1
            FOR SHARE;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(planId);
        command.Parameters.AddWithValue(targetServerId);
        command.Parameters.AddWithValue(opensAt.ToUniversalTime());
        command.Parameters.AddWithValue(closesAt.ToUniversalTime());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new ActivityPlanScheduleConflict(
                reader.GetString(0),
                reader.GetString(1),
                new DateTimeOffset(reader.GetDateTime(2)),
                new DateTimeOffset(reader.GetDateTime(3)))
            : null;
    }

    private async Task<IReadOnlyList<AdminActivityTargetRecord>> ReadTargetsAsync(
        NpgsqlConnection connection,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT server.id, server.display_name, server.velocity_target,
                   target.agent_id, target.conflict_group, target.port,
                   target.last_seen_at, target.reported_online,
                   target.package_deployment_enabled, target.server_files_present,
                   target.host_total_memory_mib,
                   target.deployed_package_import_id,
                   target.deployed_profile_id, target.deployed_version,
                   target.settings::text,
                   slot.server_id IS NOT NULL, slot.status,
                   operation.id, operation.action, operation.status,
                   operation.reason, operation.requested_by,
                   operation.requested_at, operation.started_at,
                   operation.completed_at, operation.result_code,
                   operation.result_message,
                   operation.automatically_stopping_server_ids
            FROM launcher.servers AS server
            JOIN launcher.server_control_targets AS target
              ON target.server_id = server.id
            LEFT JOIN launcher.deployment_slots AS slot
              ON slot.server_id = target.server_id
            LEFT JOIN LATERAL (
                SELECT operation.*
                FROM launcher.server_control_operations operation
                WHERE operation.target_server_id = target.server_id
                  AND operation.status IN ('Pending', 'Running')
                ORDER BY operation.requested_at DESC
                LIMIT 1
            ) operation ON true
            WHERE server.activity_plan_status IS NULL
              AND server.server_role = 'Player'
              AND (
                  target.package_deployment_enabled
                  OR target.server_id = 'activity'
                  OR slot.server_id IS NOT NULL
              )
            ORDER BY server.sort_order, server.display_name, server.id;
            """;
        var result = new List<AdminActivityTargetRecord>();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var target = new DeploymentTarget(
                ServerId: reader.GetString(0),
                DisplayName: reader.GetString(1),
                VelocityTarget: reader.GetString(2),
                AgentId: reader.GetString(3),
                ConflictGroup: reader.IsDBNull(4) ? null : reader.GetString(4),
                Port: reader.GetInt32(5),
                Online: reader.GetBoolean(7),
                LastSeenAt: new DateTimeOffset(reader.GetDateTime(6)),
                PackageDeploymentEnabled: reader.GetBoolean(8),
                ServerFilesPresent: reader.GetBoolean(9),
                HostTotalMemoryMiB: reader.IsDBNull(10) ? null : reader.GetInt32(10),
                DeployedPackage: reader.IsDBNull(11)
                    ? null
                    : new ServerPackageDeploymentIdentity(
                        reader.GetGuid(11),
                        reader.GetString(12),
                        reader.GetString(13)),
                Settings: reader.IsDBNull(14)
                    ? null
                    : Deserialize<ServerQuickSettings>(reader.GetString(14)),
                DynamicDeploymentSlot: reader.GetBoolean(15),
                DeploymentSlotStatus: reader.IsDBNull(16)
                    ? null
                    : Enum.Parse<DeploymentSlotProvisioningStatus>(
                        reader.GetString(16),
                        ignoreCase: true));
            AdminServerControlOperationRecord? operation = null;
            if (!reader.IsDBNull(17))
            {
                operation = new AdminServerControlOperationRecord(
                    reader.GetGuid(17),
                    target.ServerId,
                    target.DisplayName,
                    Enum.Parse<ServerControlAction>(
                        reader.GetString(18),
                        ignoreCase: true),
                    Enum.Parse<ServerControlOperationStatus>(
                        reader.GetString(19),
                        ignoreCase: true),
                    reader.GetString(20),
                    reader.GetGuid(21),
                    new DateTimeOffset(reader.GetDateTime(22)),
                    reader.IsDBNull(23)
                        ? null
                        : new DateTimeOffset(reader.GetDateTime(23)),
                    reader.IsDBNull(24)
                        ? null
                        : new DateTimeOffset(reader.GetDateTime(24)),
                    reader.IsDBNull(25) ? null : reader.GetString(25),
                    reader.IsDBNull(26) ? null : reader.GetString(26),
                    reader.GetFieldValue<string[]>(27));
            }

            result.Add(new AdminActivityTargetRecord(
                target.ServerId,
                target.DisplayName,
                target.VelocityTarget,
                target.Port,
                IsConfiguredTarget(target),
                now - target.LastSeenAt <= controlFreshness,
                target.Online,
                target.ServerFilesPresent,
                target.DeployedPackage,
                operation,
                PackageImportRules.ResolvePackageDeploymentMemoryGuidance(
                    target.ServerId,
                    target.AgentId,
                    target.ConflictGroup,
                    target.Port,
                    target.PackageDeploymentEnabled,
                    target.HostTotalMemoryMiB)));
        }

        return result;
    }

    private static AdminActivitySlotRecord ToLegacySlot(AdminActivityTargetRecord? target) =>
        target is null
            ? new AdminActivitySlotRecord(
                Configured: false,
                AgentConnected: false,
                Online: false,
                ServerFilesPresent: false,
                DeployedPackage: null,
                ActiveOperation: null,
                MemoryGuidance: null)
            : new AdminActivitySlotRecord(
                target.Configured,
                target.AgentConnected,
                target.Online,
                target.ServerFilesPresent,
                target.DeployedPackage,
                target.ActiveOperation,
                target.MemoryGuidance);

    private static async Task<DeploymentTarget?> ResolveTargetAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string? requestedTargetServerId,
        string? existingTargetServerId,
        PackageSnapshot? package,
        CancellationToken cancellationToken)
    {
        var targetServerId = ResolveTargetServerId(
            requestedTargetServerId,
            existingTargetServerId,
            package);
        if (targetServerId is null)
        {
            return null;
        }

        var target = await ReadDeploymentTargetAsync(
            connection,
            transaction,
            targetServerId,
            cancellationToken);
        return target is not null && IsConfiguredTarget(target) ? target : null;
    }

    private static string? ResolveTargetServerId(
        string? requestedTargetServerId,
        string? existingTargetServerId,
        PackageSnapshot? package) =>
        NormalizeTargetServerId(requestedTargetServerId) ??
        NormalizeTargetServerId(existingTargetServerId) ??
        NormalizeTargetServerId(package?.Record.TargetServerId) ??
        (package is null ? null : PackageImportRules.ActivityServerId);

    private static string? NormalizeTargetServerId(string? serverId) =>
        string.IsNullOrWhiteSpace(serverId) ? null : serverId.Trim();

    private static async Task<DeploymentTarget?> ReadDeploymentTargetAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string serverId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT server.id, server.display_name, server.velocity_target,
                   target.agent_id, target.conflict_group, target.port,
                   target.reported_online, target.last_seen_at,
                   target.package_deployment_enabled, target.settings::text,
                   target.server_files_present, target.host_total_memory_mib,
                   target.deployed_package_import_id,
                   target.deployed_profile_id, target.deployed_version,
                   slot.server_id IS NOT NULL, slot.status
            FROM launcher.servers AS server
            JOIN launcher.server_control_targets AS target
              ON target.server_id = server.id
            LEFT JOIN launcher.deployment_slots AS slot
              ON slot.server_id = target.server_id
            WHERE server.id = $1
              AND server.activity_plan_status IS NULL
              AND server.server_role = 'Player'
            FOR UPDATE OF server, target;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(serverId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new DeploymentTarget(
            ServerId: reader.GetString(0),
            DisplayName: reader.GetString(1),
            VelocityTarget: reader.GetString(2),
            AgentId: reader.GetString(3),
            ConflictGroup: reader.IsDBNull(4) ? null : reader.GetString(4),
            Port: reader.GetInt32(5),
            Online: reader.GetBoolean(6),
            LastSeenAt: new DateTimeOffset(reader.GetDateTime(7)),
            PackageDeploymentEnabled: reader.GetBoolean(8),
            ServerFilesPresent: reader.GetBoolean(10),
            HostTotalMemoryMiB: reader.IsDBNull(11) ? null : reader.GetInt32(11),
            DeployedPackage: reader.IsDBNull(12)
                ? null
                : new ServerPackageDeploymentIdentity(
                    reader.GetGuid(12),
                    reader.GetString(13),
                    reader.GetString(14)),
            Settings: reader.IsDBNull(9)
                ? null
                : Deserialize<ServerQuickSettings>(reader.GetString(9)),
            DynamicDeploymentSlot: reader.GetBoolean(15),
            DeploymentSlotStatus: reader.IsDBNull(16)
                ? null
                : Enum.Parse<DeploymentSlotProvisioningStatus>(
                    reader.GetString(16),
                    ignoreCase: true));
    }

    private static bool IsConfiguredTarget(DeploymentTarget target) =>
        target.PackageDeploymentEnabled &&
        PackageImportRules.IsPackageDeploymentTarget(
            target.AgentId,
            target.ConflictGroup,
            target.Port) &&
        (!target.DynamicDeploymentSlot ||
         target.DeploymentSlotStatus is not DeploymentSlotProvisioningStatus.Provisioning
             and not DeploymentSlotProvisioningStatus.Failed) &&
        string.Equals(
            target.VelocityTarget,
            target.ServerId,
            StringComparison.Ordinal);

    private static async Task<bool> HasActiveCommandAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string serverId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT EXISTS (
                SELECT 1
                FROM launcher.server_control_commands
                WHERE server_id = $1 AND status IN ('Pending', 'Claimed')
            );
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(serverId);
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    private static async Task InsertDeploymentAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string planId,
        DeploymentTarget target,
        ServerPackageDeploymentRequest deployment,
        AdminServerControlOperationRecord operation,
        IPAddress? sourceIp,
        CancellationToken cancellationToken)
    {
        await using (var insertOperation = new NpgsqlCommand(
                         """
                         INSERT INTO launcher.server_control_operations
                             (id, target_server_id, action, status, reason,
                              requested_by, source_ip, requested_at,
                              automatically_stopping_server_ids)
                         VALUES ($1, $2, 'DeployPackage', 'Pending', $3, $4,
                                 $5, $6, ARRAY[]::text[]);
                         """,
                         connection,
                         transaction))
        {
            insertOperation.Parameters.AddWithValue(operation.OperationId);
            insertOperation.Parameters.AddWithValue(target.ServerId);
            insertOperation.Parameters.AddWithValue(operation.Reason);
            insertOperation.Parameters.AddWithValue(operation.RequestedBy);
            AdminPostgresParameters.AddPositional(
                insertOperation.Parameters,
                NpgsqlDbType.Inet,
                sourceIp);
            insertOperation.Parameters.AddWithValue(operation.RequestedAt);
            await insertOperation.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var insertCommand = new NpgsqlCommand(
                         """
                         INSERT INTO launcher.server_control_commands
                             (id, operation_id, sequence, server_id, agent_id,
                              kind, payload)
                         VALUES ($1, $2, 0, $3, $4, 'DeployPackage', $5);
                         """,
                         connection,
                         transaction))
        {
            insertCommand.Parameters.AddWithValue(Guid.NewGuid());
            insertCommand.Parameters.AddWithValue(operation.OperationId);
            insertCommand.Parameters.AddWithValue(target.ServerId);
            insertCommand.Parameters.AddWithValue(target.AgentId);
            insertCommand.Parameters.AddWithValue(
                NpgsqlDbType.Jsonb,
                JsonSerializer.Serialize(
                    new DeploymentCommandPayload(PackageDeployment: deployment),
                    JsonOptions));
            await insertCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var link = new NpgsqlCommand(
                         """
                         INSERT INTO launcher.activity_plan_deployments
                             (operation_id, activity_plan_id, package_import_id,
                              requested_by, requested_at)
                         VALUES ($1, $2, $3, $4, $5);
                         """,
                         connection,
                         transaction))
        {
            link.Parameters.AddWithValue(operation.OperationId);
            link.Parameters.AddWithValue(planId);
            link.Parameters.AddWithValue(deployment.ImportId);
            link.Parameters.AddWithValue(operation.RequestedBy);
            link.Parameters.AddWithValue(operation.RequestedAt);
            await link.ExecuteNonQueryAsync(cancellationToken);
        }

        await WriteAuditAsync(
            connection,
            transaction,
            operation.RequestedBy,
            sourceIp,
            "activity_plan.deployment_queued",
            planId,
            before: null,
            after: new
            {
                operation.OperationId,
                deployment.ImportId,
                deployment.ProfileId,
                deployment.Version,
                TargetServerId = target.ServerId
            },
            cancellationToken);
    }

    private static async Task WriteAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid actorUserId,
        IPAddress? sourceIp,
        string action,
        string targetId,
        object? before,
        object? after,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO launcher.audit_logs
                (actor_user_id, action, target_type, target_id, source_ip,
                 before_data, after_data)
            VALUES ($1, $2, 'activity_plan', $3, $4, $5, $6);
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(actorUserId);
        command.Parameters.AddWithValue(action);
        command.Parameters.AddWithValue(targetId);
        AdminPostgresParameters.AddPositional(
            command.Parameters,
            NpgsqlDbType.Inet,
            sourceIp);
        AdminPostgresParameters.AddPositional(
            command.Parameters,
            NpgsqlDbType.Jsonb,
            before is null ? null : JsonSerializer.Serialize(before, JsonOptions));
        AdminPostgresParameters.AddPositional(
            command.Parameters,
            NpgsqlDbType.Jsonb,
            after is null ? null : JsonSerializer.Serialize(after, JsonOptions));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string CreatePlanId(DateTimeOffset opensAt) =>
        $"activity-{opensAt:yyyyMMdd}-{Guid.NewGuid():N}"[..27];

    private static string TakeTextElements(string value, int maximum)
    {
        var elements = StringInfo.GetTextElementEnumerator(value.Trim());
        var result = new List<string>(maximum);
        while (result.Count < maximum && elements.MoveNext())
        {
            result.Add(elements.GetTextElement());
        }

        return string.Concat(result);
    }

    private static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, JsonOptions)
        ?? throw new InvalidDataException($"Stored JSON is empty: {typeof(T).Name}");

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private sealed record PackageSnapshot(
        AdminActivityPackageRecord Record,
        PackageImportAnalysisRecord Analysis,
        PackageImportDeploymentPlanRecord Plan);

    private sealed record PlanState(
        string Title,
        DateTimeOffset OpensAt,
        DateTimeOffset ClosesAt,
        Guid? PackageImportId,
        string? TargetServerId,
        ActivityPlanStatus Status,
        long Revision,
        bool IsUnbound,
        DateTimeOffset CreatedAt);

    private sealed record DeploymentTarget(
        string ServerId,
        string DisplayName,
        string VelocityTarget,
        string AgentId,
        string? ConflictGroup,
        int Port,
        bool Online,
        DateTimeOffset LastSeenAt,
        bool PackageDeploymentEnabled,
        bool ServerFilesPresent,
        int? HostTotalMemoryMiB,
        ServerPackageDeploymentIdentity? DeployedPackage,
        ServerQuickSettings? Settings,
        bool DynamicDeploymentSlot,
        DeploymentSlotProvisioningStatus? DeploymentSlotStatus);

    private sealed record DeploymentCommandPayload(
        string? ConsoleCommand = null,
        ServerQuickSettings? Settings = null,
        ServerPackageDeploymentRequest? PackageDeployment = null);
}

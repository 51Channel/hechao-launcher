using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hechao.Api.Admin;
using Hechao.Api.ServerControl;
using Hechao.Contracts;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Hechao.Api.PackageImports;

internal enum PackageImportOrchestrationOutcome
{
    NoWork,
    Waiting,
    Progressed
}

internal sealed class PackageImportOrchestrationRepository(
    NpgsqlDataSource dataSource,
    IOptions<ServerControlOptions> serverControlOptions)
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly ServerControlOptions controlOptions = serverControlOptions.Value;

    internal async Task<PackageImportOrchestrationOutcome>
        TryQueueDeploymentAsync(
            DateTimeOffset now,
            CancellationToken cancellationToken)
    {
        await using var connection =
            await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable,
            cancellationToken);
        var package = await ReadQueuedPackageAsync(
            connection,
            transaction,
            cancellationToken);
        if (package is null)
        {
            return PackageImportOrchestrationOutcome.NoWork;
        }

        if (package.Analysis.Server is null ||
            string.IsNullOrWhiteSpace(package.ManifestSha256))
        {
            await FailPackageAsync(
                connection,
                transaction,
                package.ImportId,
                "DEPLOYMENT_METADATA_INVALID",
                "服务端归档或客户端签名清单元数据缺失，正式通道未发生变化。",
                now,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return PackageImportOrchestrationOutcome.Progressed;
        }

        var target = await ReadTargetForUpdateAsync(
            connection,
            transaction,
            package.Plan.TargetServerId,
            cancellationToken);
        if (target is null || !PackageImportRules.IsActivityTarget(
                target.ServerId,
                target.AgentId,
                target.ConflictGroup,
                target.Port))
        {
            await FailPackageAsync(
                connection,
                transaction,
                package.ImportId,
                "DEPLOYMENT_TARGET_INVALID",
                "部署目标不再是受控的 owl5 活动槽，服务端未切换。",
                now,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return PackageImportOrchestrationOutcome.Progressed;
        }

        if (!target.PackageDeploymentEnabled)
        {
            await FailPackageAsync(
                connection,
                transaction,
                package.ImportId,
                "DEPLOYMENT_CAPABILITY_DISABLED",
                "目标服控代理未启用整合包部署能力，服务端未切换。",
                now,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return PackageImportOrchestrationOutcome.Progressed;
        }

        if (now - target.LastSeenAt >
            TimeSpan.FromSeconds(controlOptions.AgentFreshnessSeconds) ||
            target.Online ||
            await HasActiveCommandAsync(
                connection,
                transaction,
                target.ServerId,
                cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return PackageImportOrchestrationOutcome.Waiting;
        }

        var initialMemoryMiB = target.Settings?.InitialMemoryMiB ??
            Math.Min(2048, package.Plan.MaximumMemoryMiB);
        initialMemoryMiB = Math.Min(initialMemoryMiB, package.Plan.MaximumMemoryMiB);
        if (initialMemoryMiB < 512 || initialMemoryMiB % 256 != 0)
        {
            await FailPackageAsync(
                connection,
                transaction,
                package.ImportId,
                "DEPLOYMENT_MEMORY_INVALID",
                "目标服务端初始内存不是 256 MiB 整数倍，服务端未切换。",
                now,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return PackageImportOrchestrationOutcome.Progressed;
        }

        var server = package.Analysis.Server;
        var deployment = new ServerPackageDeploymentRequest(
            package.ImportId,
            package.Plan.ProfileId,
            package.Plan.Version,
            server.ArchiveBytes,
            server.Sha256,
            server.ExpandedBytes,
            server.FileCount,
            package.Plan.PreserveWorldData,
            initialMemoryMiB,
            package.Plan.MaximumMemoryMiB);
        var operationId = Guid.NewGuid();
        await InsertDeploymentOperationAsync(
            connection,
            transaction,
            operationId,
            package,
            target,
            deployment,
            now,
            cancellationToken);
        await WriteEventAsync(
            connection,
            transaction,
            package.ImportId,
            PackageImportStatus.DeployingServer,
            "SERVER_DEPLOYMENT_QUEUED",
            "服务端原子部署已排队；部署结束后服务端保持停止。",
            now,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return PackageImportOrchestrationOutcome.Progressed;
    }

    internal async Task<PackageImportOrchestrationOutcome>
        ReconcileDeploymentAsync(
            DateTimeOffset now,
            CancellationToken cancellationToken)
    {
        await using var connection =
            await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction =
            await connection.BeginTransactionAsync(cancellationToken);
        const string sql = """
            SELECT package.id,
                   operation.status,
                   operation.result_code,
                   operation.result_message
            FROM launcher.package_imports AS package
            JOIN launcher.server_control_operations AS operation
              ON operation.id = package.deployment_operation_id
            WHERE package.status = 'DeployingServer'
              AND operation.status IN ('Succeeded', 'Failed', 'Cancelled')
            ORDER BY package.updated_at, package.id
            LIMIT 1
            FOR UPDATE OF package SKIP LOCKED;
            """;
        Guid importId;
        string status;
        string? resultCode;
        string? resultMessage;
        await using (var command = new NpgsqlCommand(sql, connection, transaction))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
            {
                return PackageImportOrchestrationOutcome.NoWork;
            }

            importId = reader.GetGuid(0);
            status = reader.GetString(1);
            resultCode = reader.IsDBNull(2) ? null : reader.GetString(2);
            resultMessage = reader.IsDBNull(3) ? null : reader.GetString(3);
        }

        if (string.Equals(status, "Succeeded", StringComparison.Ordinal))
        {
            await using var update = new NpgsqlCommand(
                """
                UPDATE launcher.package_imports
                SET status = 'Finalizing',
                    error_code = NULL,
                    error_message = NULL,
                    revision = revision + 1,
                    updated_at = $2
                WHERE id = $1 AND status = 'DeployingServer';
                """,
                connection,
                transaction);
            update.Parameters.AddWithValue(importId);
            update.Parameters.AddWithValue(now);
            await update.ExecuteNonQueryAsync(cancellationToken);
            await WriteEventAsync(
                connection,
                transaction,
                importId,
                PackageImportStatus.Finalizing,
                "SERVER_DEPLOYED_STOPPED",
                "服务端已完成原子切换并保持停止，正在收口测试通道与目录。",
                now,
                cancellationToken);
        }
        else
        {
            await FailPackageAsync(
                connection,
                transaction,
                importId,
                "SERVER_DEPLOYMENT_FAILED",
                LimitMessage(
                    $"服务端部署失败，正式通道未发生变化。" +
                    $" {resultCode ?? "UNKNOWN"}: {resultMessage ?? "无结果说明。"}",
                    2000),
                now,
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return PackageImportOrchestrationOutcome.Progressed;
    }

    internal async Task<PackageImportOrchestrationOutcome> FinalizeAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var connection =
            await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction =
            await connection.BeginTransactionAsync(cancellationToken);
        var package = await ReadFinalizingPackageAsync(
            connection,
            transaction,
            cancellationToken);
        if (package is null)
        {
            return PackageImportOrchestrationOutcome.NoWork;
        }

        var profileIsArchived = await ReadProfileArchivedForUpdateAsync(
            connection,
            transaction,
            package.Plan.ProfileId,
            cancellationToken);
        if (profileIsArchived is null || profileIsArchived.Value)
        {
            await FailPackageAsync(
                connection,
                transaction,
                package.ImportId,
                profileIsArchived is true
                    ? "PROFILE_ARCHIVED"
                    : "FINALIZATION_PROFILE_MISSING",
                profileIsArchived is true
                    ? "目标客户端档案在整合包收口前已归档；服务端保持停止，Test 与正式通道未变化。"
                    : "目标客户端档案在整合包收口前不存在；服务端保持停止，Test 与正式通道未变化。",
                now,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return PackageImportOrchestrationOutcome.Progressed;
        }

        if (string.IsNullOrWhiteSpace(package.ManifestSha256) ||
            !await IsUsableReleaseAsync(
                connection,
                transaction,
                package.Plan.ProfileId,
                package.ManifestSha256,
                cancellationToken))
        {
            await FailPackageAsync(
                connection,
                transaction,
                package.ImportId,
                "FINALIZATION_RELEASE_INVALID",
                "客户端发布在收口前缺失或已暂停；服务端保持停止，正式通道未变化。",
                now,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return PackageImportOrchestrationOutcome.Progressed;
        }

        if (package.Plan.SyncServerCatalog)
        {
            var catalogError = await SynchronizeCatalogAsync(
                connection,
                transaction,
                package,
                now,
                cancellationToken);
            if (catalogError is not null)
            {
                await FailPackageAsync(
                    connection,
                    transaction,
                    package.ImportId,
                    catalogError.Value.Code,
                    catalogError.Value.Message,
                    now,
                    cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return PackageImportOrchestrationOutcome.Progressed;
            }
        }

        await AssignTestChannelAsync(
            connection,
            transaction,
            package,
            now,
            cancellationToken);
        await EnableProfileForTestingAsync(
            connection,
            transaction,
            package,
            now,
            cancellationToken);

        await using (var update = new NpgsqlCommand(
                         """
                         UPDATE launcher.package_imports
                         SET status = 'Completed',
                             error_code = NULL,
                             error_message = NULL,
                             completed_at = $2,
                             revision = revision + 1,
                             updated_at = $2
                         WHERE id = $1 AND status = 'Finalizing';
                         """,
                         connection,
                         transaction))
        {
            update.Parameters.AddWithValue(package.ImportId);
            update.Parameters.AddWithValue(now);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        await WriteEventAsync(
            connection,
            transaction,
            package.ImportId,
            PackageImportStatus.Completed,
            "IMPORT_COMPLETED",
            "客户端档案已启用并进入 Test 通道，服务端已部署并保持停止。",
            now,
            cancellationToken);
        await WriteAuditAsync(
            connection,
            transaction,
            package.CreatedBy,
            "package_import.completed",
            "package_import",
            package.ImportId.ToString("D"),
            null,
            JsonSerializer.Serialize(new
            {
                package.Plan.ProfileId,
                package.Plan.Version,
                package.Plan.TargetServerId,
                package.ManifestSha256,
                package.DeploymentOperationId,
                package.Plan.SyncServerCatalog
            }, JsonOptions),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return PackageImportOrchestrationOutcome.Progressed;
    }

    private static async Task<QueuedPackage?> ReadQueuedPackageAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, analysis::text, plan::text, manifest_sha256, created_by
            FROM launcher.package_imports
            WHERE status = 'QueuedForDeployment'
            ORDER BY updated_at, id
            LIMIT 1
            FOR UPDATE SKIP LOCKED;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new QueuedPackage(
            reader.GetGuid(0),
            Deserialize<PackageImportAnalysisRecord>(reader.GetString(1)),
            Deserialize<PackageImportDeploymentPlanRecord>(reader.GetString(2)),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.GetGuid(4));
    }

    private static async Task<FinalizingPackage?> ReadFinalizingPackageAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, analysis::text, plan::text, manifest_sha256, created_by,
                   deployment_operation_id
            FROM launcher.package_imports
            WHERE status = 'Finalizing'
            ORDER BY updated_at, id
            LIMIT 1
            FOR UPDATE SKIP LOCKED;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new FinalizingPackage(
            reader.GetGuid(0),
            Deserialize<PackageImportAnalysisRecord>(reader.GetString(1)),
            Deserialize<PackageImportDeploymentPlanRecord>(reader.GetString(2)),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.GetGuid(4),
            reader.GetGuid(5));
    }

    private static async Task<DeploymentTarget?> ReadTargetForUpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string serverId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT server_id, agent_id, conflict_group, port, reported_online,
                   last_seen_at, package_deployment_enabled, settings::text,
                   server_files_present
            FROM launcher.server_control_targets
            WHERE server_id = $1
            FOR UPDATE;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(serverId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new DeploymentTarget(
            reader.GetString(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetInt32(3),
            reader.GetBoolean(4),
            new DateTimeOffset(reader.GetDateTime(5)),
            reader.GetBoolean(6),
            reader.IsDBNull(7)
                ? null
                : Deserialize<ServerQuickSettings>(reader.GetString(7)),
            reader.GetBoolean(8));
    }

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
                WHERE server_id = $1
                  AND status IN ('Pending', 'Claimed')
            );
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(serverId);
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    private static async Task InsertDeploymentOperationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid operationId,
        QueuedPackage package,
        DeploymentTarget target,
        ServerPackageDeploymentRequest deployment,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var reason = $"部署整合包 {package.ImportId:D}";
        await using (var operation = new NpgsqlCommand(
                         """
                         INSERT INTO launcher.server_control_operations
                             (id, target_server_id, action, status, reason,
                              requested_by, requested_at,
                              automatically_stopping_server_ids)
                         VALUES ($1, $2, 'DeployPackage', 'Pending', $3, $4, $5,
                                 ARRAY[]::text[]);
                         """,
                         connection,
                         transaction))
        {
            operation.Parameters.AddWithValue(operationId);
            operation.Parameters.AddWithValue(target.ServerId);
            operation.Parameters.AddWithValue(reason);
            operation.Parameters.AddWithValue(package.CreatedBy);
            operation.Parameters.AddWithValue(now);
            await operation.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var command = new NpgsqlCommand(
                         """
                         INSERT INTO launcher.server_control_commands
                             (id, operation_id, sequence, server_id, agent_id,
                              kind, payload)
                         VALUES ($1, $2, 0, $3, $4, 'DeployPackage', $5);
                         """,
                         connection,
                         transaction))
        {
            command.Parameters.AddWithValue(Guid.NewGuid());
            command.Parameters.AddWithValue(operationId);
            command.Parameters.AddWithValue(target.ServerId);
            command.Parameters.AddWithValue(target.AgentId);
            command.Parameters.AddWithValue(
                NpgsqlDbType.Jsonb,
                JsonSerializer.Serialize(
                    new DeploymentCommandPayload(PackageDeployment: deployment),
                    JsonOptions));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var update = new NpgsqlCommand(
                         """
                         UPDATE launcher.package_imports
                         SET status = 'DeployingServer',
                             deployment_operation_id = $2,
                             error_code = NULL,
                             error_message = NULL,
                             revision = revision + 1,
                             updated_at = $3
                         WHERE id = $1 AND status = 'QueuedForDeployment';
                         """,
                         connection,
                         transaction))
        {
            update.Parameters.AddWithValue(package.ImportId);
            update.Parameters.AddWithValue(operationId);
            update.Parameters.AddWithValue(now);
            if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new InvalidOperationException(
                    "The package deployment queue state changed unexpectedly.");
            }
        }

        await WriteAuditAsync(
            connection,
            transaction,
            package.CreatedBy,
            "server_control.operation.queued",
            "server_control_operation",
            operationId.ToString("D"),
            null,
            JsonSerializer.Serialize(new
            {
                Action = ServerControlAction.DeployPackage,
                ServerId = target.ServerId,
                package.ImportId,
                package.Plan.ProfileId,
                package.Plan.Version,
                AutomaticallyStoppingServerIds = Array.Empty<string>()
            }, JsonOptions),
            cancellationToken);
    }

    private static async Task<bool?> ReadProfileArchivedForUpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string profileId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT archived_at IS NOT NULL
            FROM launcher.client_profiles
            WHERE id = $1
            FOR UPDATE;
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(profileId);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is bool isArchived ? isArchived : null;
    }

    private static async Task<bool> IsUsableReleaseAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string profileId,
        string manifestSha256,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT NOT is_paused
            FROM launcher.client_profile_releases
            WHERE profile_id = $1 AND manifest_sha256 = $2
            FOR SHARE;
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(profileId);
        command.Parameters.AddWithValue(manifestSha256);
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    private static async Task AssignTestChannelAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        FinalizingPackage package,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        const string readSql = """
            SELECT release_sha256, rollout_percentage, revision, updated_at
            FROM launcher.client_profile_channels
            WHERE profile_id = $1 AND channel = 'test'
            FOR UPDATE;
            """;
        string? beforeSha;
        int beforeRollout;
        long beforeRevision;
        DateTimeOffset beforeUpdatedAt;
        await using (var read = new NpgsqlCommand(readSql, connection, transaction))
        {
            read.Parameters.AddWithValue(package.Plan.ProfileId);
            await using var reader = await read.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidDataException(
                    "The imported client profile has no Test channel.");
            }

            beforeSha = reader.IsDBNull(0) ? null : reader.GetString(0);
            beforeRollout = reader.GetInt32(1);
            beforeRevision = reader.GetInt64(2);
            beforeUpdatedAt = new DateTimeOffset(reader.GetDateTime(3));
        }

        if (string.Equals(
                beforeSha,
                package.ManifestSha256,
                StringComparison.Ordinal) &&
            beforeRollout == 100)
        {
            return;
        }

        await using (var update = new NpgsqlCommand(
                         """
                         UPDATE launcher.client_profile_channels
                         SET release_sha256 = $1,
                             rollout_percentage = 100,
                             revision = revision + 1,
                             updated_by = $2,
                             updated_at = $3
                         WHERE profile_id = $4 AND channel = 'test';
                         """,
                         connection,
                         transaction))
        {
            update.Parameters.AddWithValue(package.ManifestSha256!);
            update.Parameters.AddWithValue(package.CreatedBy);
            update.Parameters.AddWithValue(now);
            update.Parameters.AddWithValue(package.Plan.ProfileId);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        await WriteAuditAsync(
            connection,
            transaction,
            package.CreatedBy,
            "catalog.client_profile_channel.updated",
            "client_profile_channel",
            $"{package.Plan.ProfileId}:test",
            JsonSerializer.Serialize(new
            {
                ManifestSha256 = beforeSha,
                RolloutPercentage = beforeRollout,
                Revision = beforeRevision,
                UpdatedAt = beforeUpdatedAt
            }, JsonOptions),
            JsonSerializer.Serialize(new
            {
                ManifestSha256 = package.ManifestSha256,
                RolloutPercentage = 100,
                Revision = beforeRevision + 1,
                UpdatedAt = now
            }, JsonOptions),
            cancellationToken);
    }

    private static async Task<(string Code, string Message)?>
        SynchronizeCatalogAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            FinalizingPackage package,
            DateTimeOffset now,
            CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<ModLoaderKind>(
                package.Analysis.Metadata.Loader,
                ignoreCase: true,
                out var loader) ||
            !Enum.IsDefined(loader))
        {
            return (
                "CATALOG_LOADER_UNSUPPORTED",
                "识别出的加载器无法写入服务器目录；服务端保持停止，正式通道未变化。");
        }

        var maximumPlayers = Math.Clamp(
            package.Analysis.Metadata.MaximumPlayers ?? 30,
            1,
            10_000);
        const string readSql = """
            SELECT to_jsonb(server)::text, server.server_role
            FROM launcher.servers AS server
            WHERE server.id = $1
            FOR UPDATE;
            """;
        string? before = null;
        string? role = null;
        await using (var read = new NpgsqlCommand(readSql, connection, transaction))
        {
            read.Parameters.AddWithValue(package.Plan.TargetServerId);
            await using var reader = await read.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                before = reader.GetString(0);
                role = reader.GetString(1);
            }
        }

        if (string.Equals(role, "Infrastructure", StringComparison.Ordinal))
        {
            return (
                "CATALOG_TARGET_PROTECTED",
                "活动导入不能覆盖内部基础设施目录记录；服务端保持停止。");
        }

        var shortName = TakeTextElements(package.Plan.ServerDisplayName, 12);
        var iconGlyph = TakeTextElements(package.Plan.ServerDisplayName, 1);
        if (before is null)
        {
            await using var insert = new NpgsqlCommand(
                """
                INSERT INTO launcher.servers
                    (id, display_name, short_name, icon_glyph, status,
                     online_players, max_players, minecraft_version, loader,
                     minimum_tier, client_profile_id, velocity_target,
                     allow_protocol_translation, server_role,
                     monitoring_enabled, sort_order, is_visible, announcement,
                     opens_at, closes_at, created_at, updated_at)
                VALUES
                    ($1, $2, $3, $4, 'Closed', 0, $5, $6, $7, $8, $9,
                     $10, false, 'Player', true,
                     (SELECT LEAST(COALESCE(max(sort_order), 0) + 10, 100000)
                      FROM launcher.servers),
                     false, '', NULL, NULL, $11, $11);
                """,
                connection,
                transaction);
            AddCatalogParameters(
                insert,
                package,
                shortName,
                iconGlyph,
                maximumPlayers,
                loader,
                now);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        else
        {
            await using var update = new NpgsqlCommand(
                """
                UPDATE launcher.servers
                SET display_name = $2,
                    short_name = $3,
                    icon_glyph = $4,
                    status = 'Closed',
                    online_players = 0,
                    max_players = $5,
                    minecraft_version = $6,
                    loader = $7,
                    minimum_tier = $8,
                    client_profile_id = $9,
                    velocity_target = $10,
                    allow_protocol_translation = false,
                    server_role = 'Player',
                    monitoring_enabled = true,
                    is_visible = false,
                    announcement = '',
                    opens_at = NULL,
                    closes_at = NULL,
                    revision = revision + 1,
                    updated_at = $11
                WHERE id = $1;
                """,
                connection,
                transaction);
            AddCatalogParameters(
                update,
                package,
                shortName,
                iconGlyph,
                maximumPlayers,
                loader,
                now);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        string after;
        await using (var readAfter = new NpgsqlCommand(
                         "SELECT to_jsonb(server)::text FROM launcher.servers AS server WHERE id = $1;",
                         connection,
                         transaction))
        {
            readAfter.Parameters.AddWithValue(package.Plan.TargetServerId);
            after = (string)(await readAfter.ExecuteScalarAsync(cancellationToken)
                ?? throw new InvalidOperationException(
                    "The synchronized server catalog record is missing."));
        }

        await WriteAuditAsync(
            connection,
            transaction,
            package.CreatedBy,
            before is null ? "catalog.server.created" : "catalog.server.updated",
            "server",
            package.Plan.TargetServerId,
            before,
            after,
            cancellationToken);
        return null;
    }

    private static async Task EnableProfileForTestingAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        FinalizingPackage package,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        const string readSql = """
            SELECT is_active, revision, updated_at
            FROM launcher.client_profiles
            WHERE id = $1
            FOR UPDATE;
            """;
        bool wasActive;
        long beforeRevision;
        DateTimeOffset beforeUpdatedAt;
        await using (var read = new NpgsqlCommand(readSql, connection, transaction))
        {
            read.Parameters.AddWithValue(package.Plan.ProfileId);
            await using var reader = await read.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidDataException(
                    "The imported client profile is missing during finalization.");
            }

            wasActive = reader.GetBoolean(0);
            beforeRevision = reader.GetInt64(1);
            beforeUpdatedAt = new DateTimeOffset(reader.GetDateTime(2));
        }

        if (wasActive)
        {
            return;
        }

        await using (var update = new NpgsqlCommand(
                         """
                         UPDATE launcher.client_profiles
                         SET is_active = true,
                             revision = revision + 1,
                             updated_at = $2
                         WHERE id = $1 AND NOT is_active;
                         """,
                         connection,
                         transaction))
        {
            update.Parameters.AddWithValue(package.Plan.ProfileId);
            update.Parameters.AddWithValue(now);
            if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new InvalidOperationException(
                    "The imported client profile activation state changed unexpectedly.");
            }
        }

        await WriteAuditAsync(
            connection,
            transaction,
            package.CreatedBy,
            "catalog.client_profile.enabled_for_test",
            "client_profile",
            package.Plan.ProfileId,
            JsonSerializer.Serialize(new
            {
                IsActive = false,
                Revision = beforeRevision,
                UpdatedAt = beforeUpdatedAt
            }, JsonOptions),
            JsonSerializer.Serialize(new
            {
                IsActive = true,
                Revision = beforeRevision + 1,
                UpdatedAt = now,
                Channel = "Test"
            }, JsonOptions),
            cancellationToken);
    }

    private static void AddCatalogParameters(
        NpgsqlCommand command,
        FinalizingPackage package,
        string shortName,
        string iconGlyph,
        int maximumPlayers,
        ModLoaderKind loader,
        DateTimeOffset now)
    {
        command.Parameters.AddWithValue(package.Plan.TargetServerId);
        command.Parameters.AddWithValue(package.Plan.ServerDisplayName.Trim());
        command.Parameters.AddWithValue(shortName);
        command.Parameters.AddWithValue(iconGlyph);
        command.Parameters.AddWithValue(maximumPlayers);
        command.Parameters.AddWithValue(package.Analysis.Metadata.MinecraftVersion);
        command.Parameters.AddWithValue(loader.ToString());
        command.Parameters.AddWithValue(package.Plan.MinimumTier.ToString());
        command.Parameters.AddWithValue(package.Plan.ProfileId);
        command.Parameters.AddWithValue(PackageImportRules.ActivityVelocityTarget);
        command.Parameters.AddWithValue(now);
    }

    private static async Task FailPackageAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid importId,
        string code,
        string message,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var safeMessage = LimitMessage(message.Trim(), 2000);
        await using (var command = new NpgsqlCommand(
                         """
                         UPDATE launcher.package_imports
                         SET status = 'Failed',
                             error_code = $2,
                             error_message = $3,
                             completed_at = $4,
                             revision = revision + 1,
                             updated_at = $4
                         WHERE id = $1
                           AND status IN (
                               'QueuedForDeployment',
                               'DeployingServer',
                               'Finalizing'
                           );
                         """,
                         connection,
                         transaction))
        {
            command.Parameters.AddWithValue(importId);
            command.Parameters.AddWithValue(code);
            command.Parameters.AddWithValue(safeMessage);
            command.Parameters.AddWithValue(now);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await WriteEventAsync(
            connection,
            transaction,
            importId,
            PackageImportStatus.Failed,
            code,
            LimitMessage(safeMessage, 1000),
            now,
            cancellationToken);
    }

    private static async Task WriteEventAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid importId,
        PackageImportStatus status,
        string code,
        string message,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO launcher.package_import_events
                (import_id, status, code, message, created_at)
            VALUES ($1, $2, $3, $4, $5);
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(importId);
        command.Parameters.AddWithValue(status.ToString());
        command.Parameters.AddWithValue(code);
        command.Parameters.AddWithValue(LimitMessage(message, 1000));
        command.Parameters.AddWithValue(now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task WriteAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid actorUserId,
        string action,
        string targetType,
        string targetId,
        string? beforeJson,
        string? afterJson,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO launcher.audit_logs
                (actor_user_id, action, target_type, target_id,
                 before_data, after_data)
            VALUES ($1, $2, $3, $4, $5, $6);
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(actorUserId);
        command.Parameters.AddWithValue(action);
        command.Parameters.AddWithValue(targetType);
        command.Parameters.AddWithValue(targetId);
        AdminPostgresParameters.AddPositional(
            command.Parameters,
            NpgsqlDbType.Jsonb,
            beforeJson);
        AdminPostgresParameters.AddPositional(
            command.Parameters,
            NpgsqlDbType.Jsonb,
            afterJson);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

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
        ?? throw new InvalidDataException(
            $"Stored package import JSON is empty: {typeof(T).Name}");

    private static string LimitMessage(string value, int maximum) =>
        value.Length <= maximum ? value : value[..maximum];

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private sealed record QueuedPackage(
        Guid ImportId,
        PackageImportAnalysisRecord Analysis,
        PackageImportDeploymentPlanRecord Plan,
        string? ManifestSha256,
        Guid CreatedBy);

    private sealed record FinalizingPackage(
        Guid ImportId,
        PackageImportAnalysisRecord Analysis,
        PackageImportDeploymentPlanRecord Plan,
        string? ManifestSha256,
        Guid CreatedBy,
        Guid DeploymentOperationId);

    private sealed record DeploymentTarget(
        string ServerId,
        string AgentId,
        string? ConflictGroup,
        int Port,
        bool Online,
        DateTimeOffset LastSeenAt,
        bool PackageDeploymentEnabled,
        ServerQuickSettings? Settings,
        bool ServerFilesPresent);

    private sealed record DeploymentCommandPayload(
        string? ConsoleCommand = null,
        ServerQuickSettings? Settings = null,
        ServerPackageDeploymentRequest? PackageDeployment = null);
}

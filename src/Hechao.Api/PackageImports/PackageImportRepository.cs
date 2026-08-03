using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hechao.Api.Admin;
using Hechao.Contracts;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Hechao.Api.PackageImports;

public enum PackageImportMutationStatus
{
    Success,
    NotFound,
    RevisionConflict,
    InvalidStatus,
    NoWork
}

public sealed record PackageImportMutationResult(
    PackageImportMutationStatus Status,
    AdminPackageImportRecord? Import = null);

public sealed record PackagePublisherAgentState(
    bool Connected,
    DateTimeOffset? LastSeenAt);

public enum PackagePublisherClaimStatus
{
    Valid,
    NotFound,
    Conflict
}

public sealed record PackagePublisherClaimResult(
    PackagePublisherClaimStatus Status,
    AdminPackageImportRecord? Import = null);

public enum PackagePublisherMutationStatus
{
    Success,
    NotFound,
    ClaimConflict
}

public sealed record PackagePublisherMutationResult(
    PackagePublisherMutationStatus Status,
    AdminPackageImportRecord? Import = null);

public sealed class PackageImportRepository
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly NpgsqlDataSource dataSource;
    private readonly PackageImportOptions options;

    public PackageImportRepository(
        NpgsqlDataSource dataSource,
        IOptions<PackageImportOptions> options)
    {
        this.dataSource = dataSource;
        this.options = options.Value;
    }

    public async Task<AdminPackageImportRecord> CreateAsync(
        Guid importId,
        AdminPackageUploadCreateRequest request,
        Guid actorUserId,
        IPAddress? sourceIp,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var command = new NpgsqlCommand(
                         """
                         INSERT INTO launcher.package_imports
                             (id, file_name, expected_upload_bytes, created_by,
                              source_ip, created_at, updated_at)
                         VALUES ($1, $2, $3, $4, $5, $6, $6);
                         """,
                         connection,
                         transaction))
        {
            command.Parameters.AddWithValue(importId);
            command.Parameters.AddWithValue(request.FileName.Trim());
            command.Parameters.AddWithValue(request.TotalBytes);
            command.Parameters.AddWithValue(actorUserId);
            AdminPostgresParameters.AddPositional(
                command.Parameters,
                NpgsqlDbType.Inet,
                sourceIp);
            command.Parameters.AddWithValue(now);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await WriteEventAsync(
            connection,
            transaction,
            importId,
            PackageImportStatus.Uploading,
            "UPLOAD_CREATED",
            "上传任务已创建。",
            now,
            cancellationToken);
        await WriteAuditAsync(
            connection,
            transaction,
            actorUserId,
            sourceIp,
            "package_import.created",
            importId,
            before: null,
            after: request,
            now,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (await GetAsync(importId, cancellationToken))!;
    }

    public async Task<IReadOnlyList<AdminPackageImportRecord>> GetRecentAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT package.id, package.file_name, package.expected_upload_bytes,
                   package.uploaded_bytes, package.source_sha256, package.status,
                   package.analysis::text, package.plan::text,
                   package.manifest_sha256, package.deployment_operation_id,
                   package.error_code, package.error_message, package.created_by,
                   account.display_name, package.created_at, package.updated_at,
                   package.completed_at, package.revision
            FROM launcher.package_imports package
            LEFT JOIN launcher.users account
                ON account.id = package.created_by
            ORDER BY package.created_at DESC
            LIMIT $1;
            """;
        var results = new List<AdminPackageImportRecord>();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadImport(reader));
        }

        return results;
    }

    public async Task<AdminPackageImportRecord?> GetAsync(
        Guid importId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT package.id, package.file_name, package.expected_upload_bytes,
                   package.uploaded_bytes, package.source_sha256, package.status,
                   package.analysis::text, package.plan::text,
                   package.manifest_sha256, package.deployment_operation_id,
                   package.error_code, package.error_message, package.created_by,
                   account.display_name, package.created_at, package.updated_at,
                   package.completed_at, package.revision
            FROM launcher.package_imports package
            LEFT JOIN launcher.users account
                ON account.id = package.created_by
            WHERE package.id = $1;
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(importId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadImport(reader) : null;
    }

    public async Task<PackageImportMutationResult> UpdateUploadedBytesAsync(
        Guid importId,
        long uploadedBytes,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            UPDATE launcher.package_imports
            SET uploaded_bytes = $2,
                updated_at = $3
            WHERE id = $1
              AND status = 'Uploading'
              AND uploaded_bytes <= $2
              AND $2 <= expected_upload_bytes;
            """,
            connection);
        command.Parameters.AddWithValue(importId);
        command.Parameters.AddWithValue(uploadedBytes);
        command.Parameters.AddWithValue(now);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1
            ? new PackageImportMutationResult(PackageImportMutationStatus.Success)
            : new PackageImportMutationResult(PackageImportMutationStatus.InvalidStatus);
    }

    public async Task<PackageImportMutationResult> MarkUploadedAsync(
        Guid importId,
        string sourceSha256,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            UPDATE launcher.package_imports
            SET status = 'Uploaded',
                source_sha256 = $2,
                uploaded_bytes = expected_upload_bytes,
                error_code = NULL,
                error_message = NULL,
                revision = revision + 1,
                updated_at = $3
            WHERE id = $1
              AND status = 'Uploading'
              AND uploaded_bytes = expected_upload_bytes;
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(importId);
        command.Parameters.AddWithValue(sourceSha256);
        command.Parameters.AddWithValue(now);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            return new PackageImportMutationResult(PackageImportMutationStatus.InvalidStatus);
        }

        await WriteEventAsync(
            connection,
            transaction,
            importId,
            PackageImportStatus.Uploaded,
            "UPLOAD_COMPLETED",
            "上传完成，等待安全识别。",
            now,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new PackageImportMutationResult(
            PackageImportMutationStatus.Success,
            await GetAsync(importId, cancellationToken));
    }

    public async Task<Guid?> ClaimAnalysisAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        const string sql = """
            WITH due AS (
                SELECT id
                FROM launcher.package_imports
                WHERE (
                    status = 'Uploaded'
                    OR (
                        status = 'Analyzing'
                        AND analysis_started_at <= $1 - interval '30 minutes'
                    )
                )
                  AND analysis_attempt_count < 5
                ORDER BY updated_at, id
                LIMIT 1
                FOR UPDATE SKIP LOCKED
            )
            UPDATE launcher.package_imports package
            SET status = 'Analyzing',
                analysis_started_at = $1,
                analysis_attempt_count = analysis_attempt_count + 1,
                error_code = NULL,
                error_message = NULL,
                revision = revision + 1,
                updated_at = $1
            FROM due
            WHERE package.id = due.id
            RETURNING package.id;
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(now);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return value is Guid importId ? importId : null;
    }

    public async Task CompleteAnalysisAsync(
        Guid importId,
        PackageImportAnalysisRecord analysis,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var command = new NpgsqlCommand(
                         """
                         UPDATE launcher.package_imports
                         SET status = 'AwaitingReview',
                             analysis = $2::jsonb,
                             analysis_started_at = NULL,
                             error_code = NULL,
                             error_message = NULL,
                             revision = revision + 1,
                             updated_at = $3
                         WHERE id = $1 AND status = 'Analyzing';
                         """,
                         connection,
                         transaction))
        {
            command.Parameters.AddWithValue(importId);
            command.Parameters.AddWithValue(JsonSerializer.Serialize(analysis, JsonOptions));
            command.Parameters.AddWithValue(now);
            if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new InvalidOperationException(
                    "The package import analysis lease is no longer active.");
            }
        }

        await WriteEventAsync(
            connection,
            transaction,
            importId,
            PackageImportStatus.AwaitingReview,
            analysis.HasBlockingIssues ? "ANALYSIS_BLOCKED" : "ANALYSIS_READY",
            analysis.HasBlockingIssues
                ? "识别完成，但存在必须处理的阻断项。"
                : "客户端和服务端识别完成，等待管理员确认。",
            now,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task FailAsync(
        Guid importId,
        string code,
        string message,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var safeMessage = message.Trim();
        if (safeMessage.Length > 2000)
        {
            safeMessage = safeMessage[..2000];
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var command = new NpgsqlCommand(
                         """
                         UPDATE launcher.package_imports
                         SET status = 'Failed',
                             error_code = $2,
                             error_message = $3,
                             analysis_started_at = NULL,
                             publisher_lease_expires_at = NULL,
                             completed_at = $4,
                             revision = revision + 1,
                             updated_at = $4
                         WHERE id = $1
                           AND status NOT IN ('Completed', 'Failed', 'Cancelled');
                         """,
                         connection,
                         transaction))
        {
            command.Parameters.AddWithValue(importId);
            command.Parameters.AddWithValue(code);
            command.Parameters.AddWithValue(safeMessage);
            command.Parameters.AddWithValue(now);
            if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
            {
                return;
            }
        }

        await WriteEventAsync(
            connection,
            transaction,
            importId,
            PackageImportStatus.Failed,
            code,
            safeMessage,
            now,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<PackageImportMutationResult> ConfirmAsync(
        Guid importId,
        AdminPackageImportConfirmRequest request,
        Guid actorUserId,
        IPAddress? sourceIp,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var plan = new PackageImportDeploymentPlanRecord(
            request.ProfileId,
            request.ProfileDisplayName.Trim(),
            request.Version,
            request.TargetServerId,
            request.PreserveWorldData,
            request.SyncServerCatalog,
            request.ServerDisplayName.Trim(),
            request.MinimumTier,
            request.MaximumMemoryMiB);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            UPDATE launcher.package_imports
            SET status = 'QueuedForPublishing',
                plan = $2::jsonb,
                error_code = NULL,
                error_message = NULL,
                revision = revision + 1,
                updated_at = $3
            WHERE id = $1
              AND status = 'AwaitingReview'
              AND revision = $4;
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(importId);
        command.Parameters.AddWithValue(JsonSerializer.Serialize(plan, JsonOptions));
        command.Parameters.AddWithValue(now);
        command.Parameters.AddWithValue(request.ExpectedRevision);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            return new PackageImportMutationResult(
                PackageImportMutationStatus.RevisionConflict);
        }

        await WriteEventAsync(
            connection,
            transaction,
            importId,
            PackageImportStatus.QueuedForPublishing,
            "PUBLISH_CONFIRMED",
            "管理员已确认部署预览，等待客户端发布代理。",
            now,
            cancellationToken);
        await WriteAuditAsync(
            connection,
            transaction,
            actorUserId,
            sourceIp,
            "package_import.confirmed",
            importId,
            before: null,
            after: plan,
            now,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new PackageImportMutationResult(
            PackageImportMutationStatus.Success,
            await GetAsync(importId, cancellationToken));
    }

    public async Task<PackageImportMutationResult> CancelAsync(
        Guid importId,
        AdminPackageImportCancelRequest request,
        Guid actorUserId,
        IPAddress? sourceIp,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var reason = request.Reason.Trim();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            UPDATE launcher.package_imports
            SET status = 'Cancelled',
                error_code = 'CANCELLED_BY_ADMIN',
                error_message = $2,
                completed_at = $3,
                revision = revision + 1,
                updated_at = $3
            WHERE id = $1
              AND revision = $4
              AND status IN (
                  'Uploading', 'Uploaded', 'Analyzing', 'AwaitingReview',
                  'QueuedForPublishing'
              );
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(importId);
        command.Parameters.AddWithValue(reason);
        command.Parameters.AddWithValue(now);
        command.Parameters.AddWithValue(request.ExpectedRevision);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            return new PackageImportMutationResult(
                PackageImportMutationStatus.RevisionConflict);
        }

        await WriteEventAsync(
            connection,
            transaction,
            importId,
            PackageImportStatus.Cancelled,
            "CANCELLED_BY_ADMIN",
            reason,
            now,
            cancellationToken);
        await WriteAuditAsync(
            connection,
            transaction,
            actorUserId,
            sourceIp,
            "package_import.cancelled",
            importId,
            before: null,
            after: new { reason },
            now,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new PackageImportMutationResult(
            PackageImportMutationStatus.Success,
            await GetAsync(importId, cancellationToken));
    }

    public async Task RecordPublisherHeartbeatAsync(
        PackagePublisherHeartbeatRequest request,
        DateTimeOffset receivedAt,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var command = new NpgsqlCommand(
                         """
                         INSERT INTO launcher.package_publisher_agents
                             (agent_id, agent_version, captured_at, last_seen_at)
                         VALUES ($1, $2, $3, $4)
                         ON CONFLICT (agent_id) DO UPDATE
                         SET agent_version = EXCLUDED.agent_version,
                             captured_at = EXCLUDED.captured_at,
                             last_seen_at = EXCLUDED.last_seen_at;
                         """,
                         connection,
                         transaction))
        {
            command.Parameters.AddWithValue(request.AgentId);
            command.Parameters.AddWithValue(request.AgentVersion.Trim());
            command.Parameters.AddWithValue(request.CapturedAt);
            command.Parameters.AddWithValue(receivedAt);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var renew = new NpgsqlCommand(
                         """
                         UPDATE launcher.package_imports
                         SET publisher_lease_expires_at = CASE
                                 WHEN id = $3 THEN $2
                                 ELSE LEAST(publisher_lease_expires_at, $4)
                             END
                         WHERE status = 'PublishingClient'
                           AND publisher_claimed_by = $1;
                         """,
                         connection,
                         transaction))
        {
            renew.Parameters.AddWithValue(request.AgentId);
            renew.Parameters.AddWithValue(
                receivedAt.AddMinutes(options.PublisherLeaseMinutes));
            AdminPostgresParameters.AddPositional(
                renew.Parameters,
                NpgsqlDbType.Uuid,
                request.ActiveImportId);
            renew.Parameters.AddWithValue(
                receivedAt.AddSeconds(
                    options.PublisherAgentFreshnessSeconds));
            await renew.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<PackagePublisherAgentState> GetPublisherAgentStateAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT max(last_seen_at) FROM launcher.package_publisher_agents;",
            connection);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        var lastSeenAt = value is DateTime date
            ? new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Utc))
            : value is DateTimeOffset offset ? offset : (DateTimeOffset?)null;
        return new PackagePublisherAgentState(
            lastSeenAt is not null &&
            now - lastSeenAt <= TimeSpan.FromSeconds(
                options.PublisherAgentFreshnessSeconds),
            lastSeenAt);
    }

    public async Task<PackagePublisherClaimResponse> ClaimPublisherJobAsync(
        string agentId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var leaseExpiresAt = now.AddMinutes(options.PublisherLeaseMinutes);
        const string sql = """
            WITH due AS (
                SELECT id
                FROM launcher.package_imports
                WHERE (
                    status = 'QueuedForPublishing'
                    OR (
                        status = 'PublishingClient'
                        AND publisher_lease_expires_at <= $2
                    )
                )
                  AND publisher_attempt_count < 5
                ORDER BY updated_at, id
                LIMIT 1
                FOR UPDATE SKIP LOCKED
            )
            UPDATE launcher.package_imports package
            SET status = 'PublishingClient',
                publisher_claimed_by = $1,
                publisher_claimed_at = $2,
                publisher_lease_expires_at = $3,
                publisher_attempt_count = publisher_attempt_count + 1,
                error_code = NULL,
                error_message = NULL,
                revision = revision + 1,
                updated_at = $2
            FROM due
            WHERE package.id = due.id
            RETURNING package.id, package.publisher_attempt_count,
                      package.analysis::text, package.plan::text;
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(agentId);
        command.Parameters.AddWithValue(now);
        command.Parameters.AddWithValue(leaseExpiresAt);
        PackagePublisherJobDelivery? delivery = null;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                var analysis = Deserialize<PackageImportAnalysisRecord>(reader.GetString(2));
                var plan = Deserialize<PackageImportDeploymentPlanRecord>(reader.GetString(3));
                if (analysis.Client is null)
                {
                    throw new InvalidDataException(
                        "The queued package import has no client archive.");
                }

                delivery = new PackagePublisherJobDelivery(
                    reader.GetGuid(0),
                    reader.GetInt32(1),
                    plan.ProfileId,
                    plan.Version,
                    analysis.Metadata.MinecraftVersion,
                    analysis.Metadata.JavaMajorVersion,
                    analysis.Metadata.Loader,
                    analysis.Metadata.LoaderVersion,
                    analysis.Client.ArchiveBytes,
                    analysis.Client.Sha256);
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return new PackagePublisherClaimResponse(delivery, now);
    }

    public async Task<bool> CanOpenPublisherArchiveAsync(
        Guid importId,
        string agentId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT EXISTS (
                SELECT 1
                FROM launcher.package_imports
                WHERE id = $1
                  AND status = 'PublishingClient'
                  AND publisher_claimed_by = $2
                  AND publisher_lease_expires_at >= $3
            );
            """,
            connection);
        command.Parameters.AddWithValue(importId);
        command.Parameters.AddWithValue(agentId);
        command.Parameters.AddWithValue(now);
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    public async Task<PackagePublisherClaimResult> GetPublisherClaimAsync(
        Guid importId,
        string agentId,
        int attemptCount,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT status, publisher_claimed_by, publisher_attempt_count,
                   publisher_lease_expires_at
            FROM launcher.package_imports
            WHERE id = $1;
            """,
            connection);
        command.Parameters.AddWithValue(importId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return new PackagePublisherClaimResult(
                PackagePublisherClaimStatus.NotFound);
        }

        var valid = string.Equals(
                        reader.GetString(0),
                        PackageImportStatus.PublishingClient.ToString(),
                        StringComparison.Ordinal) &&
                    !reader.IsDBNull(1) &&
                    string.Equals(reader.GetString(1), agentId, StringComparison.Ordinal) &&
                    reader.GetInt32(2) == attemptCount &&
                    !reader.IsDBNull(3) &&
                    new DateTimeOffset(reader.GetDateTime(3)) >= now;
        await reader.CloseAsync();
        return valid
            ? new PackagePublisherClaimResult(
                PackagePublisherClaimStatus.Valid,
                await GetAsync(importId, cancellationToken))
            : new PackagePublisherClaimResult(PackagePublisherClaimStatus.Conflict);
    }

    public async Task<PackagePublisherMutationResult> CompletePublisherSuccessAsync(
        Guid importId,
        string agentId,
        int attemptCount,
        string manifestSha256,
        int uploadedObjects,
        int existingObjects,
        long uploadedBytes,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            UPDATE launcher.package_imports
            SET status = 'QueuedForDeployment',
                manifest_sha256 = $5,
                publisher_uploaded_objects = $6,
                publisher_existing_objects = $7,
                publisher_uploaded_bytes = $8,
                publisher_claimed_by = NULL,
                publisher_claimed_at = NULL,
                publisher_lease_expires_at = NULL,
                error_code = NULL,
                error_message = NULL,
                revision = revision + 1,
                updated_at = $4
            WHERE id = $1
              AND status = 'PublishingClient'
              AND publisher_claimed_by = $2
              AND publisher_attempt_count = $3
              AND publisher_lease_expires_at >= $4;
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(importId);
        command.Parameters.AddWithValue(agentId);
        command.Parameters.AddWithValue(attemptCount);
        command.Parameters.AddWithValue(now);
        command.Parameters.AddWithValue(manifestSha256);
        command.Parameters.AddWithValue(uploadedObjects);
        command.Parameters.AddWithValue(existingObjects);
        command.Parameters.AddWithValue(uploadedBytes);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            return new PackagePublisherMutationResult(
                await ExistsAsync(connection, transaction, importId, cancellationToken)
                    ? PackagePublisherMutationStatus.ClaimConflict
                    : PackagePublisherMutationStatus.NotFound);
        }

        await WriteEventAsync(
            connection,
            transaction,
            importId,
            PackageImportStatus.QueuedForDeployment,
            "CLIENT_PUBLISHED",
            "客户端对象和签名清单已校验，等待服务端原子部署。",
            now,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new PackagePublisherMutationResult(
            PackagePublisherMutationStatus.Success,
            await GetAsync(importId, cancellationToken));
    }

    public async Task<PackagePublisherMutationResult> CompletePublisherFailureAsync(
        Guid importId,
        string agentId,
        int attemptCount,
        string code,
        string message,
        bool retryable,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var safeMessage = message.Trim();
        if (safeMessage.Length > 2000)
        {
            safeMessage = safeMessage[..2000];
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        string? status = null;
        await using (var command = new NpgsqlCommand(
                         """
                         UPDATE launcher.package_imports
                         SET status = CASE
                                 WHEN $6 AND publisher_attempt_count < 5
                                     THEN 'QueuedForPublishing'
                                 ELSE 'Failed'
                             END,
                             publisher_claimed_by = NULL,
                             publisher_claimed_at = NULL,
                             publisher_lease_expires_at = NULL,
                             error_code = $5,
                             error_message = $7,
                             completed_at = CASE
                                 WHEN $6 AND publisher_attempt_count < 5
                                     THEN NULL
                                 ELSE $4
                             END,
                             revision = revision + 1,
                             updated_at = $4
                         WHERE id = $1
                           AND status = 'PublishingClient'
                           AND publisher_claimed_by = $2
                           AND publisher_attempt_count = $3
                           AND publisher_lease_expires_at >= $4
                         RETURNING status;
                         """,
                         connection,
                         transaction))
        {
            command.Parameters.AddWithValue(importId);
            command.Parameters.AddWithValue(agentId);
            command.Parameters.AddWithValue(attemptCount);
            command.Parameters.AddWithValue(now);
            command.Parameters.AddWithValue(code);
            command.Parameters.AddWithValue(retryable);
            command.Parameters.AddWithValue(safeMessage);
            status = await command.ExecuteScalarAsync(cancellationToken) as string;
        }

        if (status is null)
        {
            return new PackagePublisherMutationResult(
                await ExistsAsync(connection, transaction, importId, cancellationToken)
                    ? PackagePublisherMutationStatus.ClaimConflict
                    : PackagePublisherMutationStatus.NotFound);
        }

        var nextStatus = Enum.Parse<PackageImportStatus>(status);
        await WriteEventAsync(
            connection,
            transaction,
            importId,
            nextStatus,
            nextStatus == PackageImportStatus.Failed
                ? code
                : "PUBLISH_RETRY_QUEUED",
            nextStatus == PackageImportStatus.Failed
                ? safeMessage
                : "客户端发布未完成，任务已保留并等待下一次代理重试。",
            now,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new PackagePublisherMutationResult(
            PackagePublisherMutationStatus.Success,
            await GetAsync(importId, cancellationToken));
    }

    private static async Task<bool> ExistsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid importId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM launcher.package_imports WHERE id = $1);",
            connection,
            transaction);
        command.Parameters.AddWithValue(importId);
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    private static AdminPackageImportRecord ReadImport(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetInt64(2),
            reader.GetInt64(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            Enum.Parse<PackageImportStatus>(reader.GetString(5), ignoreCase: false),
            reader.IsDBNull(6)
                ? null
                : Deserialize<PackageImportAnalysisRecord>(reader.GetString(6)),
            reader.IsDBNull(7)
                ? null
                : Deserialize<PackageImportDeploymentPlanRecord>(reader.GetString(7)),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.IsDBNull(9) ? null : reader.GetGuid(9),
            reader.IsDBNull(10) ? null : reader.GetString(10),
            reader.IsDBNull(11) ? null : reader.GetString(11),
            reader.GetGuid(12),
            reader.IsDBNull(13) ? null : reader.GetString(13),
            new DateTimeOffset(reader.GetDateTime(14)),
            new DateTimeOffset(reader.GetDateTime(15)),
            reader.IsDBNull(16) ? null : new DateTimeOffset(reader.GetDateTime(16)),
            reader.GetInt64(17));

    private static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, JsonOptions)
        ?? throw new InvalidDataException(
            $"Stored package import JSON is empty: {typeof(T).Name}");

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
        command.Parameters.AddWithValue(message);
        command.Parameters.AddWithValue(now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task WriteAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid actorUserId,
        IPAddress? sourceIp,
        string action,
        Guid importId,
        object? before,
        object? after,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO launcher.audit_logs
                (actor_user_id, action, target_type, target_id, source_ip,
                 before_data, after_data, created_at)
            VALUES ($1, $2, 'package_import', $3, $4, $5, $6, $7);
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(actorUserId);
        command.Parameters.AddWithValue(action);
        command.Parameters.AddWithValue(importId.ToString("D"));
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
        command.Parameters.AddWithValue(now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}

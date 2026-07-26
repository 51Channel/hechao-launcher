using System.Net;
using System.Text.Json;
using Hechao.Contracts;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Hechao.Api.Diagnostics;

public enum DiagnosticUploadCreateStatus
{
    Success,
    DailyCountExceeded,
    DailyBytesExceeded,
    ActiveLimitExceeded
}

public sealed record DiagnosticUploadCreateResult(
    DiagnosticUploadCreateStatus Status,
    DiagnosticUploadAuthorizationResponse? Authorization);

public sealed record DiagnosticUploadTicket(
    Guid UploadId,
    Guid UserId,
    string ProfileId,
    string LauncherVersion,
    long ExpectedBytes,
    string ExpectedSha256);

public sealed record DiagnosticUploadDownload(
    Guid UploadId,
    Guid UserId,
    string ProfileId,
    long Size,
    string Sha256,
    DateTimeOffset UploadedAt,
    DateTimeOffset ExpiresAt);

public sealed class DiagnosticUploadRepository(
    NpgsqlDataSource dataSource,
    IOptions<DiagnosticUploadOptions> options,
    TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions AuditJsonOptions =
        new(JsonSerializerDefaults.Web);
    private readonly DiagnosticUploadOptions _options = options.Value;

    public async Task<DiagnosticUploadCreateResult> CreateAsync(
        Guid userId,
        DiagnosticUploadCreateRequest request,
        IPAddress? sourceIp,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var token = DiagnosticUploadRules.CreateUploadToken();
        var tokenSha256 = DiagnosticUploadRules.HashUploadToken(token);
        var uploadId = Guid.NewGuid();
        var tokenExpiresAt = now.AddMinutes(_options.UploadTokenMinutes);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var userLock = new NpgsqlCommand(
                         "SELECT pg_advisory_xact_lock(hashtextextended($1, 0));",
                         connection,
                         transaction))
        {
            userLock.Parameters.AddWithValue(userId.ToString("D"));
            await userLock.ExecuteScalarAsync(cancellationToken);
        }

        const string quotaSql = """
            SELECT
                (SELECT count(*)
                 FROM launcher.diagnostic_uploads
                 WHERE user_id = $1 AND created_at >= $2),
                (SELECT COALESCE(sum(expected_bytes), 0)
                 FROM launcher.diagnostic_uploads
                 WHERE user_id = $1 AND created_at >= $2),
                (SELECT count(*)
                 FROM launcher.diagnostic_uploads
                 WHERE user_id = $1 AND status = 'uploaded' AND expires_at > $3);
            """;
        long dailyCount;
        long dailyBytes;
        long activeCount;
        await using (var quota = new NpgsqlCommand(quotaSql, connection, transaction))
        {
            quota.Parameters.AddWithValue(userId);
            quota.Parameters.AddWithValue(now.AddDays(-1));
            quota.Parameters.AddWithValue(now);
            await using var reader = await quota.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            dailyCount = reader.GetInt64(0);
            dailyBytes = reader.GetInt64(1);
            activeCount = reader.GetInt64(2);
        }

        var quotaStatus = dailyCount >= _options.MaximumUploadsPerDay
            ? DiagnosticUploadCreateStatus.DailyCountExceeded
            : dailyBytes + request.Size > _options.MaximumBytesPerDay
                ? DiagnosticUploadCreateStatus.DailyBytesExceeded
                : activeCount >= _options.MaximumActiveUploads
                    ? DiagnosticUploadCreateStatus.ActiveLimitExceeded
                    : DiagnosticUploadCreateStatus.Success;
        if (quotaStatus != DiagnosticUploadCreateStatus.Success)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new DiagnosticUploadCreateResult(quotaStatus, null);
        }

        const string insertSql = """
            INSERT INTO launcher.diagnostic_uploads
                (id, user_id, profile_id, launcher_version, expected_bytes,
                 expected_sha256, upload_token_sha256, upload_token_expires_at, status, created_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, 'pending', $9);
            """;
        await using (var insert = new NpgsqlCommand(insertSql, connection, transaction))
        {
            insert.Parameters.AddWithValue(uploadId);
            insert.Parameters.AddWithValue(userId);
            insert.Parameters.AddWithValue(request.ProfileId);
            insert.Parameters.AddWithValue(request.LauncherVersion);
            insert.Parameters.AddWithValue(request.Size);
            insert.Parameters.AddWithValue(request.Sha256.ToLowerInvariant());
            insert.Parameters.AddWithValue(tokenSha256);
            insert.Parameters.AddWithValue(tokenExpiresAt);
            insert.Parameters.AddWithValue(now);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await WriteAuditAsync(
            connection,
            transaction,
            userId,
            sourceIp,
            "diagnostic.upload.authorized",
            uploadId,
            new
            {
                request.ProfileId,
                request.LauncherVersion,
                request.Size,
                Sha256 = request.Sha256.ToLowerInvariant(),
                tokenExpiresAt
            },
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new DiagnosticUploadCreateResult(
            DiagnosticUploadCreateStatus.Success,
            new DiagnosticUploadAuthorizationResponse(
                uploadId,
                token,
                tokenExpiresAt,
                _options.MaximumBytes));
    }

    public async Task<DiagnosticUploadTicket?> BeginUploadAsync(
        Guid uploadId,
        string uploadTokenSha256,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        const string sql = """
            UPDATE launcher.diagnostic_uploads
            SET status = 'uploading', upload_token_sha256 = NULL
            WHERE id = $1
              AND status = 'pending'
              AND upload_token_sha256 = $2
              AND upload_token_expires_at > $3
            RETURNING id, user_id, profile_id, launcher_version,
                      expected_bytes, expected_sha256;
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(uploadId);
        command.Parameters.AddWithValue(uploadTokenSha256);
        command.Parameters.AddWithValue(now);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new DiagnosticUploadTicket(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt64(4),
                reader.GetString(5))
            : null;
    }

    public async Task<DiagnosticUploadReceipt?> CompleteAsync(
        DiagnosticUploadTicket ticket,
        long actualBytes,
        string actualSha256,
        IPAddress? sourceIp,
        CancellationToken cancellationToken)
    {
        var uploadedAt = timeProvider.GetUtcNow();
        var expiresAt = uploadedAt.AddDays(_options.RetentionDays);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        const string sql = """
            UPDATE launcher.diagnostic_uploads
            SET status = 'uploaded', actual_bytes = $2, actual_sha256 = $3,
                uploaded_at = $4, expires_at = $5
            WHERE id = $1 AND status = 'uploading'
            RETURNING profile_id;
            """;
        string? profileId;
        await using (var update = new NpgsqlCommand(sql, connection, transaction))
        {
            update.Parameters.AddWithValue(ticket.UploadId);
            update.Parameters.AddWithValue(actualBytes);
            update.Parameters.AddWithValue(actualSha256);
            update.Parameters.AddWithValue(uploadedAt);
            update.Parameters.AddWithValue(expiresAt);
            profileId = await update.ExecuteScalarAsync(cancellationToken) as string;
        }

        if (profileId is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        await WriteAuditAsync(
            connection,
            transaction,
            ticket.UserId,
            sourceIp,
            "diagnostic.upload.completed",
            ticket.UploadId,
            new
            {
                ticket.ProfileId,
                ticket.LauncherVersion,
                Size = actualBytes,
                Sha256 = actualSha256,
                uploadedAt,
                expiresAt
            },
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new DiagnosticUploadReceipt(
            ticket.UploadId,
            profileId,
            actualBytes,
            actualSha256,
            uploadedAt,
            expiresAt);
    }

    public async Task MarkFailedAsync(
        DiagnosticUploadTicket ticket,
        string reason,
        IPAddress? sourceIp,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var update = new NpgsqlCommand(
                         """
                         UPDATE launcher.diagnostic_uploads
                         SET status = 'failed', upload_token_sha256 = NULL
                         WHERE id = $1 AND status = 'uploading';
                         """,
                         connection,
                         transaction))
        {
            update.Parameters.AddWithValue(ticket.UploadId);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        await WriteAuditAsync(
            connection,
            transaction,
            ticket.UserId,
            sourceIp,
            "diagnostic.upload.failed",
            ticket.UploadId,
            new { reason },
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AdminDiagnosticUploadRecord>> GetAdminListAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT upload.id, upload.user_id, account.display_name, upload.profile_id,
                   upload.launcher_version, upload.actual_bytes, upload.actual_sha256,
                   upload.uploaded_at, upload.expires_at
            FROM launcher.diagnostic_uploads upload
            INNER JOIN launcher.users account ON account.id = upload.user_id
            WHERE upload.status = 'uploaded' AND upload.expires_at > now()
            ORDER BY upload.uploaded_at DESC
            LIMIT $1;
            """;
        var records = new List<AdminDiagnosticUploadRecord>();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(new AdminDiagnosticUploadRecord(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetInt64(5),
                reader.GetString(6),
                new DateTimeOffset(reader.GetDateTime(7)),
                new DateTimeOffset(reader.GetDateTime(8))));
        }

        return records;
    }

    public async Task<DiagnosticUploadDownload?> GetForAdminDownloadAsync(
        Guid uploadId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, user_id, profile_id, actual_bytes, actual_sha256,
                   uploaded_at, expires_at
            FROM launcher.diagnostic_uploads
            WHERE id = $1 AND status = 'uploaded' AND expires_at > now();
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(uploadId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new DiagnosticUploadDownload(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetInt64(3),
                reader.GetString(4),
                new DateTimeOffset(reader.GetDateTime(5)),
                new DateTimeOffset(reader.GetDateTime(6)))
            : null;
    }

    public async Task RecordAdminDownloadAsync(
        Guid uploadId,
        Guid actorUserId,
        IPAddress? sourceIp,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await WriteAuditAsync(
            connection,
            transaction,
            actorUserId,
            sourceIp,
            "diagnostic.admin.downloaded",
            uploadId,
            new { downloadedAt = timeProvider.GetUtcNow() },
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> ExpireAsync(
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        const string sql = """
            WITH expired AS (
                UPDATE launcher.diagnostic_uploads
                SET status = 'expired', upload_token_sha256 = NULL
                WHERE
                    (status = 'uploaded' AND expires_at <= $1)
                    OR
                    (status IN ('pending', 'uploading') AND upload_token_expires_at <= $1)
                RETURNING id
            )
            INSERT INTO launcher.audit_logs
                (action, target_type, target_id, after_data)
            SELECT 'diagnostic.upload.expired', 'diagnostic_upload', id::text,
                   jsonb_build_object('expiredAt', $1)
            FROM expired
            RETURNING target_id;
            """;
        var expired = new List<Guid>();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(now);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (Guid.TryParse(reader.GetString(0), out var uploadId))
            {
                expired.Add(uploadId);
            }
        }

        return expired;
    }

    private static async Task WriteAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid actorUserId,
        IPAddress? sourceIp,
        string action,
        Guid uploadId,
        object afterData,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO launcher.audit_logs
                (actor_user_id, action, target_type, target_id, source_ip, after_data)
            VALUES ($1, $2, 'diagnostic_upload', $3, $4, $5);
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(actorUserId);
        command.Parameters.AddWithValue(action);
        command.Parameters.AddWithValue(uploadId.ToString("D"));
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Inet,
            Value = sourceIp ?? (object)DBNull.Value
        });
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Jsonb,
            Value = JsonSerializer.Serialize(afterData, AuditJsonOptions)
        });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

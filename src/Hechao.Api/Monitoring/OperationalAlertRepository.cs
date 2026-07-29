using Hechao.Contracts;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Hechao.Api.Monitoring;

public sealed class OperationalAlertRepository(
    NpgsqlDataSource dataSource,
    IOptions<OperationalAlertOptions> options,
    IOptions<ServerHeartbeatOptions> heartbeatOptions,
    TimeProvider timeProvider)
{
    private static readonly long TenGibibytes = 10L * 1024 * 1024 * 1024;
    private static readonly long FiveGibibytes = 5L * 1024 * 1024 * 1024;

    private readonly OperationalAlertOptions _options = options.Value;
    private readonly ServerHeartbeatOptions _heartbeatOptions =
        heartbeatOptions.Value;

    public async Task UpsertRequestMetricsAsync(
        IReadOnlyList<ApiRequestMetricSnapshot> snapshots,
        CancellationToken cancellationToken)
    {
        if (snapshots.Count == 0)
        {
            return;
        }

        const string sql = """
            INSERT INTO launcher.api_request_minute_metrics
                (bucket_start, category, request_count, client_error_count,
                 server_error_count, total_duration_ms, maximum_duration_ms,
                 updated_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7, now())
            ON CONFLICT (bucket_start, category) DO UPDATE
            SET request_count =
                    launcher.api_request_minute_metrics.request_count +
                    EXCLUDED.request_count,
                client_error_count =
                    launcher.api_request_minute_metrics.client_error_count +
                    EXCLUDED.client_error_count,
                server_error_count =
                    launcher.api_request_minute_metrics.server_error_count +
                    EXCLUDED.server_error_count,
                total_duration_ms =
                    launcher.api_request_minute_metrics.total_duration_ms +
                    EXCLUDED.total_duration_ms,
                maximum_duration_ms = greatest(
                    launcher.api_request_minute_metrics.maximum_duration_ms,
                    EXCLUDED.maximum_duration_ms),
                updated_at = now();
            """;

        await using var connection =
            await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction =
            await connection.BeginTransactionAsync(cancellationToken);
        foreach (var snapshot in snapshots)
        {
            await using var command =
                new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue(snapshot.BucketStart);
            command.Parameters.AddWithValue(snapshot.Category.ToString());
            command.Parameters.AddWithValue(snapshot.RequestCount);
            command.Parameters.AddWithValue(snapshot.ClientErrorCount);
            command.Parameters.AddWithValue(snapshot.ServerErrorCount);
            command.Parameters.AddWithValue(
                snapshot.TotalDurationMilliseconds);
            command.Parameters.AddWithValue(
                snapshot.MaximumDurationMilliseconds);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task EvaluateAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return;
        }

        var observedAt = timeProvider.GetUtcNow();
        var from = observedAt.AddMinutes(-_options.EvaluationWindowMinutes);
        await using var connection =
            await dataSource.OpenConnectionAsync(cancellationToken);

        var candidates = new List<OperationalAlertCandidate>();
        var metrics = await ReadRequestMetricsAsync(
            connection,
            from,
            cancellationToken);
        var all = metrics.GetValueOrDefault(ApiRequestMetricCategory.All);
        var login = metrics.GetValueOrDefault(ApiRequestMetricCategory.Login);
        var objectDownload =
            metrics.GetValueOrDefault(ApiRequestMetricCategory.ObjectDownload);
        Add(candidates, OperationalAlertRules.EvaluateApiErrors(
            all.RequestCount,
            all.ServerErrorCount,
            observedAt));
        Add(candidates, OperationalAlertRules.EvaluateApiLatency(
            all.RequestCount,
            all.TotalDurationMilliseconds,
            all.MaximumDurationMilliseconds,
            observedAt));
        Add(candidates, OperationalAlertRules.EvaluateLoginFailures(
            login.ClientErrorCount + login.ServerErrorCount,
            observedAt));
        Add(candidates, OperationalAlertRules.EvaluateObjectEndpointFailures(
            objectDownload.ServerErrorCount,
            observedAt));

        var (downloadAttempts, downloadFailures) =
            await ReadDownloadTelemetryAsync(
                connection,
                from,
                observedAt,
                cancellationToken);
        Add(candidates, OperationalAlertRules.EvaluateDownloadFailures(
            downloadAttempts,
            downloadFailures,
            observedAt));
        candidates.AddRange(await ReadServerCandidatesAsync(
            connection,
            observedAt,
            cancellationToken));

        await using var transaction =
            await connection.BeginTransactionAsync(cancellationToken);
        foreach (var candidate in candidates)
        {
            await UpsertActiveAsync(
                connection,
                transaction,
                candidate,
                "ApiEvaluator",
                cancellationToken);
        }

        await ResolveMissingEvaluatorAlertsAsync(
            connection,
            transaction,
            candidates.Select(candidate => candidate.Fingerprint).ToArray(),
            observedAt,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task ApplyExternalEventAsync(
        InternalOperationalAlertEventRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection =
            await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction =
            await connection.BeginTransactionAsync(cancellationToken);
        if (request.Active)
        {
            await UpsertActiveAsync(
                connection,
                transaction,
                new OperationalAlertCandidate(
                    request.Fingerprint.Trim(),
                    request.Code.Trim(),
                    request.Source,
                    request.Severity,
                    request.Title.Trim(),
                    request.Summary.Trim(),
                    request.ObservedAt),
                "PlatformMonitor",
                cancellationToken);
        }
        else
        {
            await ResolveAsync(
                connection,
                transaction,
                request.Fingerprint.Trim(),
                request.ObservedAt,
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<AdminOperationalAlertSummary> GetAdminSummaryAsync(
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT fingerprint, code, source, severity, status, title, summary,
                   opened_at, last_seen_at, last_transition_at, resolved_at,
                   observation_count, acknowledged_at, acknowledged_by,
                   revision
            FROM launcher.operational_alerts
            ORDER BY
                CASE status WHEN 'Active' THEN 0 ELSE 1 END,
                CASE severity
                    WHEN 'Critical' THEN 0
                    WHEN 'Warning' THEN 1
                    ELSE 2
                END,
                last_transition_at DESC,
                fingerprint
            LIMIT 200;
            """;

        var alerts = new List<OperationalAlertRecord>();
        await using var connection =
            await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            alerts.Add(ReadAlert(reader));
        }

        var active = alerts
            .Where(alert => alert.Status == OperationalAlertStatus.Active)
            .ToArray();
        return new AdminOperationalAlertSummary(
            timeProvider.GetUtcNow(),
            active.Length,
            active.Count(alert =>
                alert.Severity == OperationalAlertSeverity.Critical),
            active.Count(alert =>
                alert.Severity == OperationalAlertSeverity.Warning),
            active.Count(alert => alert.AcknowledgedAt is null),
            alerts);
    }

    public async Task<InternalOperationalAlertSnapshot> GetActiveSnapshotAsync(
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT fingerprint, code, source, severity, status, title, summary,
                   opened_at, last_seen_at, last_transition_at, resolved_at,
                   observation_count, acknowledged_at, acknowledged_by,
                   revision
            FROM launcher.operational_alerts
            WHERE status = 'Active'
            ORDER BY
                CASE severity
                    WHEN 'Critical' THEN 0
                    WHEN 'Warning' THEN 1
                    ELSE 2
                END,
                last_transition_at DESC,
                fingerprint;
            """;

        var alerts = new List<OperationalAlertRecord>();
        await using var connection =
            await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            alerts.Add(ReadAlert(reader));
        }

        return new InternalOperationalAlertSnapshot(
            timeProvider.GetUtcNow(),
            alerts);
    }

    public async Task<bool> AcknowledgeAsync(
        string fingerprint,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        const string updateSql = """
            UPDATE launcher.operational_alerts
            SET acknowledged_at = $2,
                acknowledged_by = $3,
                updated_at = $2,
                revision = revision + 1
            WHERE fingerprint = $1
              AND status = 'Active'
            RETURNING code, severity;
            """;
        const string auditSql = """
            INSERT INTO launcher.audit_logs
                (actor_user_id, action, target_type, target_id, after_data)
            VALUES (
                $1,
                'operational_alert.acknowledged',
                'operational_alert',
                $2,
                jsonb_build_object('code', $3::text, 'severity', $4::text)
            );
            """;

        var now = timeProvider.GetUtcNow();
        await using var connection =
            await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction =
            await connection.BeginTransactionAsync(cancellationToken);
        string? code = null;
        string? severity = null;
        await using (var update =
                     new NpgsqlCommand(updateSql, connection, transaction))
        {
            update.Parameters.AddWithValue(fingerprint);
            update.Parameters.AddWithValue(now);
            update.Parameters.AddWithValue(actorUserId);
            await using var reader =
                await update.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                code = reader.GetString(0);
                severity = reader.GetString(1);
            }
        }

        if (code is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        await using (var audit =
                     new NpgsqlCommand(auditSql, connection, transaction))
        {
            audit.Parameters.AddWithValue(actorUserId);
            audit.Parameters.AddWithValue(fingerprint);
            audit.Parameters.AddWithValue(code);
            audit.Parameters.AddWithValue(severity!);
            await audit.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<int> DeleteRequestMetricsBeforeAsync(
        DateTimeOffset cutoff,
        CancellationToken cancellationToken)
    {
        await using var connection =
            await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            DELETE FROM launcher.api_request_minute_metrics
            WHERE bucket_start < $1;
            """,
            connection);
        command.Parameters.AddWithValue(cutoff);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<Dictionary<ApiRequestMetricCategory, RequestMetric>>
        ReadRequestMetricsAsync(
            NpgsqlConnection connection,
            DateTimeOffset from,
            CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT category,
                   sum(request_count)::bigint,
                   sum(client_error_count)::bigint,
                   sum(server_error_count)::bigint,
                   sum(total_duration_ms)::bigint,
                   max(maximum_duration_ms)::integer
            FROM launcher.api_request_minute_metrics
            WHERE bucket_start >= $1
            GROUP BY category;
            """;
        var result =
            new Dictionary<ApiRequestMetricCategory, RequestMetric>();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(from);
        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (Enum.TryParse<ApiRequestMetricCategory>(
                    reader.GetString(0),
                    ignoreCase: false,
                    out var category))
            {
                result[category] = new RequestMetric(
                    reader.GetInt64(1),
                    reader.GetInt64(2),
                    reader.GetInt64(3),
                    reader.GetInt64(4),
                    reader.GetInt32(5));
            }
        }

        return result;
    }

    private static async Task<(long Attempts, long Failures)>
        ReadDownloadTelemetryAsync(
            NpgsqlConnection connection,
            DateTimeOffset from,
            DateTimeOffset to,
            CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT count(*)::bigint,
                   count(*) FILTER (WHERE outcome = 'Failure')::bigint
            FROM launcher.client_telemetry_events
            WHERE occurred_at >= $1
              AND occurred_at < $2
              AND event_type IN ('Install', 'Repair');
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(from);
        command.Parameters.AddWithValue(to);
        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return (reader.GetInt64(0), reader.GetInt64(1));
    }

    private async Task<IReadOnlyList<OperationalAlertCandidate>>
        ReadServerCandidatesAsync(
            NpgsqlConnection connection,
            DateTimeOffset observedAt,
            CancellationToken cancellationToken)
    {
        const string sql = """
            WITH active_targets AS (
                SELECT velocity_target,
                       string_agg(display_name, ', ' ORDER BY sort_order, id)
                           AS display_names,
                       bool_or(loader = 'Paper') AS expects_tick_metrics
                FROM launcher.servers
                WHERE monitoring_enabled
                  AND status = 'Online'
                  AND (opens_at IS NULL OR opens_at <= $1)
                  AND (closes_at IS NULL OR closes_at > $1)
                GROUP BY velocity_target
            )
            SELECT target.velocity_target,
                   target.display_names,
                   target.expects_tick_metrics,
                   heartbeat.velocity_target IS NOT NULL,
                   heartbeat.is_online,
                   heartbeat.process_working_set_bytes,
                   heartbeat.disk_free_bytes,
                   heartbeat.disk_total_bytes,
                   heartbeat.tps_1m,
                   heartbeat.mspt_average,
                   heartbeat.probe_issues,
                   heartbeat.received_at
            FROM active_targets target
            LEFT JOIN launcher.velocity_target_heartbeats heartbeat
              ON heartbeat.velocity_target = target.velocity_target
            ORDER BY target.velocity_target;
            """;

        var result = new List<OperationalAlertCandidate>();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(observedAt);
        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var target = reader.GetString(0);
            var names = reader.GetString(1);
            var expectsTickMetrics = reader.GetBoolean(2);
            var hasHeartbeat = reader.GetBoolean(3);
            var online = hasHeartbeat && reader.GetBoolean(4);
            var workingSet = ReadInt64(reader, 5);
            var diskFree = ReadInt64(reader, 6);
            var diskTotal = ReadInt64(reader, 7);
            var tps = ReadDouble(reader, 8);
            var mspt = ReadDouble(reader, 9);
            var issues = reader.IsDBNull(10)
                ? []
                : reader.GetFieldValue<string[]>(10);
            var receivedAt = ReadTimestamp(reader, 11);
            var fresh = receivedAt is not null &&
                        receivedAt >= observedAt.AddSeconds(
                            -_heartbeatOptions.FreshnessSeconds);

            if (!hasHeartbeat || !fresh || !online)
            {
                var reason = !hasHeartbeat
                    ? "从未收到心跳"
                    : !fresh
                        ? $"心跳已过期，最后收到于 {receivedAt:O}"
                        : "状态探针报告目标离线";
                result.Add(new OperationalAlertCandidate(
                    $"server:{target}:heartbeat",
                    "Server.Heartbeat",
                    OperationalAlertSource.Server,
                    OperationalAlertSeverity.Critical,
                    $"{names} 服务状态异常",
                    $"{target}: {reason}。",
                    observedAt));
                continue;
            }

            if (issues.Contains(
                    nameof(ServerMetricIssueCode.ProcessNotRunning),
                    StringComparer.Ordinal))
            {
                result.Add(new OperationalAlertCandidate(
                    $"server:{target}:process",
                    "Server.Process",
                    OperationalAlertSource.Server,
                    OperationalAlertSeverity.Critical,
                    $"{names} Java 进程未运行",
                    $"{target}: 状态端口可访问，但只读进程探针没有找到目标 Java 进程。",
                    observedAt));
            }
            else if (workingSet is null &&
                     issues.Any(issue => issue is
                         nameof(ServerMetricIssueCode.ProcessAccessDenied) or
                         nameof(ServerMetricIssueCode.ProcessProbeFailed)))
            {
                result.Add(new OperationalAlertCandidate(
                    $"server:{target}:process",
                    "Server.Process",
                    OperationalAlertSource.Server,
                    OperationalAlertSeverity.Warning,
                    $"{names} 进程指标不可用",
                    $"{target}: 只读进程探针无法读取 Java 指标。",
                    observedAt));
            }

            if (diskFree is not null && diskTotal is > 0)
            {
                var percent = diskFree.Value * 100d / diskTotal.Value;
                if (percent < 15 || diskFree < TenGibibytes)
                {
                    result.Add(new OperationalAlertCandidate(
                        $"server:{target}:disk",
                        "Server.DiskFree",
                        OperationalAlertSource.Server,
                        percent < 5 || diskFree < FiveGibibytes
                            ? OperationalAlertSeverity.Critical
                            : OperationalAlertSeverity.Warning,
                        $"{names} 磁盘余量不足",
                        $"{target}: 剩余 {FormatGibibytes(diskFree.Value)} GiB（{percent:0.##}%）。",
                        observedAt));
                }
            }

            if (tps is not null || mspt is not null)
            {
                var degraded = tps is < 18.5 || mspt is > 50;
                if (degraded)
                {
                    result.Add(new OperationalAlertCandidate(
                        $"server:{target}:tick",
                        "Server.TickPerformance",
                        OperationalAlertSource.Server,
                        tps is < 17 || mspt is > 65
                            ? OperationalAlertSeverity.Critical
                            : OperationalAlertSeverity.Warning,
                        $"{names} Tick 性能下降",
                        $"{target}: TPS {tps?.ToString("0.##") ?? "无数据"}，MSPT {mspt?.ToString("0.##") ?? "无数据"}。",
                        observedAt));
                }
            }
            else if (expectsTickMetrics &&
                     issues.Any(issue => issue is
                         nameof(ServerMetricIssueCode.MetricsFileMissing) or
                         nameof(ServerMetricIssueCode.MetricsFileStale) or
                         nameof(ServerMetricIssueCode.MetricsFileInvalid)))
            {
                result.Add(new OperationalAlertCandidate(
                    $"server:{target}:tick-metrics",
                    "Server.TickMetrics",
                    OperationalAlertSource.Server,
                    OperationalAlertSeverity.Warning,
                    $"{names} Tick 指标不可用",
                    $"{target}: Paper/Purpur 指标快照缺失、过期或无效。",
                    observedAt));
            }
        }

        return result;
    }

    private static async Task UpsertActiveAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        OperationalAlertCandidate candidate,
        string producer,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO launcher.operational_alerts
                (fingerprint, code, source, severity, status, producer,
                 title, summary, opened_at, last_seen_at, last_transition_at,
                 observation_count, updated_at)
            VALUES
                ($1, $2, $3, $4, 'Active', $5, $6, $7, $8, $8, $8, 1, $8)
            ON CONFLICT (fingerprint) DO UPDATE
            SET code = EXCLUDED.code,
                source = EXCLUDED.source,
                severity = EXCLUDED.severity,
                status = 'Active',
                producer = EXCLUDED.producer,
                title = EXCLUDED.title,
                summary = EXCLUDED.summary,
                opened_at = CASE
                    WHEN launcher.operational_alerts.status = 'Resolved'
                        THEN EXCLUDED.opened_at
                    ELSE launcher.operational_alerts.opened_at
                END,
                last_seen_at = greatest(
                    launcher.operational_alerts.last_seen_at,
                    EXCLUDED.last_seen_at),
                last_transition_at = CASE
                    WHEN launcher.operational_alerts.status = 'Resolved'
                      OR launcher.operational_alerts.severity <> EXCLUDED.severity
                        THEN EXCLUDED.last_transition_at
                    ELSE launcher.operational_alerts.last_transition_at
                END,
                resolved_at = NULL,
                observation_count =
                    launcher.operational_alerts.observation_count + 1,
                acknowledged_at = CASE
                    WHEN launcher.operational_alerts.status = 'Resolved'
                      OR launcher.operational_alerts.severity <> EXCLUDED.severity
                        THEN NULL
                    ELSE launcher.operational_alerts.acknowledged_at
                END,
                acknowledged_by = CASE
                    WHEN launcher.operational_alerts.status = 'Resolved'
                      OR launcher.operational_alerts.severity <> EXCLUDED.severity
                        THEN NULL
                    ELSE launcher.operational_alerts.acknowledged_by
                END,
                updated_at = EXCLUDED.updated_at,
                revision = launcher.operational_alerts.revision + 1;
            """;
        await using var command =
            new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(candidate.Fingerprint);
        command.Parameters.AddWithValue(candidate.Code);
        command.Parameters.AddWithValue(candidate.Source.ToString());
        command.Parameters.AddWithValue(candidate.Severity.ToString());
        command.Parameters.AddWithValue(producer);
        command.Parameters.AddWithValue(candidate.Title);
        command.Parameters.AddWithValue(candidate.Summary);
        command.Parameters.AddWithValue(candidate.ObservedAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ResolveMissingEvaluatorAlertsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string[] activeFingerprints,
        DateTimeOffset resolvedAt,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE launcher.operational_alerts
            SET status = 'Resolved',
                resolved_at = $2,
                last_transition_at = $2,
                updated_at = $2,
                revision = revision + 1
            WHERE producer = 'ApiEvaluator'
              AND status = 'Active'
              AND NOT (fingerprint = ANY($1));
            """;
        await using var command =
            new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Text,
            Value = activeFingerprints
        });
        command.Parameters.AddWithValue(resolvedAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ResolveAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string fingerprint,
        DateTimeOffset resolvedAt,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE launcher.operational_alerts
            SET status = 'Resolved',
                resolved_at = $2,
                last_seen_at = greatest(last_seen_at, $2),
                last_transition_at = $2,
                updated_at = $2,
                revision = revision + 1
            WHERE fingerprint = $1
              AND status = 'Active';
            """;
        await using var command =
            new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(fingerprint);
        command.Parameters.AddWithValue(resolvedAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static OperationalAlertRecord ReadAlert(NpgsqlDataReader reader) =>
        new(
            reader.GetString(0),
            reader.GetString(1),
            Enum.Parse<OperationalAlertSource>(reader.GetString(2)),
            Enum.Parse<OperationalAlertSeverity>(reader.GetString(3)),
            Enum.Parse<OperationalAlertStatus>(reader.GetString(4)),
            reader.GetString(5),
            reader.GetString(6),
            ReadTimestamp(reader, 7)!.Value,
            ReadTimestamp(reader, 8)!.Value,
            ReadTimestamp(reader, 9)!.Value,
            ReadTimestamp(reader, 10),
            reader.GetInt64(11),
            ReadTimestamp(reader, 12),
            reader.IsDBNull(13) ? null : reader.GetGuid(13),
            reader.GetInt64(14));

    private static long? ReadInt64(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);

    private static double? ReadDouble(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetDouble(ordinal);

    private static DateTimeOffset? ReadTimestamp(
        NpgsqlDataReader reader,
        int ordinal) =>
        reader.IsDBNull(ordinal)
            ? null
            : new DateTimeOffset(reader.GetDateTime(ordinal));

    private static string FormatGibibytes(long value) =>
        (value / 1024d / 1024 / 1024).ToString("0.##");

    private static void Add(
        ICollection<OperationalAlertCandidate> candidates,
        OperationalAlertCandidate? candidate)
    {
        if (candidate is not null)
        {
            candidates.Add(candidate);
        }
    }

    private readonly record struct RequestMetric(
        long RequestCount,
        long ClientErrorCount,
        long ServerErrorCount,
        long TotalDurationMilliseconds,
        int MaximumDurationMilliseconds);
}

using Hechao.Contracts;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Hechao.Api.Monitoring;

public sealed class ServerRuntimeStatusRepository(
    NpgsqlDataSource dataSource,
    IOptions<ServerHeartbeatOptions> options,
    TimeProvider timeProvider)
{
    private readonly ServerHeartbeatOptions _options = options.Value;

    public async Task<AdminServerRuntimeSummary> GetSummaryAsync(
        CancellationToken cancellationToken)
    {
        var generatedAt = timeProvider.GetUtcNow();
        var targets = await ReadTargetsAsync(
            generatedAt,
            cancellationToken);
        var issues = await ReadIssueSummaryAsync(
            generatedAt.AddHours(-24),
            cancellationToken);
        return new AdminServerRuntimeSummary(
            generatedAt,
            _options.FreshnessSeconds,
            targets,
            issues);
    }

    public async Task<int> DeleteSamplesBeforeAsync(
        DateTimeOffset cutoff,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(
            cancellationToken);
        await using var command = new NpgsqlCommand(
            "DELETE FROM launcher.server_runtime_samples WHERE received_at < $1;",
            connection);
        command.Parameters.AddWithValue(cutoff);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<AdminServerRuntimeRecord>> ReadTargetsAsync(
        DateTimeOffset generatedAt,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT server.id,
                   server.display_name,
                   server.velocity_target,
                   server.is_visible,
                   heartbeat.velocity_target,
                   heartbeat.collector_instance,
                   heartbeat.is_online,
                   heartbeat.online_players,
                   heartbeat.max_players,
                   heartbeat.software_version,
                   heartbeat.protocol_version,
                   heartbeat.process_working_set_bytes,
                   heartbeat.process_private_bytes,
                   heartbeat.process_cpu_percent,
                   heartbeat.process_started_at,
                   heartbeat.disk_free_bytes,
                   heartbeat.disk_total_bytes,
                   heartbeat.tps_1m,
                   heartbeat.tps_5m,
                   heartbeat.tps_15m,
                   heartbeat.mspt_average,
                   heartbeat.gc_collection_time_ms,
                   heartbeat.metrics_captured_at,
                   heartbeat.probe_issues,
                   heartbeat.captured_at,
                   heartbeat.received_at
            FROM launcher.servers server
            LEFT JOIN launcher.velocity_target_heartbeats heartbeat
              ON heartbeat.velocity_target = server.velocity_target
            ORDER BY server.velocity_target, server.sort_order, server.id;
            """;

        var accumulators = new Dictionary<string, TargetAccumulator>(
            StringComparer.Ordinal);
        await using var connection = await dataSource.OpenConnectionAsync(
            cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var target = reader.GetString(2);
            if (!accumulators.TryGetValue(target, out var accumulator))
            {
                var hasHeartbeat = !reader.IsDBNull(4);
                var receivedAt = ReadTimestamp(reader, 25);
                accumulator = new TargetAccumulator(
                    target,
                    hasHeartbeat,
                    hasHeartbeat &&
                    receivedAt >= generatedAt.AddSeconds(-_options.FreshnessSeconds),
                    hasHeartbeat && reader.GetBoolean(6),
                    hasHeartbeat ? reader.GetInt32(7) : 0,
                    hasHeartbeat ? reader.GetInt32(8) : 0,
                    ReadString(reader, 9),
                    ReadInt32(reader, 10),
                    ReadInt64(reader, 11),
                    ReadInt64(reader, 12),
                    ReadDouble(reader, 13),
                    ReadTimestamp(reader, 14),
                    ReadInt64(reader, 15),
                    ReadInt64(reader, 16),
                    ReadDouble(reader, 17),
                    ReadDouble(reader, 18),
                    ReadDouble(reader, 19),
                    ReadDouble(reader, 20),
                    ReadInt64(reader, 21),
                    ReadTimestamp(reader, 22),
                    reader.IsDBNull(23)
                        ? []
                        : reader.GetFieldValue<string[]>(23)
                            .Select(ParseIssue)
                            .Where(issue => issue is not null)
                            .Select(issue => issue!.Value)
                            .ToArray(),
                    ReadString(reader, 5),
                    ReadTimestamp(reader, 24),
                    receivedAt);
                accumulators.Add(target, accumulator);
            }

            accumulator.Servers.Add(new AdminServerRuntimeBinding(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetBoolean(3)));
        }

        return accumulators.Values
            .Select(accumulator => accumulator.ToRecord())
            .ToArray();
    }

    private async Task<IReadOnlyList<AdminServerRuntimeIssueSummary>>
        ReadIssueSummaryAsync(
            DateTimeOffset from,
            CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT issue,
                   count(*)::bigint,
                   count(DISTINCT sample.velocity_target)::integer
            FROM launcher.server_runtime_samples sample
            CROSS JOIN LATERAL unnest(sample.probe_issues) AS issue
            WHERE sample.received_at >= $1
            GROUP BY issue
            ORDER BY count(*) DESC, issue;
            """;

        var result = new List<AdminServerRuntimeIssueSummary>();
        await using var connection = await dataSource.OpenConnectionAsync(
            cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(from);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var issue = ParseIssue(reader.GetString(0));
            if (issue is null)
            {
                continue;
            }

            result.Add(new AdminServerRuntimeIssueSummary(
                issue.Value,
                reader.GetInt64(1),
                reader.GetInt32(2)));
        }

        return result;
    }

    private static ServerMetricIssueCode? ParseIssue(string value) =>
        Enum.TryParse<ServerMetricIssueCode>(
            value,
            ignoreCase: false,
            out var issue)
            ? issue
            : null;

    private static string? ReadString(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static int? ReadInt32(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);

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

    private sealed class TargetAccumulator(
        string velocityTarget,
        bool hasHeartbeat,
        bool isFresh,
        bool online,
        int onlinePlayers,
        int maxPlayers,
        string? softwareVersion,
        int? protocolVersion,
        long? processWorkingSetBytes,
        long? processPrivateBytes,
        double? processCpuPercent,
        DateTimeOffset? processStartedAt,
        long? diskFreeBytes,
        long? diskTotalBytes,
        double? tps1m,
        double? tps5m,
        double? tps15m,
        double? msptAverage,
        long? gcCollectionTimeMilliseconds,
        DateTimeOffset? metricsCapturedAt,
        IReadOnlyList<ServerMetricIssueCode> issues,
        string? collectorInstance,
        DateTimeOffset? capturedAt,
        DateTimeOffset? receivedAt)
    {
        public List<AdminServerRuntimeBinding> Servers { get; } = [];

        public AdminServerRuntimeRecord ToRecord() =>
            new(
                velocityTarget,
                Servers,
                hasHeartbeat,
                isFresh,
                online,
                onlinePlayers,
                maxPlayers,
                softwareVersion,
                protocolVersion,
                processWorkingSetBytes,
                processPrivateBytes,
                processCpuPercent,
                processStartedAt,
                diskFreeBytes,
                diskTotalBytes,
                tps1m,
                tps5m,
                tps15m,
                msptAverage,
                gcCollectionTimeMilliseconds,
                metricsCapturedAt,
                issues,
                collectorInstance,
                capturedAt,
                receivedAt);
    }
}

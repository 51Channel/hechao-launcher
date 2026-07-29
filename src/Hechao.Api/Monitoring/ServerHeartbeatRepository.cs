using Hechao.Contracts;
using Npgsql;
using NpgsqlTypes;

namespace Hechao.Api.Monitoring;

public sealed class ServerHeartbeatRepository(NpgsqlDataSource dataSource)
{
    public async Task<ServerHeartbeatBatchResponse> ImportAsync(
        ServerHeartbeatBatchRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var requestedTargets = request.Servers
            .Select(server => server.VelocityTarget)
            .ToArray();
        var knownTargets = await ReadKnownTargetsAsync(
            connection,
            transaction,
            requestedTargets,
            cancellationToken);
        var unknownTargets = requestedTargets
            .Except(knownTargets, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (unknownTargets.Length > 0)
        {
            throw new UnknownVelocityTargetsException(unknownTargets);
        }

        const string upsertSql = """
            INSERT INTO launcher.velocity_target_heartbeats (
                velocity_target,
                collector_instance,
                is_online,
                online_players,
                max_players,
                software_version,
                protocol_version,
                process_working_set_bytes,
                process_private_bytes,
                process_cpu_percent,
                process_started_at,
                disk_free_bytes,
                disk_total_bytes,
                tps_1m,
                tps_5m,
                tps_15m,
                mspt_average,
                gc_collection_time_ms,
                metrics_captured_at,
                probe_issues,
                captured_at,
                received_at)
            VALUES (
                $1, $2, $3, $4, $5, NULLIF($6, ''), NULLIF($7, -1),
                $8, $9, $10, $11, $12, $13, $14, $15, $16, $17, $18,
                $19, $20, $21, now())
            ON CONFLICT (velocity_target) DO UPDATE SET
                collector_instance = EXCLUDED.collector_instance,
                is_online = EXCLUDED.is_online,
                online_players = EXCLUDED.online_players,
                max_players = EXCLUDED.max_players,
                software_version = EXCLUDED.software_version,
                protocol_version = EXCLUDED.protocol_version,
                process_working_set_bytes = EXCLUDED.process_working_set_bytes,
                process_private_bytes = EXCLUDED.process_private_bytes,
                process_cpu_percent = EXCLUDED.process_cpu_percent,
                process_started_at = EXCLUDED.process_started_at,
                disk_free_bytes = EXCLUDED.disk_free_bytes,
                disk_total_bytes = EXCLUDED.disk_total_bytes,
                tps_1m = EXCLUDED.tps_1m,
                tps_5m = EXCLUDED.tps_5m,
                tps_15m = EXCLUDED.tps_15m,
                mspt_average = EXCLUDED.mspt_average,
                gc_collection_time_ms = EXCLUDED.gc_collection_time_ms,
                metrics_captured_at = EXCLUDED.metrics_captured_at,
                probe_issues = EXCLUDED.probe_issues,
                captured_at = EXCLUDED.captured_at,
                received_at = now()
            WHERE EXCLUDED.captured_at >= launcher.velocity_target_heartbeats.captured_at;
            """;

        const string sampleSql = """
            INSERT INTO launcher.server_runtime_samples (
                velocity_target,
                collector_instance,
                is_online,
                online_players,
                max_players,
                process_working_set_bytes,
                process_private_bytes,
                process_cpu_percent,
                process_started_at,
                disk_free_bytes,
                disk_total_bytes,
                tps_1m,
                tps_5m,
                tps_15m,
                mspt_average,
                gc_collection_time_ms,
                metrics_captured_at,
                probe_issues,
                captured_at,
                received_at)
            VALUES (
                $1, $2, $3, $4, $5, $8, $9, $10, $11, $12, $13, $14,
                $15, $16, $17, $18, $19, $20, $21, now())
            ON CONFLICT (velocity_target, captured_at) DO NOTHING;
            """;

        foreach (var heartbeat in request.Servers)
        {
            await using var command = new NpgsqlCommand(upsertSql, connection, transaction);
            AddHeartbeatParameters(command, request, heartbeat);
            await command.ExecuteNonQueryAsync(cancellationToken);

            await using var sampleCommand = new NpgsqlCommand(
                sampleSql,
                connection,
                transaction);
            AddHeartbeatParameters(sampleCommand, request, heartbeat);
            await sampleCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new ServerHeartbeatBatchResponse(request.Servers.Count, DateTimeOffset.UtcNow);
    }

    private static async Task<HashSet<string>> ReadKnownTargetsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string[] requestedTargets,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT DISTINCT velocity_target
            FROM launcher.servers
            WHERE monitoring_enabled
              AND velocity_target = ANY($1);
            """;

        var result = new HashSet<string>(StringComparer.Ordinal);
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(requestedTargets);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(reader.GetString(0));
        }

        return result;
    }

    private static void AddHeartbeatParameters(
        NpgsqlCommand command,
        ServerHeartbeatBatchRequest request,
        VelocityTargetHeartbeat heartbeat)
    {
        command.Parameters.AddWithValue(heartbeat.VelocityTarget);
        command.Parameters.AddWithValue(request.CollectorInstance);
        command.Parameters.AddWithValue(heartbeat.Online);
        command.Parameters.AddWithValue(heartbeat.OnlinePlayers);
        command.Parameters.AddWithValue(heartbeat.MaxPlayers);
        command.Parameters.AddWithValue(heartbeat.SoftwareVersion ?? string.Empty);
        command.Parameters.AddWithValue(heartbeat.ProtocolVersion ?? -1);
        AddNullable(command, NpgsqlDbType.Bigint, heartbeat.ProcessWorkingSetBytes);
        AddNullable(command, NpgsqlDbType.Bigint, heartbeat.ProcessPrivateBytes);
        AddNullable(command, NpgsqlDbType.Double, heartbeat.ProcessCpuPercent);
        AddNullableTimestamp(command, heartbeat.ProcessStartedAt);
        AddNullable(command, NpgsqlDbType.Bigint, heartbeat.DiskFreeBytes);
        AddNullable(command, NpgsqlDbType.Bigint, heartbeat.DiskTotalBytes);
        AddNullable(command, NpgsqlDbType.Double, heartbeat.Tps1m);
        AddNullable(command, NpgsqlDbType.Double, heartbeat.Tps5m);
        AddNullable(command, NpgsqlDbType.Double, heartbeat.Tps15m);
        AddNullable(command, NpgsqlDbType.Double, heartbeat.MsptAverage);
        AddNullable(
            command,
            NpgsqlDbType.Bigint,
            heartbeat.GcCollectionTimeMilliseconds);
        AddNullableTimestamp(command, heartbeat.MetricsCapturedAt);
        command.Parameters.AddWithValue(
            NpgsqlDbType.Array | NpgsqlDbType.Text,
            (heartbeat.Issues ?? [])
                .Select(issue => issue.ToString())
                .ToArray());
        command.Parameters.AddWithValue(request.CapturedAt.UtcDateTime);
    }

    private static void AddNullable<T>(
        NpgsqlCommand command,
        NpgsqlDbType type,
        T? value)
        where T : struct =>
        command.Parameters.AddWithValue(
            type,
            value is null ? DBNull.Value : value.Value);

    private static void AddNullableTimestamp(
        NpgsqlCommand command,
        DateTimeOffset? value) =>
        command.Parameters.AddWithValue(
            NpgsqlDbType.TimestampTz,
            value is null ? DBNull.Value : value.Value.UtcDateTime);
}

public sealed class UnknownVelocityTargetsException(IReadOnlyList<string> targets)
    : Exception($"Unknown Velocity targets: {string.Join(", ", targets)}")
{
    public IReadOnlyList<string> Targets { get; } = targets;
}

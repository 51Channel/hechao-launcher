using Hechao.Contracts;
using Npgsql;

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
                captured_at,
                received_at)
            VALUES ($1, $2, $3, $4, $5, NULLIF($6, ''), NULLIF($7, -1), $8, now())
            ON CONFLICT (velocity_target) DO UPDATE SET
                collector_instance = EXCLUDED.collector_instance,
                is_online = EXCLUDED.is_online,
                online_players = EXCLUDED.online_players,
                max_players = EXCLUDED.max_players,
                software_version = EXCLUDED.software_version,
                protocol_version = EXCLUDED.protocol_version,
                captured_at = EXCLUDED.captured_at,
                received_at = now()
            WHERE EXCLUDED.captured_at >= launcher.velocity_target_heartbeats.captured_at;
            """;

        foreach (var heartbeat in request.Servers)
        {
            await using var command = new NpgsqlCommand(upsertSql, connection, transaction);
            command.Parameters.AddWithValue(heartbeat.VelocityTarget);
            command.Parameters.AddWithValue(request.CollectorInstance);
            command.Parameters.AddWithValue(heartbeat.Online);
            command.Parameters.AddWithValue(heartbeat.OnlinePlayers);
            command.Parameters.AddWithValue(heartbeat.MaxPlayers);
            command.Parameters.AddWithValue(heartbeat.SoftwareVersion ?? string.Empty);
            command.Parameters.AddWithValue(heartbeat.ProtocolVersion ?? -1);
            command.Parameters.AddWithValue(request.CapturedAt.UtcDateTime);
            await command.ExecuteNonQueryAsync(cancellationToken);
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
            WHERE velocity_target = ANY($1);
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
}

public sealed class UnknownVelocityTargetsException(IReadOnlyList<string> targets)
    : Exception($"Unknown Velocity targets: {string.Join(", ", targets)}")
{
    public IReadOnlyList<string> Targets { get; } = targets;
}

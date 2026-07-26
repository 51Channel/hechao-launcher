using Hechao.Contracts;
using Npgsql;
using NpgsqlTypes;

namespace Hechao.Api.Telemetry;

public sealed class LauncherTelemetryRepository(
    NpgsqlDataSource dataSource,
    TimeProvider timeProvider)
{
    public async Task<LauncherTelemetryBatchResponse> ImportAsync(
        Guid userId,
        LauncherTelemetryBatchRequest request,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO launcher.client_telemetry_events
                (user_id, event_id, event_type, outcome, failure_code,
                 launcher_version, profile_id, profile_version, duration_ms,
                 bytes, occurred_at, received_at)
            VALUES
                ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12)
            ON CONFLICT (user_id, event_id) DO NOTHING;
            """;

        var accepted = 0;
        var receivedAt = timeProvider.GetUtcNow();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        foreach (var item in request.Events)
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue(userId);
            command.Parameters.AddWithValue(item.EventId);
            command.Parameters.AddWithValue(item.Type.ToString());
            command.Parameters.AddWithValue(item.Outcome.ToString());
            command.Parameters.AddWithValue(item.FailureCode.ToString());
            command.Parameters.AddWithValue(item.LauncherVersion.Trim());
            command.Parameters.AddWithValue(
                NpgsqlDbType.Text,
                item.ProfileId is null ? DBNull.Value : item.ProfileId.Trim());
            command.Parameters.AddWithValue(
                NpgsqlDbType.Text,
                item.ProfileVersion is null
                    ? DBNull.Value
                    : item.ProfileVersion.Trim());
            command.Parameters.AddWithValue(
                NpgsqlDbType.Integer,
                item.DurationMilliseconds is null
                    ? DBNull.Value
                    : item.DurationMilliseconds.Value);
            command.Parameters.AddWithValue(
                NpgsqlDbType.Bigint,
                item.Bytes is null ? DBNull.Value : item.Bytes.Value);
            command.Parameters.AddWithValue(item.OccurredAt);
            command.Parameters.AddWithValue(receivedAt);
            accepted += await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new LauncherTelemetryBatchResponse(
            accepted,
            request.Events.Count - accepted);
    }

    public async Task<AdminLauncherTelemetrySummary> GetSummaryAsync(
        int windowHours,
        CancellationToken cancellationToken)
    {
        var to = timeProvider.GetUtcNow();
        var from = to.AddHours(-windowHours);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        var (eventCount, uniqueUsers, downloads, launches) =
            await ReadOverviewAsync(connection, from, to, cancellationToken);
        var launcherVersions = await ReadLauncherVersionsAsync(
            connection,
            from,
            to,
            cancellationToken);
        var profileVersions = await ReadProfileVersionsAsync(
            connection,
            from,
            to,
            cancellationToken);
        var failures = await ReadFailuresAsync(
            connection,
            from,
            to,
            cancellationToken);

        return new AdminLauncherTelemetrySummary(
            from,
            to,
            windowHours,
            eventCount,
            uniqueUsers,
            downloads,
            launches,
            launcherVersions,
            profileVersions,
            failures);
    }

    public async Task<int> DeleteBeforeAsync(
        DateTimeOffset cutoff,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "DELETE FROM launcher.client_telemetry_events WHERE received_at < $1;",
            connection);
        command.Parameters.AddWithValue(cutoff);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<(
        long EventCount,
        long UniqueUsers,
        AdminLauncherTelemetryOperationSummary Downloads,
        AdminLauncherTelemetryOperationSummary Launches)> ReadOverviewAsync(
        NpgsqlConnection connection,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                count(*)::bigint,
                count(DISTINCT user_id)::bigint,
                count(*) FILTER (
                    WHERE event_type IN ('Install', 'Repair')
                )::bigint,
                count(*) FILTER (
                    WHERE event_type IN ('Install', 'Repair')
                      AND outcome = 'Success'
                )::bigint,
                count(*) FILTER (
                    WHERE event_type IN ('Install', 'Repair')
                      AND outcome = 'Failure'
                )::bigint,
                count(*) FILTER (
                    WHERE event_type IN ('Install', 'Repair')
                      AND outcome = 'Canceled'
                )::bigint,
                COALESCE(sum(bytes) FILTER (
                    WHERE event_type IN ('Install', 'Repair')
                ), 0)::bigint,
                count(*) FILTER (WHERE event_type = 'Launch')::bigint,
                count(*) FILTER (
                    WHERE event_type = 'Launch' AND outcome = 'Success'
                )::bigint,
                count(*) FILTER (
                    WHERE event_type = 'Launch' AND outcome = 'Failure'
                )::bigint,
                count(*) FILTER (
                    WHERE event_type = 'Launch' AND outcome = 'Canceled'
                )::bigint
            FROM launcher.client_telemetry_events
            WHERE occurred_at >= $1 AND occurred_at < $2;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(from);
        command.Parameters.AddWithValue(to);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        var downloadAttempts = reader.GetInt64(2);
        var downloadFailed = reader.GetInt64(4);
        var launchAttempts = reader.GetInt64(7);
        var launchFailed = reader.GetInt64(9);
        return (
            reader.GetInt64(0),
            reader.GetInt64(1),
            new AdminLauncherTelemetryOperationSummary(
                downloadAttempts,
                reader.GetInt64(3),
                downloadFailed,
                reader.GetInt64(5),
                reader.GetInt64(6),
                CalculateFailureRate(downloadAttempts, downloadFailed)),
            new AdminLauncherTelemetryOperationSummary(
                launchAttempts,
                reader.GetInt64(8),
                launchFailed,
                reader.GetInt64(10),
                0,
                CalculateFailureRate(launchAttempts, launchFailed)));
    }

    private static async Task<IReadOnlyList<AdminLauncherVersionUsage>>
        ReadLauncherVersionsAsync(
            NpgsqlConnection connection,
            DateTimeOffset from,
            DateTimeOffset to,
            CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT latest.launcher_version, count(*)::bigint
            FROM (
                SELECT DISTINCT ON (user_id)
                       user_id, launcher_version
                FROM launcher.client_telemetry_events
                WHERE occurred_at >= $1 AND occurred_at < $2
                ORDER BY user_id, occurred_at DESC, received_at DESC
            ) latest
            GROUP BY latest.launcher_version
            ORDER BY count(*) DESC, latest.launcher_version DESC
            LIMIT 20;
            """;
        var result = new List<AdminLauncherVersionUsage>();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(from);
        command.Parameters.AddWithValue(to);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new AdminLauncherVersionUsage(
                reader.GetString(0),
                reader.GetInt64(1)));
        }

        return result;
    }

    private static async Task<IReadOnlyList<AdminProfileVersionUsage>>
        ReadProfileVersionsAsync(
            NpgsqlConnection connection,
            DateTimeOffset from,
            DateTimeOffset to,
            CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT profile_id,
                   profile_version,
                   count(DISTINCT user_id)::bigint,
                   count(*)::bigint
            FROM launcher.client_telemetry_events
            WHERE occurred_at >= $1
              AND occurred_at < $2
              AND profile_id IS NOT NULL
            GROUP BY profile_id, profile_version
            ORDER BY count(DISTINCT user_id) DESC,
                     count(*) DESC,
                     profile_id,
                     profile_version DESC
            LIMIT 40;
            """;
        var result = new List<AdminProfileVersionUsage>();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(from);
        command.Parameters.AddWithValue(to);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new AdminProfileVersionUsage(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt64(2),
                reader.GetInt64(3)));
        }

        return result;
    }

    private static async Task<IReadOnlyList<AdminLauncherTelemetryFailureSummary>>
        ReadFailuresAsync(
            NpgsqlConnection connection,
            DateTimeOffset from,
            DateTimeOffset to,
            CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT event_type, failure_code, count(*)::bigint
            FROM launcher.client_telemetry_events
            WHERE occurred_at >= $1
              AND occurred_at < $2
              AND outcome <> 'Success'
            GROUP BY event_type, failure_code
            ORDER BY count(*) DESC, event_type, failure_code
            LIMIT 30;
            """;
        var result = new List<AdminLauncherTelemetryFailureSummary>();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(from);
        command.Parameters.AddWithValue(to);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new AdminLauncherTelemetryFailureSummary(
                Enum.Parse<LauncherTelemetryEventType>(
                    reader.GetString(0),
                    ignoreCase: false),
                Enum.Parse<LauncherTelemetryFailureCode>(
                    reader.GetString(1),
                    ignoreCase: false),
                reader.GetInt64(2)));
        }

        return result;
    }

    private static double CalculateFailureRate(long attempts, long failures) =>
        attempts == 0
            ? 0
            : Math.Round(failures * 100d / attempts, 2);
}

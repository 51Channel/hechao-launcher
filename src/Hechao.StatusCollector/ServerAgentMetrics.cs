using System.Text.Json;
using Hechao.Contracts;

namespace Hechao.StatusCollector;

public sealed record ServerAgentMetrics(
    DateTimeOffset CapturedAt,
    double Tps1m,
    double Tps5m,
    double Tps15m,
    double MsptAverage,
    long GcCollectionTimeMilliseconds);

public sealed record ServerAgentMetricsProbeResult(
    ServerAgentMetrics? Metrics,
    ServerMetricIssueCode? Issue);

public interface IServerAgentMetricsReader
{
    Task<ServerAgentMetricsProbeResult> ProbeAsync(
        ServerProbeConfiguration server,
        DateTimeOffset capturedAt,
        TimeSpan maximumAge,
        CancellationToken cancellationToken);
}

public sealed class NullServerAgentMetricsReader : IServerAgentMetricsReader
{
    public static NullServerAgentMetricsReader Instance { get; } = new();

    private NullServerAgentMetricsReader()
    {
    }

    public Task<ServerAgentMetricsProbeResult> ProbeAsync(
        ServerProbeConfiguration server,
        DateTimeOffset capturedAt,
        TimeSpan maximumAge,
        CancellationToken cancellationToken) =>
        Task.FromResult(new ServerAgentMetricsProbeResult(
            null,
            ServerMetricIssueCode.MetricsNotConfigured));
}

public sealed class JsonServerAgentMetricsReader : IServerAgentMetricsReader
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };

    public async Task<ServerAgentMetricsProbeResult> ProbeAsync(
        ServerProbeConfiguration server,
        DateTimeOffset capturedAt,
        TimeSpan maximumAge,
        CancellationToken cancellationToken)
    {
        if (server.MetricsPath is null)
        {
            return new ServerAgentMetricsProbeResult(
                null,
                ServerMetricIssueCode.MetricsNotConfigured);
        }

        if (!File.Exists(server.MetricsPath))
        {
            return new ServerAgentMetricsProbeResult(
                null,
                ServerMetricIssueCode.MetricsFileMissing);
        }

        try
        {
            await using var stream = new FileStream(
                server.MetricsPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4096,
                useAsync: true);
            var snapshot = await JsonSerializer.DeserializeAsync<MetricsFile>(
                stream,
                SerializerOptions,
                cancellationToken);
            if (snapshot is null ||
                snapshot.SchemaVersion != 1 ||
                !IsFiniteInRange(snapshot.Tps1m, 0, 20.1) ||
                !IsFiniteInRange(snapshot.Tps5m, 0, 20.1) ||
                !IsFiniteInRange(snapshot.Tps15m, 0, 20.1) ||
                !IsFiniteInRange(snapshot.MsptAverage, 0, 60_000) ||
                snapshot.GcCollectionTimeMilliseconds is < 0 or > 31_536_000_000 ||
                snapshot.CapturedAt > capturedAt.AddMinutes(1))
            {
                return new ServerAgentMetricsProbeResult(
                    null,
                    ServerMetricIssueCode.MetricsFileInvalid);
            }

            if (snapshot.CapturedAt < capturedAt - maximumAge)
            {
                return new ServerAgentMetricsProbeResult(
                    null,
                    ServerMetricIssueCode.MetricsFileStale);
            }

            return new ServerAgentMetricsProbeResult(
                new ServerAgentMetrics(
                    snapshot.CapturedAt,
                    snapshot.Tps1m,
                    snapshot.Tps5m,
                    snapshot.Tps15m,
                    snapshot.MsptAverage,
                    snapshot.GcCollectionTimeMilliseconds),
                null);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            JsonException or NotSupportedException)
        {
            return new ServerAgentMetricsProbeResult(
                null,
                ServerMetricIssueCode.MetricsFileInvalid);
        }
    }

    private static bool IsFiniteInRange(
        double value,
        double minimum,
        double maximum) =>
        double.IsFinite(value) && value >= minimum && value <= maximum;

    private sealed class MetricsFile
    {
        public int SchemaVersion { get; init; }

        public DateTimeOffset CapturedAt { get; init; }

        public double Tps1m { get; init; }

        public double Tps5m { get; init; }

        public double Tps15m { get; init; }

        public double MsptAverage { get; init; }

        public long GcCollectionTimeMilliseconds { get; init; }
    }
}

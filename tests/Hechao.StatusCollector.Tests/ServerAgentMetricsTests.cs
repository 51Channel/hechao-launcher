using System.Text.Json;
using Hechao.Contracts;

namespace Hechao.StatusCollector.Tests;

public sealed class ServerAgentMetricsTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"hechao-agent-{Guid.NewGuid():N}");

    public ServerAgentMetricsTests()
    {
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public async Task ProbeAsync_ReadsValidFreshSnapshot()
    {
        var now = new DateTimeOffset(
            2026,
            7,
            27,
            8,
            30,
            0,
            TimeSpan.Zero);
        var path = await WriteSnapshotAsync(new
        {
            schemaVersion = 1,
            capturedAt = now.AddSeconds(-5),
            tps1m = 19.98,
            tps5m = 19.97,
            tps15m = 19.96,
            msptAverage = 18.4,
            gcCollectionTimeMilliseconds = 12_345
        });
        var server = CreateServer(path);

        var result = await new JsonServerAgentMetricsReader().ProbeAsync(
            server,
            now,
            TimeSpan.FromSeconds(30),
            CancellationToken.None);

        Assert.Null(result.Issue);
        Assert.NotNull(result.Metrics);
        Assert.Equal(19.98, result.Metrics.Tps1m);
        Assert.Equal(18.4, result.Metrics.MsptAverage);
        Assert.Equal(12_345, result.Metrics.GcCollectionTimeMilliseconds);
    }

    [Fact]
    public async Task ProbeAsync_RejectsStaleSnapshot()
    {
        var now = new DateTimeOffset(
            2026,
            7,
            27,
            8,
            30,
            0,
            TimeSpan.Zero);
        var path = await WriteSnapshotAsync(new
        {
            schemaVersion = 1,
            capturedAt = now.AddMinutes(-2),
            tps1m = 20,
            tps5m = 20,
            tps15m = 20,
            msptAverage = 10,
            gcCollectionTimeMilliseconds = 10
        });

        var result = await new JsonServerAgentMetricsReader().ProbeAsync(
            CreateServer(path),
            now,
            TimeSpan.FromSeconds(30),
            CancellationToken.None);

        Assert.Null(result.Metrics);
        Assert.Equal(ServerMetricIssueCode.MetricsFileStale, result.Issue);
    }

    [Fact]
    public async Task ProbeAsync_RejectsMalformedSnapshot()
    {
        var path = Path.Combine(_directory, "metrics.json");
        await File.WriteAllTextAsync(path, "{ not-json");

        var result = await new JsonServerAgentMetricsReader().ProbeAsync(
            CreateServer(path),
            DateTimeOffset.UtcNow,
            TimeSpan.FromSeconds(30),
            CancellationToken.None);

        Assert.Null(result.Metrics);
        Assert.Equal(ServerMetricIssueCode.MetricsFileInvalid, result.Issue);
    }

    [Fact]
    public async Task ProbeAsync_ReportsMissingAndUnconfiguredFiles()
    {
        var reader = new JsonServerAgentMetricsReader();
        var now = DateTimeOffset.UtcNow;

        var missing = await reader.ProbeAsync(
            CreateServer(Path.Combine(_directory, "missing.json")),
            now,
            TimeSpan.FromSeconds(30),
            CancellationToken.None);
        var unconfigured = await reader.ProbeAsync(
            new ServerProbeConfiguration
            {
                VelocityTarget = "pvp",
                Host = "remote.example",
                Port = 25565,
                FallbackMaxPlayers = 30
            },
            now,
            TimeSpan.FromSeconds(30),
            CancellationToken.None);

        Assert.Equal(ServerMetricIssueCode.MetricsFileMissing, missing.Issue);
        Assert.Equal(
            ServerMetricIssueCode.MetricsNotConfigured,
            unconfigured.Issue);
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
        GC.SuppressFinalize(this);
    }

    private async Task<string> WriteSnapshotAsync(object snapshot)
    {
        var path = Path.Combine(_directory, "metrics.json");
        await File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(snapshot));
        return path;
    }

    private static ServerProbeConfiguration CreateServer(string metricsPath) =>
        new()
        {
            VelocityTarget = "lobby",
            Host = "127.0.0.1",
            Port = 25566,
            FallbackMaxPlayers = 300,
            DataPath = Path.GetDirectoryName(metricsPath),
            MetricsPath = metricsPath
        };
}

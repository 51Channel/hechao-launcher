using System.Net.Sockets;
using Hechao.Contracts;

namespace Hechao.StatusCollector.Tests;

public sealed class ServerHeartbeatCollectorTests
{
    [Fact]
    public async Task CollectAsync_IsolatesFailedTargets()
    {
        var configuration = new CollectorConfiguration
        {
            CollectorInstance = "mc-vps-primary",
            ProbeTimeoutSeconds = 2,
            Servers =
            [
                new ServerProbeConfiguration
                {
                    VelocityTarget = "lobby",
                    Host = "online",
                    Port = 25566,
                    FallbackMaxPlayers = 300
                },
                new ServerProbeConfiguration
                {
                    VelocityTarget = "activity",
                    Host = "offline",
                    Port = 25568,
                    FallbackMaxPlayers = 30
                }
            ]
        };
        var collector = new ServerHeartbeatCollector(new FakeStatusClient());

        var result = await collector.CollectAsync(configuration, CancellationToken.None);

        Assert.Equal("mc-vps-primary", result.CollectorInstance);
        Assert.Collection(
            result.Servers,
            server =>
            {
                Assert.Equal("lobby", server.VelocityTarget);
                Assert.True(server.Online);
                Assert.Equal(8, server.OnlinePlayers);
                Assert.Equal(300, server.MaxPlayers);
            },
            server =>
            {
                Assert.Equal("activity", server.VelocityTarget);
                Assert.False(server.Online);
                Assert.Equal(0, server.OnlinePlayers);
                Assert.Equal(30, server.MaxPlayers);
                Assert.Contains(
                    ServerMetricIssueCode.StatusUnavailable,
                    server.Issues!);
            });
    }

    [Fact]
    public async Task CollectAsync_CombinesProcessAndAgentMetrics()
    {
        var now = new DateTimeOffset(
            2026,
            7,
            27,
            8,
            30,
            0,
            TimeSpan.Zero);
        var configuration = new CollectorConfiguration
        {
            CollectorInstance = "mc-vps-primary",
            ProbeTimeoutSeconds = 2,
            MetricsMaxAgeSeconds = 30,
            Servers =
            [
                new ServerProbeConfiguration
                {
                    VelocityTarget = "lobby",
                    Host = "online",
                    Port = 25566,
                    FallbackMaxPlayers = 300,
                    DataPath = @"E:\LobbyServer",
                    MetricsPath =
                        @"E:\LobbyServer\plugins\HechaoServerMetrics\metrics.json"
                }
            ]
        };
        var processMetrics = new ServerProcessMetrics(
            4_294_967_296,
            5_368_709_120,
            37.5,
            now.AddHours(-3));
        var agentMetrics = new ServerAgentMetrics(
            now.AddSeconds(-5),
            19.98,
            19.97,
            19.96,
            18.4,
            12_345);
        var collector = new ServerHeartbeatCollector(
            new FakeStatusClient(),
            new FakeProcessMetricsProvider(
                new ServerProcessProbeResult(
                    processMetrics,
                    214_748_364_800,
                    536_870_912_000,
                    [ServerMetricIssueCode.DiskProbeFailed])),
            new FakeAgentMetricsReader(
                new ServerAgentMetricsProbeResult(agentMetrics, null)),
            new FrozenTimeProvider(now));

        var result = await collector.CollectAsync(
            configuration,
            CancellationToken.None);

        var server = Assert.Single(result.Servers);
        Assert.Equal(now, result.CapturedAt);
        Assert.Equal(processMetrics.WorkingSetBytes, server.ProcessWorkingSetBytes);
        Assert.Equal(processMetrics.PrivateBytes, server.ProcessPrivateBytes);
        Assert.Equal(processMetrics.CpuPercent, server.ProcessCpuPercent);
        Assert.Equal(processMetrics.StartedAt, server.ProcessStartedAt);
        Assert.Equal(agentMetrics.Tps1m, server.Tps1m);
        Assert.Equal(agentMetrics.MsptAverage, server.MsptAverage);
        Assert.Equal(
            agentMetrics.GcCollectionTimeMilliseconds,
            server.GcCollectionTimeMilliseconds);
        Assert.Equal(agentMetrics.CapturedAt, server.MetricsCapturedAt);
        Assert.Equal(
            [ServerMetricIssueCode.DiskProbeFailed],
            server.Issues);
    }

    [Fact]
    public async Task CollectAsync_ReportsProbeIssuesWithoutFailingBatch()
    {
        var now = new DateTimeOffset(
            2026,
            7,
            27,
            8,
            30,
            0,
            TimeSpan.Zero);
        var configuration = new CollectorConfiguration
        {
            CollectorInstance = "mc-vps-primary",
            Servers =
            [
                new ServerProbeConfiguration
                {
                    VelocityTarget = "activity",
                    Host = "offline",
                    Port = 25568,
                    FallbackMaxPlayers = 30
                }
            ]
        };
        var collector = new ServerHeartbeatCollector(
            new FakeStatusClient(),
            new FakeProcessMetricsProvider(
                new ServerProcessProbeResult(
                    null,
                    null,
                    null,
                    [ServerMetricIssueCode.ProcessNotRunning])),
            new FakeAgentMetricsReader(
                new ServerAgentMetricsProbeResult(
                    null,
                    ServerMetricIssueCode.MetricsFileMissing)),
            new FrozenTimeProvider(now));

        var result = await collector.CollectAsync(
            configuration,
            CancellationToken.None);

        var server = Assert.Single(result.Servers);
        Assert.False(server.Online);
        Assert.Equal(
            [
                ServerMetricIssueCode.StatusUnavailable,
                ServerMetricIssueCode.ProcessNotRunning,
                ServerMetricIssueCode.MetricsFileMissing
            ],
            server.Issues);
    }

    [Fact]
    public async Task CollectAsync_ReportsOptedInEmptyServerAsQuiescent()
    {
        var now = new DateTimeOffset(
            2026,
            7,
            30,
            8,
            30,
            0,
            TimeSpan.Zero);
        var staleMetrics = new ServerAgentMetrics(
            now.AddHours(-2),
            20,
            20,
            20,
            0.5,
            123);
        var configuration = new CollectorConfiguration
        {
            CollectorInstance = "mc-vps-primary",
            Servers =
            [
                new ServerProbeConfiguration
                {
                    VelocityTarget = "activity",
                    Host = "empty",
                    Port = 25568,
                    FallbackMaxPlayers = 30,
                    AllowStaleMetricsWhenEmpty = true
                }
            ]
        };
        var collector = new ServerHeartbeatCollector(
            new FakeStatusClient(),
            NullServerProcessMetricsProvider.Instance,
            new FakeAgentMetricsReader(
                new ServerAgentMetricsProbeResult(
                    staleMetrics,
                    ServerMetricIssueCode.MetricsFileStale)),
            new FrozenTimeProvider(now));

        var result = await collector.CollectAsync(
            configuration,
            CancellationToken.None);

        var server = Assert.Single(result.Servers);
        Assert.True(server.Online);
        Assert.Equal(0, server.OnlinePlayers);
        Assert.Null(server.Tps1m);
        Assert.Null(server.MetricsCapturedAt);
        Assert.DoesNotContain(
            ServerMetricIssueCode.MetricsFileStale,
            server.Issues!);
    }

    [Fact]
    public async Task CollectAsync_RejectsStaleMetricsWhenPlayerIsOnline()
    {
        var now = new DateTimeOffset(
            2026,
            7,
            30,
            8,
            30,
            0,
            TimeSpan.Zero);
        var configuration = new CollectorConfiguration
        {
            CollectorInstance = "mc-vps-primary",
            Servers =
            [
                new ServerProbeConfiguration
                {
                    VelocityTarget = "activity",
                    Host = "online",
                    Port = 25568,
                    FallbackMaxPlayers = 30,
                    AllowStaleMetricsWhenEmpty = true
                }
            ]
        };
        var collector = new ServerHeartbeatCollector(
            new FakeStatusClient(),
            NullServerProcessMetricsProvider.Instance,
            new FakeAgentMetricsReader(
                new ServerAgentMetricsProbeResult(
                    new ServerAgentMetrics(
                        now.AddHours(-2),
                        20,
                        20,
                        20,
                        0.5,
                        123),
                    ServerMetricIssueCode.MetricsFileStale)),
            new FrozenTimeProvider(now));

        var result = await collector.CollectAsync(
            configuration,
            CancellationToken.None);

        var server = Assert.Single(result.Servers);
        Assert.True(server.Online);
        Assert.Equal(8, server.OnlinePlayers);
        Assert.Null(server.Tps1m);
        Assert.Null(server.MetricsCapturedAt);
        Assert.Contains(
            ServerMetricIssueCode.MetricsFileStale,
            server.Issues!);
    }

    private sealed class FakeStatusClient : IMinecraftStatusClient
    {
        public Task<MinecraftServerStatus> QueryAsync(
            string host,
            int port,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            if (host == "online")
            {
                return Task.FromResult(
                    new MinecraftServerStatus(8, 300, "Paper 1.21.11", 774));
            }

            if (host == "empty")
            {
                return Task.FromResult(
                    new MinecraftServerStatus(0, 30, "NeoForge 1.21.11", 774));
            }

            throw new SocketException((int)SocketError.ConnectionRefused);
        }
    }

    private sealed class FakeProcessMetricsProvider(
        ServerProcessProbeResult result) : IServerProcessMetricsProvider
    {
        public Task<ServerProcessProbeResult> ProbeAsync(
            ServerProbeConfiguration server,
            CancellationToken cancellationToken) =>
            Task.FromResult(result);
    }

    private sealed class FakeAgentMetricsReader(
        ServerAgentMetricsProbeResult result) : IServerAgentMetricsReader
    {
        public Task<ServerAgentMetricsProbeResult> ProbeAsync(
            ServerProbeConfiguration server,
            DateTimeOffset capturedAt,
            TimeSpan maximumAge,
            CancellationToken cancellationToken) =>
            Task.FromResult(result);
    }

    private sealed class FrozenTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

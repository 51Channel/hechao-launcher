using Hechao.Api.Monitoring;
using Hechao.Contracts;

namespace Hechao.Api.Tests;

public sealed class ServerRuntimeStatusResolverTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 23, 12, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan Freshness = TimeSpan.FromMinutes(3);

    [Theory]
    [InlineData(ServerStatus.Maintenance)]
    [InlineData(ServerStatus.Closed)]
    public void Resolve_ConfiguredUnavailableStatusOverridesHeartbeat(ServerStatus configuredStatus)
    {
        var heartbeat = new ServerHeartbeatObservation(true, 12, 30, Now);

        var result = ServerRuntimeStatusResolver.Resolve(
            configuredStatus,
            0,
            30,
            heartbeat,
            Now,
            Freshness);

        Assert.Equal(configuredStatus, result.Status);
        Assert.Equal(0, result.OnlinePlayers);
        Assert.Equal(30, result.MaxPlayers);
    }

    [Fact]
    public void Resolve_NoHeartbeatPreservesConfiguredFallback()
    {
        var result = ServerRuntimeStatusResolver.Resolve(
            ServerStatus.Online,
            18,
            100,
            null,
            Now,
            Freshness);

        Assert.Equal(new ResolvedServerRuntimeStatus(ServerStatus.Online, 18, 100), result);
    }

    [Fact]
    public void Resolve_FreshHeartbeatSuppliesLiveState()
    {
        var heartbeat = new ServerHeartbeatObservation(true, 7, 30, Now.AddSeconds(-20));

        var result = ServerRuntimeStatusResolver.Resolve(
            ServerStatus.Online,
            21,
            30,
            heartbeat,
            Now,
            Freshness);

        Assert.Equal(new ResolvedServerRuntimeStatus(ServerStatus.Online, 7, 30), result);
    }

    [Fact]
    public void Resolve_OfflineHeartbeatClosesServer()
    {
        var heartbeat = new ServerHeartbeatObservation(false, 0, 0, Now);

        var result = ServerRuntimeStatusResolver.Resolve(
            ServerStatus.Online,
            18,
            100,
            heartbeat,
            Now,
            Freshness);

        Assert.Equal(new ResolvedServerRuntimeStatus(ServerStatus.Closed, 0, 100), result);
    }

    [Fact]
    public void Resolve_StaleHeartbeatClosesServer()
    {
        var heartbeat = new ServerHeartbeatObservation(true, 7, 30, Now.AddMinutes(-4));

        var result = ServerRuntimeStatusResolver.Resolve(
            ServerStatus.Online,
            21,
            30,
            heartbeat,
            Now,
            Freshness);

        Assert.Equal(new ResolvedServerRuntimeStatus(ServerStatus.Closed, 0, 30), result);
    }
}

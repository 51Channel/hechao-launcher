using System.Net.Sockets;

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
            });
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

            throw new SocketException((int)SocketError.ConnectionRefused);
        }
    }
}

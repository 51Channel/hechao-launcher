using System.Net;
using System.Net.Sockets;

namespace Hechao.StatusCollector.Tests;

public sealed class WindowsServerProcessMetricsProviderTests
{
    [Fact]
    public async Task ProbeAsync_MapsListeningPortToCurrentProcess()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = Assert.IsType<IPEndPoint>(listener.LocalEndpoint);
        var server = new ServerProbeConfiguration
        {
            VelocityTarget = "probe-test",
            Host = "127.0.0.1",
            Port = endpoint.Port,
            FallbackMaxPlayers = 1,
            DataPath = Path.GetTempPath()
        };

        var result = await new WindowsServerProcessMetricsProvider().ProbeAsync(
            server,
            CancellationToken.None);

        Assert.NotNull(result.Process);
        Assert.True(result.Process.WorkingSetBytes > 0);
        Assert.True(result.Process.PrivateBytes > 0);
        Assert.InRange(result.Process.CpuPercent, 0, 100);
        Assert.True(result.Process.StartedAt < DateTimeOffset.UtcNow);
        Assert.True(result.DiskFreeBytes > 0);
        Assert.True(result.DiskTotalBytes >= result.DiskFreeBytes);
        Assert.Empty(result.Issues);
    }
}

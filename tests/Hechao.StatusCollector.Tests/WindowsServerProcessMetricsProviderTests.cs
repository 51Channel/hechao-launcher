using System.Net;
using System.Net.Sockets;
using Hechao.Contracts;

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
        var processPath = Assert.IsType<string>(Environment.ProcessPath);
        var server = new ServerProbeConfiguration
        {
            VelocityTarget = "probe-test",
            Host = "127.0.0.1",
            Port = endpoint.Port,
            FallbackMaxPlayers = 1,
            DataPath = Path.GetTempPath(),
            ExpectedProcessExecutablePath = Path.GetFullPath(processPath)
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
        Assert.True(result.EndpointOwnedByExpectedProcess);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public async Task ProbeAsync_RejectsListenerOwnedByDifferentExecutable()
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
            VelocityTarget = "shared-port-inactive-target",
            Host = "127.0.0.1",
            Port = endpoint.Port,
            FallbackMaxPlayers = 1,
            DataPath = Path.GetTempPath(),
            ExpectedProcessExecutablePath = Path.Combine(
                Path.GetTempPath(),
                "different-java.exe")
        };

        var result = await new WindowsServerProcessMetricsProvider().ProbeAsync(
            server,
            CancellationToken.None);

        Assert.Null(result.Process);
        Assert.False(result.EndpointOwnedByExpectedProcess);
        Assert.Contains(ServerMetricIssueCode.ProcessNotRunning, result.Issues);
    }
}

namespace Hechao.ServerControlAgent.Tests;

public sealed class ServerControlWorkerSchedulingTests
{
    [Fact]
    public void HeartbeatsAndCommandsRunInIndependentLoops()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "Hechao.ServerControlAgent",
            "ServerControlWorker.cs"));

        Assert.Contains("await Task.WhenAll(", source, StringComparison.Ordinal);
        Assert.Contains("RunHeartbeatLoopAsync(cancellationToken)", source, StringComparison.Ordinal);
        Assert.Contains("RunCommandLoopAsync(cancellationToken)", source, StringComparison.Ordinal);

        var heartbeatStart = source.IndexOf(
            "private async Task RunHeartbeatLoopAsync",
            StringComparison.Ordinal);
        var commandStart = source.IndexOf(
            "private async Task RunCommandLoopAsync",
            StringComparison.Ordinal);
        var sendHeartbeatStart = source.IndexOf(
            "private async Task SendHeartbeatAsync",
            StringComparison.Ordinal);

        Assert.True(heartbeatStart >= 0);
        Assert.True(commandStart > heartbeatStart);
        Assert.True(sendHeartbeatStart > commandStart);

        var heartbeatLoop = source[heartbeatStart..commandStart];
        var commandLoop = source[commandStart..sendHeartbeatStart];
        Assert.Contains("SendHeartbeatAsync(cancellationToken)", heartbeatLoop, StringComparison.Ordinal);
        Assert.DoesNotContain("ProcessCommandAsync", heartbeatLoop, StringComparison.Ordinal);
        Assert.Contains("ProcessCommandAsync(command, cancellationToken)", commandLoop, StringComparison.Ordinal);
        Assert.DoesNotContain("SendHeartbeatAsync", commandLoop, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Hechao.Launcher.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Repository not found.");
    }
}

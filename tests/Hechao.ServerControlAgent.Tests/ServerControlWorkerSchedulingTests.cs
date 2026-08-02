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
        Assert.Contains("\"heartbeat_failed\"", source, StringComparison.Ordinal);
        Assert.Contains("SendHeartbeatAsync", source, StringComparison.Ordinal);
        Assert.Contains("\"command_poll_failed\"", source, StringComparison.Ordinal);
        Assert.Contains("PollCommandsAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoopContinuesAfterUnexpectedIterationFailure()
    {
        var logPath = Path.Combine(
            Path.GetTempPath(),
            $"hechao-agent-loop-{Guid.NewGuid():N}",
            "agent.log");
        var runner = new ResilientLoopRunner(new AgentLog(logPath));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var attempts = 0;

        await runner.RunAsync(
            "test_loop_failed",
            TimeSpan.FromMilliseconds(1),
            _ =>
            {
                if (Interlocked.Increment(ref attempts) == 1)
                {
                    throw new FormatException("unexpected test failure");
                }

                cancellation.Cancel();
                return Task.CompletedTask;
            },
            cancellation.Token);

        Assert.Equal(2, attempts);
        var log = await File.ReadAllTextAsync(logPath);
        Assert.Contains("test_loop_failed", log, StringComparison.Ordinal);
        Assert.Contains("FormatException", log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnavailableLogPathDoesNotStopNextIteration()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"hechao-agent-log-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var pathBlockingDirectory = Path.Combine(root, "blocked");
        await File.WriteAllTextAsync(pathBlockingDirectory, "not a directory");
        var runner = new ResilientLoopRunner(new AgentLog(
            Path.Combine(pathBlockingDirectory, "agent.log")));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var attempts = 0;

        await runner.RunAsync(
            "test_log_failed",
            TimeSpan.FromMilliseconds(1),
            _ =>
            {
                if (Interlocked.Increment(ref attempts) == 1)
                {
                    throw new NotSupportedException("trigger logging");
                }

                cancellation.Cancel();
                return Task.CompletedTask;
            },
            cancellation.Token);

        Assert.Equal(2, attempts);
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

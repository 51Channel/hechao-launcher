using Hechao.Launcher.Infrastructure;

namespace Hechao.Launcher.Tests;

public sealed class AsyncRelayCommandTests
{
    [Fact]
    public async Task ExecuteAsync_ReportsFailureAndRestoresCommandState()
    {
        var failure = new InvalidOperationException("expected");
        Exception? reported = null;
        var command = new AsyncRelayCommand(
            () => Task.FromException(failure),
            exception => reported = exception);

        await command.ExecuteAsync();

        Assert.Same(failure, reported);
        Assert.False(command.IsExecuting);
        Assert.True(command.CanExecute(null));
    }

    [Fact]
    public async Task ExecuteAsync_IgnoresDuplicateInvocationWhileRunning()
    {
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var invocationCount = 0;
        var command = new AsyncRelayCommand(
            async () =>
            {
                invocationCount++;
                started.SetResult();
                await release.Task;
            },
            _ => Assert.Fail("The command should not fail."));

        var first = command.ExecuteAsync();
        await started.Task;
        await command.ExecuteAsync();

        Assert.Equal(1, invocationCount);
        Assert.False(command.CanExecute(null));

        release.SetResult();
        await first;

        Assert.True(command.CanExecute(null));
    }

    [Fact]
    public async Task GenericExecuteAsync_ForwardsParameterAndRestoresCommandState()
    {
        string? received = null;
        var command = new AsyncRelayCommand<string>(
            value =>
            {
                received = value;
                return Task.CompletedTask;
            },
            _ => Assert.Fail("The command should not fail."),
            value => !string.IsNullOrWhiteSpace(value));

        Assert.False(command.CanExecute(null));
        Assert.True(command.CanExecute("activity"));

        await command.ExecuteAsync("activity");

        Assert.Equal("activity", received);
        Assert.False(command.IsExecuting);
        Assert.True(command.CanExecute("activity"));
    }
}

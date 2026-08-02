namespace Hechao.ServerControlAgent;

internal sealed class ResilientLoopRunner(AgentLog log)
{
    internal async Task RunAsync(
        string failureEventName,
        TimeSpan interval,
        Func<CancellationToken, Task> iteration,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await iteration(cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                log.WriteBestEffort(
                    "ERROR",
                    failureEventName,
                    $"{exception.GetType().Name}: {exception.Message}");
            }

            try
            {
                await Task.Delay(interval, cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
        }
    }
}

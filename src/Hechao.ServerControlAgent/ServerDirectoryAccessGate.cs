namespace Hechao.ServerControlAgent;

internal sealed class ServerDirectoryAccessGate
{
    private readonly SemaphoreSlim semaphore = new(1, 1);

    internal async ValueTask<IDisposable> EnterAsync(
        CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken);
        return new Lease(semaphore);
    }

    private sealed class Lease(SemaphoreSlim semaphore) : IDisposable
    {
        private SemaphoreSlim? semaphore = semaphore;

        public void Dispose() =>
            Interlocked.Exchange(ref semaphore, null)?.Release();
    }
}

using System.Threading;

namespace Hechao.Launcher.Infrastructure;

internal sealed class SingleInstanceGuard : IDisposable
{
    private const string MutexName = @"Local\Hechao.Launcher.SingleInstance.v1";
    private readonly Mutex _mutex;
    private bool _ownsMutex;

    private SingleInstanceGuard(Mutex mutex)
    {
        _mutex = mutex;
        _ownsMutex = true;
    }

    public static bool TryAcquire(out SingleInstanceGuard? guard)
    {
        var mutex = new Mutex(initiallyOwned: false, MutexName);
        var acquired = false;
        try
        {
            acquired = mutex.WaitOne(TimeSpan.Zero);
        }
        catch (AbandonedMutexException)
        {
            acquired = true;
        }

        if (!acquired)
        {
            mutex.Dispose();
            guard = null;
            return false;
        }

        guard = new SingleInstanceGuard(mutex);
        return true;
    }

    public void Dispose()
    {
        if (_ownsMutex)
        {
            _mutex.ReleaseMutex();
            _ownsMutex = false;
        }

        _mutex.Dispose();
    }
}

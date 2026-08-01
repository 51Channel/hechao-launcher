using System.Diagnostics;
using System.IO;

namespace Hechao.Launcher.Infrastructure;

/// <summary>
/// Holds an exclusive lock file for one normalized resource path. File handle ownership is not
/// thread-affine, so callers can safely keep the lock across asynchronous I/O.
/// </summary>
internal sealed class PathFileLock : IDisposable
{
    private const int SharingViolation = 32;
    private const int LockViolation = 33;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(50);

    private readonly FileStream _stream;
    private int _disposed;

    private PathFileLock(FileStream stream)
    {
        _stream = stream;
    }

    public static PathFileLock Acquire(
        string resourcePath,
        string lockPath,
        TimeSpan waitTimeout)
    {
        var context = CreateContext(resourcePath, lockPath, waitTimeout);
        var stopwatch = Stopwatch.StartNew();

        while (true)
        {
            try
            {
                return new PathFileLock(Open(context.LockPath));
            }
            catch (IOException exception) when (IsContention(exception))
            {
                var remaining = context.WaitTimeout - stopwatch.Elapsed;
                if (remaining <= TimeSpan.Zero)
                {
                    throw new PathFileLockTimeoutException(
                        context.ResourcePath,
                        context.LockPath,
                        context.WaitTimeout,
                        exception);
                }

                Thread.Sleep(remaining < RetryDelay ? remaining : RetryDelay);
            }
        }
    }

    public static async Task<PathFileLock> AcquireAsync(
        string resourcePath,
        string lockPath,
        TimeSpan waitTimeout,
        CancellationToken cancellationToken)
    {
        var context = CreateContext(resourcePath, lockPath, waitTimeout);
        var stopwatch = Stopwatch.StartNew();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return new PathFileLock(Open(context.LockPath));
            }
            catch (IOException exception) when (IsContention(exception))
            {
                var remaining = context.WaitTimeout - stopwatch.Elapsed;
                if (remaining <= TimeSpan.Zero)
                {
                    throw new PathFileLockTimeoutException(
                        context.ResourcePath,
                        context.LockPath,
                        context.WaitTimeout,
                        exception);
                }

                await Task.Delay(
                        remaining < RetryDelay ? remaining : RetryDelay,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _stream.Dispose();
        }
    }

    private static LockContext CreateContext(
        string resourcePath,
        string lockPath,
        TimeSpan waitTimeout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(lockPath);

        if (waitTimeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(waitTimeout),
                waitTimeout,
                "The lock wait timeout cannot be negative.");
        }

        var normalizedResourcePath = Path.GetFullPath(resourcePath);
        var normalizedLockPath = Path.GetFullPath(lockPath);
        var lockDirectory = Path.GetDirectoryName(normalizedLockPath)
            ?? throw new InvalidOperationException("The lock path has no parent directory.");

        Directory.CreateDirectory(lockDirectory);
        return new LockContext(normalizedResourcePath, normalizedLockPath, waitTimeout);
    }

    private static FileStream Open(string lockPath)
    {
        return new FileStream(
            lockPath,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: 1,
            FileOptions.None);
    }

    private static bool IsContention(IOException exception)
    {
        var errorCode = exception.HResult & 0xFFFF;
        return errorCode is SharingViolation or LockViolation;
    }

    private sealed record LockContext(
        string ResourcePath,
        string LockPath,
        TimeSpan WaitTimeout);
}

internal sealed class PathFileLockTimeoutException : IOException
{
    public PathFileLockTimeoutException(
        string resourcePath,
        string lockPath,
        TimeSpan waitTimeout,
        IOException innerException)
        : base(
            $"Timed out after {waitTimeout.TotalSeconds:0.###} seconds waiting for exclusive access to '{resourcePath}'.",
            innerException)
    {
        ResourcePath = resourcePath;
        LockPath = lockPath;
        WaitTimeout = waitTimeout;
    }

    public string ResourcePath { get; }

    public string LockPath { get; }

    public TimeSpan WaitTimeout { get; }
}

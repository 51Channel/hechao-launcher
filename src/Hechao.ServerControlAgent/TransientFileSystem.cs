namespace Hechao.ServerControlAgent;

internal static class TransientFileSystem
{
    private const int ErrorAccessDenied = 5;
    private const int ErrorSharingViolation = 32;
    private const int ErrorLockViolation = 33;
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromMilliseconds(50),
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(200),
        TimeSpan.FromMilliseconds(400),
        TimeSpan.FromMilliseconds(800)
    ];

    internal static void MoveDirectory(string source, string destination) =>
        ExecuteDirectoryMoveWithRetry(
            () => Directory.Move(source, destination));

    internal static void MoveFile(string source, string destination) =>
        ExecuteWithSharingRetry(() => File.Move(source, destination));

    internal static void ExecuteWithSharingRetry(
        Action operation,
        Action<TimeSpan>? delay = null) =>
        ExecuteWithRetry(
            operation,
            retryDirectoryAccessDenied: false,
            delay);

    internal static void ExecuteDirectoryMoveWithRetry(
        Action operation,
        Action<TimeSpan>? delay = null) =>
        ExecuteWithRetry(
            operation,
            retryDirectoryAccessDenied: true,
            delay);

    private static void ExecuteWithRetry(
        Action operation,
        bool retryDirectoryAccessDenied,
        Action<TimeSpan>? delay)
    {
        ArgumentNullException.ThrowIfNull(operation);
        delay ??= Thread.Sleep;

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                operation();
                return;
            }
            catch (IOException exception) when (
                IsTransientMoveFailure(
                    exception,
                    retryDirectoryAccessDenied) &&
                attempt < RetryDelays.Length)
            {
                delay(RetryDelays[attempt]);
            }
        }
    }

    private static bool IsTransientMoveFailure(
        IOException exception,
        bool retryDirectoryAccessDenied)
    {
        var errorCode = exception.HResult & 0xFFFF;
        return errorCode is ErrorSharingViolation or ErrorLockViolation ||
               // Directory.Move reports an open descendant this way on Windows.
               retryDirectoryAccessDenied && errorCode == ErrorAccessDenied;
    }
}

using Hechao.Contracts;

namespace Hechao.ServerControlAgent;

internal sealed class ServerDirectoryDeletionManager(
    ServerControlTargetConfiguration configuration,
    ServerDirectoryAccessGate accessGate,
    string runtimeMarkerPath)
{
    private readonly string _serverDirectory =
        Path.GetFullPath(configuration.ServerDirectory)
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
    private readonly string _parentDirectory =
        Directory.GetParent(Path.GetFullPath(configuration.ServerDirectory))
            ?.FullName ?? throw new InvalidDataException(
                "The server directory cannot be a volume root.");
    private readonly string _stagingPrefix =
        $".hechao-delete-{configuration.ServerId}-";

    internal async Task<AgentCommandResult> DeleteAsync(
        Guid commandId,
        Func<CancellationToken, Task<int?>> findProcessIdAsync,
        CancellationToken cancellationToken)
    {
        if (!configuration.ServerDeletionEnabled)
        {
            return Failed(
                "SERVER_DELETION_DISABLED",
                "该服务端未在本机代理配置中开放文件删除权限。");
        }

        if (await findProcessIdAsync(cancellationToken) is not null)
        {
            return Conflict(
                "SERVER_STILL_RUNNING",
                "服务器仍在运行，已拒绝删除服务端文件。");
        }

        using (await accessGate.EnterAsync(cancellationToken))
        {
            if (await findProcessIdAsync(cancellationToken) is not null)
            {
                return Conflict(
                    "SERVER_STARTED_DURING_DELETE",
                    "删除前服务器状态发生变化，已拒绝删除服务端文件。");
            }

            if (!Directory.Exists(_serverDirectory))
            {
                TryDeleteRuntimeMarker();
                var cleanupPending = TryCleanupPendingDirectories();
                return Succeeded(
                    cleanupPending
                        ? "SERVER_FILES_ALREADY_REMOVED_CLEANUP_PENDING"
                        : "SERVER_FILES_ALREADY_DELETED",
                    cleanupPending
                        ? "服务端运行目录已经移除，仍有暂存文件等待后台清理。"
                        : "服务端运行目录已经不存在，无需重复删除。");
            }

            try
            {
                EnsureDeletionRootIsSafe();
                var pendingDirectories = TryFindPendingDirectories();
                if (pendingDirectories is null)
                {
                    return Failed(
                        "DELETE_STAGING_SCAN_FAILED",
                        "无法确认服务端父目录中的待清理状态，未移动当前运行目录。");
                }

                if (pendingDirectories.Count > 0)
                {
                    return Failed(
                        "DELETE_STAGING_CONFLICT",
                        "检测到同一服务端的待清理目录，未移动当前运行目录。");
                }

                var stagingPath = Path.Combine(
                    _parentDirectory,
                    _stagingPrefix + commandId.ToString("N"));
                TransientFileSystem.MoveDirectory(
                    _serverDirectory,
                    stagingPath);
                TryDeleteRuntimeMarker();

                try
                {
                    DeleteTree(stagingPath);
                    return Succeeded(
                        "SERVER_FILES_DELETED",
                        "服务端运行目录及其中的世界、模组、插件和日志已永久删除；外置备份未改动。");
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException or
                        NotSupportedException)
                {
                    return Succeeded(
                        "SERVER_FILES_REMOVED_CLEANUP_PENDING",
                        AgentLog.Sanitize(
                            "服务端已从运行路径移除，部分暂存文件将由代理继续清理：" +
                            exception.Message,
                            1200));
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or
                    InvalidDataException or NotSupportedException)
            {
                return Failed(
                    "SERVER_DELETE_FAILED",
                    AgentLog.Sanitize(
                        "服务端目录未能安全移出运行路径：" + exception.Message,
                        1200));
            }
        }
    }

    internal (bool FilesPresent, bool CleanupPending) CaptureState()
    {
        var filesPresent = Directory.Exists(_serverDirectory);
        var pendingDirectories = TryFindPendingDirectories();
        var cleanupPending = pendingDirectories is null ||
            (filesPresent
                ? pendingDirectories.Count > 0
                : TryCleanupPendingDirectories(pendingDirectories));
        return (filesPresent, cleanupPending);
    }

    private bool TryCleanupPendingDirectories()
    {
        var pendingDirectories = TryFindPendingDirectories();
        return pendingDirectories is null ||
               TryCleanupPendingDirectories(pendingDirectories);
    }

    private static bool TryCleanupPendingDirectories(
        IReadOnlyList<string> pendingDirectories)
    {
        var cleanupPending = false;
        foreach (var path in pendingDirectories)
        {
            try
            {
                DeleteTree(path);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or
                    NotSupportedException)
            {
                cleanupPending = true;
            }
        }

        return cleanupPending;
    }

    private IReadOnlyList<string>? TryFindPendingDirectories()
    {
        if (!Directory.Exists(_parentDirectory))
        {
            return [];
        }

        try
        {
            return Directory.EnumerateDirectories(
                    _parentDirectory,
                    "*",
                    SearchOption.TopDirectoryOnly)
                .Where(path => Path.GetFileName(path).StartsWith(
                    _stagingPrefix,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void EnsureDeletionRootIsSafe()
    {
        var volumeRoot = Path.GetPathRoot(_serverDirectory)?.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(volumeRoot) ||
            string.Equals(
                _serverDirectory,
                volumeRoot,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The configured server directory cannot be a volume root.");
        }

        if ((File.GetAttributes(_serverDirectory) &
             FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                "The configured server directory cannot be a reparse point.");
        }
    }

    private void TryDeleteRuntimeMarker()
    {
        try
        {
            File.Delete(runtimeMarkerPath);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                NotSupportedException)
        {
            // A stale marker cannot make a missing directory launchable.
        }
    }

    private static void DeleteTree(string path)
    {
        var directory = new DirectoryInfo(path);
        if (!directory.Exists)
        {
            return;
        }

        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            directory.Delete();
            return;
        }

        foreach (var entry in directory.EnumerateFileSystemInfos())
        {
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                entry.Delete();
            }
            else if (entry is DirectoryInfo childDirectory)
            {
                DeleteTree(childDirectory.FullName);
            }
            else
            {
                entry.Attributes = FileAttributes.Normal;
                entry.Delete();
            }
        }

        directory.Attributes = FileAttributes.Normal;
        directory.Delete();
    }

    private static AgentCommandResult Succeeded(string code, string message) =>
        new(ServerControlCommandOutcome.Succeeded, code, message);

    private static AgentCommandResult Failed(string code, string message) =>
        new(ServerControlCommandOutcome.Failed, code, message);

    private static AgentCommandResult Conflict(string code, string message) =>
        new(ServerControlCommandOutcome.Conflict, code, message);
}

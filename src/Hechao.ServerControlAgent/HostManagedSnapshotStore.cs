namespace Hechao.ServerControlAgent;

internal sealed class HostManagedSnapshotStore(
    ServerControlTargetConfiguration configuration,
    string backupRoot)
{
    private readonly string snapshotDirectory = Path.Combine(
        Path.GetFullPath(backupRoot),
        "host-managed",
        configuration.ServerId);

    internal void CaptureFromServer()
    {
        if (configuration.HostManagedRelativePaths.Count == 0)
        {
            return;
        }

        var parent = Directory.GetParent(snapshotDirectory)?.FullName
            ?? throw new InvalidDataException(
                "The host-managed snapshot root is invalid.");
        Directory.CreateDirectory(parent);
        EnsureDirectoryIsSafe(parent);
        var staging = snapshotDirectory + ".staging-" + Guid.NewGuid().ToString("N");
        var previous = snapshotDirectory + ".previous-" + Guid.NewGuid().ToString("N");
        var previousMoved = false;
        try
        {
            Directory.CreateDirectory(staging);
            foreach (var relativePath in configuration.HostManagedRelativePaths)
            {
                var source = configuration.GetContainedDeploymentPath(relativePath);
                if (!PathExists(source))
                {
                    throw new InvalidDataException(
                        $"A required host-managed path is missing: {relativePath}");
                }

                CopyPath(source, GetContainedPath(staging, relativePath));
            }

            if (Directory.Exists(snapshotDirectory))
            {
                Directory.Move(snapshotDirectory, previous);
                previousMoved = true;
            }

            Directory.Move(staging, snapshotDirectory);
            if (previousMoved)
            {
                DeleteTree(previous);
            }
        }
        catch
        {
            TryDeleteTree(staging);
            if (previousMoved && !Directory.Exists(snapshotDirectory) &&
                Directory.Exists(previous))
            {
                Directory.Move(previous, snapshotDirectory);
            }

            throw;
        }
    }

    internal void EnsureAvailable()
    {
        if (configuration.HostManagedRelativePaths.Count == 0)
        {
            return;
        }

        EnsureDirectoryIsSafe(snapshotDirectory);
        foreach (var relativePath in configuration.HostManagedRelativePaths)
        {
            var source = GetContainedPath(snapshotDirectory, relativePath);
            if (!PathExists(source))
            {
                throw new InvalidDataException(
                    $"The host-managed snapshot is missing: {relativePath}");
            }

            EnsureTreeIsSafe(source);
        }
    }

    internal void CopyInto(string destinationRoot)
    {
        EnsureAvailable();
        foreach (var relativePath in configuration.HostManagedRelativePaths)
        {
            var source = GetContainedPath(snapshotDirectory, relativePath);
            var destination = GetContainedPath(destinationRoot, relativePath);
            DeletePath(destination);
            CopyPath(source, destination);
        }
    }

    private static void CopyPath(string source, string destination)
    {
        EnsureTreeIsSafe(source);
        if (Directory.Exists(source))
        {
            Directory.CreateDirectory(destination);
            foreach (var entry in Directory.EnumerateFileSystemEntries(source))
            {
                CopyPath(entry, Path.Combine(destination, Path.GetFileName(entry)));
            }

            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, overwrite: false);
    }

    private static void EnsureDirectoryIsSafe(string path)
    {
        if (!Directory.Exists(path) ||
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                "The host-managed snapshot directory is missing or unsafe.");
        }
    }

    private static void EnsureTreeIsSafe(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                "A host-managed path cannot contain a reparse point.");
        }

        if (Directory.Exists(path))
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(path))
            {
                EnsureTreeIsSafe(entry);
            }
        }
    }

    private static string GetContainedPath(string root, string relativePath)
    {
        var normalizedRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(normalizedRoot, relativePath));
        if (!path.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "A host-managed path escaped its snapshot directory.");
        }

        return path;
    }

    private static bool PathExists(string path) =>
        File.Exists(path) || Directory.Exists(path);

    private static void DeletePath(string path)
    {
        if (Directory.Exists(path))
        {
            DeleteTree(path);
        }
        else if (File.Exists(path))
        {
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
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
            throw new InvalidDataException(
                "A host-managed snapshot cannot be a reparse point.");
        }

        foreach (var entry in directory.EnumerateFileSystemInfos())
        {
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException(
                    "A host-managed snapshot cannot contain a reparse point.");
            }

            if (entry is DirectoryInfo child)
            {
                DeleteTree(child.FullName);
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

    private static void TryDeleteTree(string path)
    {
        try
        {
            DeleteTree(path);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                InvalidDataException or NotSupportedException)
        {
            // The caller will preserve the previous valid snapshot on failure.
        }
    }
}

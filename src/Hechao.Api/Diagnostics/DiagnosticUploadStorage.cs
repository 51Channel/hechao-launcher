using Microsoft.Extensions.Options;

namespace Hechao.Api.Diagnostics;

public sealed class DiagnosticUploadStorage
{
    private readonly string _storageRoot;

    public DiagnosticUploadStorage(IOptions<DiagnosticUploadOptions> options)
    {
        _storageRoot = Path.GetFullPath(options.Value.StorageRoot);
        Directory.CreateDirectory(_storageRoot);
        EnsureDirectoryIsSafe(_storageRoot);
    }

    public string GetTemporaryPath(Guid uploadId) =>
        GetContainedPath($"{uploadId:N}.part");

    public string GetArchivePath(Guid uploadId) =>
        GetContainedPath($"{uploadId:N}.zip");

    public FileStream CreateTemporaryFile(Guid uploadId)
    {
        var path = GetTemporaryPath(uploadId);
        return new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
    }

    public void Commit(Guid uploadId)
    {
        var source = GetTemporaryPath(uploadId);
        var destination = GetArchivePath(uploadId);
        File.Move(source, destination, overwrite: false);
    }

    public FileStream OpenRead(Guid uploadId) =>
        new(
            GetArchivePath(uploadId),
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

    public bool ArchiveExists(Guid uploadId) =>
        File.Exists(GetArchivePath(uploadId));

    public void Delete(Guid uploadId)
    {
        TryDelete(GetTemporaryPath(uploadId));
        TryDelete(GetArchivePath(uploadId));
    }

    public int DeleteOrphanedTemporaryFiles(DateTimeOffset olderThan)
    {
        var deleted = 0;
        foreach (var path in Directory.EnumerateFiles(
                     _storageRoot,
                     "*.part",
                     SearchOption.TopDirectoryOnly))
        {
            if (File.GetLastWriteTimeUtc(path) >= olderThan.UtcDateTime)
            {
                continue;
            }

            TryDelete(path);
            deleted++;
        }

        return deleted;
    }

    private string GetContainedPath(string fileName)
    {
        var path = Path.GetFullPath(Path.Combine(_storageRoot, fileName));
        var relative = Path.GetRelativePath(_storageRoot, path);
        if (Path.IsPathRooted(relative) ||
            relative == ".." ||
            relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Diagnostic storage path escaped its root.");
        }

        return path;
    }

    private static void EnsureDirectoryIsSafe(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException(
                "Diagnostic storage root must not be a reparse point.");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (FileNotFoundException)
        {
        }
        catch (DirectoryNotFoundException)
        {
        }
    }
}

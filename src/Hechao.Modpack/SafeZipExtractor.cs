using System.IO.Compression;
using System.Security.Cryptography;

namespace Hechao.Modpack;

public sealed record ExtractedArchiveFile(
    string RelativePath,
    long Size,
    string Sha256);

public static class SafeZipExtractor
{
    public static async Task<IReadOnlyList<ExtractedArchiveFile>> ExtractAsync(
        string archivePath,
        string destinationDirectory,
        ModpackInspectionLimits? limits = null,
        CancellationToken cancellationToken = default)
    {
        limits ??= new ModpackInspectionLimits();
        limits.Validate();
        var destination = Path.GetFullPath(destinationDirectory);
        if (Directory.Exists(destination) &&
            Directory.EnumerateFileSystemEntries(destination).Any())
        {
            throw new IOException("The archive extraction directory must be empty.");
        }

        Directory.CreateDirectory(destination);
        var results = new List<ExtractedArchiveFile>();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long expandedBytes = 0;
        using var archive = ZipFile.OpenRead(Path.GetFullPath(archivePath));
        if (archive.Entries.Count > limits.MaximumEntries)
        {
            throw new InvalidDataException("The archive contains too many entries.");
        }

        try
        {
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrEmpty(entry.Name))
                {
                    continue;
                }

                ValidateEntry(entry, limits, ref expandedBytes);
                var relativePath = SafeArchivePath.Normalize(
                    entry.FullName,
                    limits.MaximumPathLength);
                if (!paths.Add(relativePath))
                {
                    throw new InvalidDataException(
                        $"The archive contains a case-insensitive path collision: {relativePath}");
                }

                var destinationPath = SafeArchivePath.GetContainedPath(
                    destination,
                    relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                await using var input = entry.Open();
                await using var output = new FileStream(
                    destinationPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    128 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var copied = await CopyAndHashAsync(
                    input,
                    output,
                    hash,
                    entry.Length,
                    cancellationToken);
                results.Add(new ExtractedArchiveFile(
                    relativePath,
                    copied,
                    Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant()));
            }

            return results;
        }
        catch
        {
            TryDeleteDirectory(destination);
            throw;
        }
    }

    internal static void ValidateEntry(
        ZipArchiveEntry entry,
        ModpackInspectionLimits limits,
        ref long expandedBytes)
    {
        if (SafeArchivePath.IsSymbolicLink(entry.ExternalAttributes))
        {
            throw new InvalidDataException(
                $"Symbolic links are not allowed in modpack archives: {entry.FullName}");
        }

        if (entry.Length < 0 || entry.Length > limits.MaximumEntryBytes)
        {
            throw new InvalidDataException(
                $"Archive entry is too large: {entry.FullName}");
        }

        if (entry.CompressedLength == 0 && entry.Length > 0 ||
            entry.CompressedLength > 0 &&
            entry.Length / Math.Max(1, entry.CompressedLength) > limits.MaximumCompressionRatio &&
            entry.Length > 1024 * 1024)
        {
            throw new InvalidDataException(
                $"Archive entry has an unsafe compression ratio: {entry.FullName}");
        }

        expandedBytes = checked(expandedBytes + entry.Length);
        if (expandedBytes > limits.MaximumExpandedBytes)
        {
            throw new InvalidDataException("The expanded archive is too large.");
        }
    }

    internal static async Task<long> CopyAndHashAsync(
        Stream input,
        Stream output,
        IncrementalHash hash,
        long expectedLength,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[128 * 1024];
        long total = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            total = checked(total + read);
            if (total > expectedLength)
            {
                throw new InvalidDataException("Archive entry exceeded its declared size.");
            }

            hash.AppendData(buffer.AsSpan(0, read));
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        if (total != expectedLength)
        {
            throw new InvalidDataException("Archive entry size does not match its declaration.");
        }

        await output.FlushAsync(cancellationToken);
        return total;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

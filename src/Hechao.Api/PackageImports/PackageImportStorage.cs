using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Hechao.Contracts;
using Hechao.Modpack;
using Microsoft.Extensions.Options;

namespace Hechao.Api.PackageImports;

public sealed record CompletedPackageUpload(
    string Sha256,
    long Bytes);

public sealed class PackageImportStorage
{
    private static readonly JsonSerializerOptions AuditJsonOptions = new(
        JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly PackageImportOptions options;
    private readonly ILogger<PackageImportStorage> logger;
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> locks = new();

    public PackageImportStorage(
        IOptions<PackageImportOptions> options,
        ILogger<PackageImportStorage> logger)
    {
        this.options = options.Value;
        this.logger = logger;
        if (this.options.Enabled)
        {
            Directory.CreateDirectory(this.options.StorageRoot);
        }
    }

    public void Initialize(Guid importId)
    {
        EnsureEnabled();
        var directory = GetImportDirectory(importId);
        Directory.CreateDirectory(directory);
        using var stream = new FileStream(
            GetUploadingPath(importId),
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.Read);
    }

    public long GetUploadedBytes(Guid importId)
    {
        EnsureEnabled();
        var upload = GetUploadingPath(importId);
        if (File.Exists(upload))
        {
            return new FileInfo(upload).Length;
        }

        var completed = GetSourcePath(importId);
        return File.Exists(completed) ? new FileInfo(completed).Length : 0;
    }

    public async Task<long> AppendAsync(
        Guid importId,
        long expectedOffset,
        long expectedTotalBytes,
        Stream input,
        CancellationToken cancellationToken)
    {
        EnsureEnabled();
        var temporaryChunk = Path.Combine(
            GetImportDirectory(importId),
            $".{Guid.NewGuid():N}.chunk");
        try
        {
            var chunkBytes = await WriteLimitedChunkAsync(
                input,
                temporaryChunk,
                options.UploadChunkBytes,
                cancellationToken);
            if (chunkBytes == 0)
            {
                throw new InvalidDataException("Upload chunks cannot be empty.");
            }

            var gate = locks.GetOrAdd(importId, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(cancellationToken);
            try
            {
                var uploadPath = GetUploadingPath(importId);
                if (!File.Exists(uploadPath))
                {
                    throw new FileNotFoundException(
                        "The package upload is not available.",
                        uploadPath);
                }

                var currentLength = new FileInfo(uploadPath).Length;
                if (currentLength != expectedOffset)
                {
                    throw new PackageUploadOffsetException(currentLength);
                }

                var newLength = checked(currentLength + chunkBytes);
                if (newLength > expectedTotalBytes)
                {
                    throw new InvalidDataException(
                        "The upload exceeds its declared total size.");
                }

                await using var source = new FileStream(
                    temporaryChunk,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    128 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                await using var destination = new FileStream(
                    uploadPath,
                    FileMode.Open,
                    FileAccess.Write,
                    FileShare.Read,
                    128 * 1024,
                    FileOptions.Asynchronous | FileOptions.WriteThrough);
                destination.Position = currentLength;
                await source.CopyToAsync(destination, 128 * 1024, cancellationToken);
                await destination.FlushAsync(cancellationToken);
                return newLength;
            }
            finally
            {
                gate.Release();
            }
        }
        finally
        {
            File.Delete(temporaryChunk);
        }
    }

    public async Task<CompletedPackageUpload> CompleteUploadAsync(
        Guid importId,
        long expectedBytes,
        CancellationToken cancellationToken)
    {
        EnsureEnabled();
        var gate = locks.GetOrAdd(importId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var uploadPath = GetUploadingPath(importId);
            var completedPath = GetSourcePath(importId);
            if (File.Exists(completedPath))
            {
                var existing = new FileInfo(completedPath);
                if (existing.Length != expectedBytes)
                {
                    throw new InvalidDataException(
                        "The completed upload size does not match the task.");
                }

                return new CompletedPackageUpload(
                    await ComputeSha256Async(completedPath, cancellationToken),
                    existing.Length);
            }

            var file = new FileInfo(uploadPath);
            if (!file.Exists || file.Length != expectedBytes)
            {
                throw new InvalidDataException(
                    $"Upload is incomplete: received {(file.Exists ? file.Length : 0)} of {expectedBytes} bytes.");
            }

            try
            {
                using var archive = ZipFile.OpenRead(uploadPath);
                _ = archive.Entries.Count;
            }
            catch (InvalidDataException exception)
            {
                throw new InvalidDataException(
                    "The uploaded file is not a readable ZIP archive.",
                    exception);
            }

            var sha256 = await ComputeSha256Async(uploadPath, cancellationToken);
            File.Move(uploadPath, completedPath, overwrite: false);
            return new CompletedPackageUpload(sha256, expectedBytes);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<PackageImportAnalysisRecord> AnalyzeAsync(
        Guid importId,
        CancellationToken cancellationToken)
    {
        EnsureEnabled();
        var artifactDirectory = GetArtifactsDirectory(importId);
        if (Directory.Exists(artifactDirectory))
        {
            Directory.Delete(artifactDirectory, recursive: true);
        }

        Directory.CreateDirectory(artifactDirectory);
        var limits = new ModpackInspectionLimits
        {
            MaximumEntries = options.MaximumEntries,
            MaximumExpandedBytes = options.MaximumExpandedBytes,
            MaximumEntryBytes = options.MaximumEntryBytes,
            MaximumCompressionRatio = options.MaximumCompressionRatio
        };
        var result = await ModpackArchiveAnalyzer.AnalyzeAndSplitAsync(
            GetSourcePath(importId),
            artifactDirectory,
            limits,
            cancellationToken);
        var summary = Map(result);
        var audit = new
        {
            summary,
            files = result.Files.Select(file => new
            {
                file.SourcePath,
                file.TargetPath,
                side = file.Side.ToString(),
                file.Size,
                file.Sha256
            })
        };
        await WriteAtomicallyAsync(
            Path.Combine(artifactDirectory, "analysis.json"),
            JsonSerializer.SerializeToUtf8Bytes(audit, AuditJsonOptions),
            cancellationToken);
        return summary;
    }

    public FileStream OpenClientArchive(Guid importId) =>
        OpenArtifact(importId, "client.zip");

    public FileStream OpenServerArchive(Guid importId) =>
        OpenArtifact(importId, "server.zip");

    public bool ServerArchiveExists(Guid importId)
    {
        EnsureEnabled();
        var path = Path.Combine(GetArtifactsDirectory(importId), "server.zip");
        return File.Exists(path) &&
               (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0;
    }

    public void Delete(Guid importId)
    {
        EnsureEnabled();
        var directory = GetImportDirectory(importId);
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }

        if (locks.TryRemove(importId, out var gate))
        {
            gate.Dispose();
        }
    }

    private PackageImportAnalysisRecord Map(ModpackAnalysisResult result)
    {
        var bySource = result.Files
            .GroupBy(file => file.SourcePath, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                Path = group.Key,
                Records = group.ToArray(),
                Shared = group.Select(file => file.Side).Distinct().Count() > 1
            })
            .OrderBy(item => item.Path, StringComparer.Ordinal)
            .ToArray();
        var samples = bySource
            .Take(200)
            .Select(item =>
            {
                var file = item.Records[0];
                return new PackageImportFileSampleRecord(
                    item.Path,
                    item.Shared ? "Shared" : file.Side.ToString(),
                    file.Size,
                    file.Sha256);
            })
            .ToArray();
        return new PackageImportAnalysisRecord(
            result.Layout.ToString(),
            new PackageImportDetectedMetadataRecord(
                result.Metadata.SuggestedProfileId,
                result.Metadata.DisplayName,
                result.Metadata.Version,
                result.Metadata.MinecraftVersion,
                result.Metadata.JavaMajorVersion,
                result.Metadata.Loader,
                result.Metadata.LoaderVersion,
                result.Metadata.MaximumPlayers,
                result.Metadata.ServerLaunchPath),
            MapPart(result.Client),
            MapPart(result.Server),
            bySource.Count(item => !item.Shared &&
                item.Records[0].Side == ModpackFileSide.Client),
            bySource.Count(item => !item.Shared &&
                item.Records[0].Side == ModpackFileSide.Server),
            bySource.Count(item => item.Shared),
            samples,
            result.Issues.Select(issue => new PackageImportIssueRecord(
                issue.Code,
                Enum.Parse<PackageImportIssueSeverity>(issue.Severity.ToString()),
                issue.Message,
                issue.Path)).ToArray());
    }

    private static PackageImportPartRecord? MapPart(ModpackArchivePart? part) =>
        part is null
            ? null
            : new PackageImportPartRecord(
                part.Sha256,
                part.ArchiveBytes,
                part.ExpandedBytes,
                part.FileCount);

    private FileStream OpenArtifact(Guid importId, string fileName)
    {
        var path = Path.Combine(GetArtifactsDirectory(importId), fileName);
        return new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
    }

    private string GetImportDirectory(Guid importId)
    {
        var root = Path.GetFullPath(options.StorageRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        var directory = Path.GetFullPath(Path.Combine(root, importId.ToString("N")));
        if (!directory.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The package import directory escapes its configured storage root.");
        }

        return directory;
    }

    private void EnsureEnabled()
    {
        if (!options.Enabled)
        {
            throw new InvalidOperationException("Package imports are disabled.");
        }
    }

    private string GetUploadingPath(Guid importId) =>
        Path.Combine(GetImportDirectory(importId), "source.upload");

    private string GetSourcePath(Guid importId) =>
        Path.Combine(GetImportDirectory(importId), "source.zip");

    private string GetArtifactsDirectory(Guid importId) =>
        Path.Combine(GetImportDirectory(importId), "artifacts");

    private static async Task<long> WriteLimitedChunkAsync(
        Stream input,
        string path,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        await using var output = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
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
            if (total > maximumBytes)
            {
                throw new InvalidDataException(
                    $"Upload chunks cannot exceed {maximumBytes} bytes.");
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        await output.FlushAsync(cancellationToken);
        return total;
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(
            await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
    }

    private static async Task WriteAtomicallyAsync(
        string path,
        byte[] content,
        CancellationToken cancellationToken)
    {
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, content, cancellationToken);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }
}

public sealed class PackageUploadOffsetException(long actualOffset)
    : IOException("The upload offset does not match the stored file.")
{
    public long ActualOffset { get; } = actualOffset;
}

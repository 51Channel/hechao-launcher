using System.Security.Cryptography;
using Hechao.Distribution;

internal sealed record ClientDistributionBuildOptions(
    string SourceDirectory,
    string OutputDirectory,
    string ProfileId,
    string Version,
    string MinecraftVersion,
    string JavaVersion,
    string Loader,
    string LoaderVersion,
    string KeyId,
    SigningKeyInput SigningKey,
    Uri ObjectBaseUri,
    DateTimeOffset PublishedAt,
    IReadOnlyList<string> DeletePaths);

internal sealed record ClientDistributionBuildResult(
    string ProfileId,
    string Version,
    int FileCount,
    long TotalBytes,
    string ManifestPath,
    string ManifestSha256);

internal static class ClientDistributionBuilder
{
    internal static async Task<ClientDistributionBuildResult> BuildAsync(
        ClientDistributionBuildOptions options,
        CancellationToken cancellationToken = default)
    {
        var sourceDirectory = Path.GetFullPath(options.SourceDirectory);
        var outputDirectory = Path.GetFullPath(options.OutputDirectory);
        if (!Directory.Exists(sourceDirectory))
        {
            throw new PublisherUsageException(
                $"Source directory does not exist: {sourceDirectory}");
        }

        if (!File.Exists(options.SigningKey.Path))
        {
            throw new PublisherUsageException(
                $"Private key does not exist: {options.SigningKey.Path}");
        }

        if (IsWithin(sourceDirectory, outputDirectory) ||
            IsWithin(sourceDirectory, options.SigningKey.Path))
        {
            throw new PublisherUsageException(
                "Output directories and private keys must not be placed inside the client source directory.");
        }

        ManifestValidator.ValidateProfileId(options.ProfileId);
        var files = new List<ClientManifestFile>();
        long totalBytes = 0;
        foreach (var filePath in EnumerateSourceFiles(sourceDirectory)
                     .OrderBy(
                         path => Path.GetRelativePath(sourceDirectory, path).Replace('\\', '/'),
                         StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(sourceDirectory, filePath).Replace('\\', '/');
            ManifestValidator.ValidateManagedPath(relativePath);
            var file = new FileInfo(filePath);
            var digest = await FileHashing.ComputeSha256Async(filePath);
            var objectRelativePath = $"objects/{digest[..2]}/{digest}";
            var objectPath = Path.Combine(
                outputDirectory,
                objectRelativePath.Replace('/', Path.DirectorySeparatorChar));
            await CopyObjectAsync(
                filePath,
                objectPath,
                file.Length,
                digest,
                cancellationToken);
            files.Add(new ClientManifestFile(
                relativePath,
                file.Length,
                digest,
                new Uri(options.ObjectBaseUri, objectRelativePath).AbsoluteUri,
                Required: true));
            totalBytes = checked(totalBytes + file.Length);
        }

        if (files.Count == 0)
        {
            throw new PublisherUsageException("The client source directory is empty.");
        }

        var manifest = new ClientManifest(
            ManifestValidator.CurrentSchemaVersion,
            options.ProfileId,
            options.Version,
            options.MinecraftVersion,
            options.JavaVersion,
            options.Loader,
            options.LoaderVersion,
            options.PublishedAt.ToUniversalTime(),
            files,
            options.DeletePaths);
        using var signingKey = options.SigningKey.Load();
        var envelope = SignedManifestCodec.Sign(manifest, options.KeyId, signingKey);
        var envelopeBytes = ManifestJson.SerializeEnvelope(envelope);
        var manifestDirectory = Path.Combine(outputDirectory, "manifests");
        var manifestPath = Path.Combine(
            manifestDirectory,
            options.ProfileId + ".json");
        Directory.CreateDirectory(manifestDirectory);
        await WriteAtomicallyAsync(manifestPath, envelopeBytes, cancellationToken);
        var envelopeDigest = Convert.ToHexString(
            SHA256.HashData(envelopeBytes)).ToLowerInvariant();
        return new ClientDistributionBuildResult(
            options.ProfileId,
            options.Version,
            files.Count,
            totalBytes,
            manifestPath,
            envelopeDigest);
    }

    private static IEnumerable<string> EnumerateSourceFiles(string sourceDirectory)
    {
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(sourceDirectory));
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException(
                    $"Symbolic links and reparse points are not allowed: {directory.FullName}");
            }

            foreach (var childDirectory in directory.EnumerateDirectories()
                         .OrderBy(item => item.Name, StringComparer.Ordinal))
            {
                pending.Push(childDirectory);
            }

            foreach (var file in directory.EnumerateFiles()
                         .OrderBy(item => item.Name, StringComparer.Ordinal))
            {
                if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException(
                        $"Symbolic links and reparse points are not allowed: {file.FullName}");
                }

                yield return file.FullName;
            }
        }
    }

    private static async Task CopyObjectAsync(
        string sourcePath,
        string objectPath,
        long expectedSize,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        if (await FileHashing.MatchesAsync(objectPath, expectedSize, expectedSha256))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(objectPath)!);
        var temporaryPath = objectPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var source = new FileStream(
                             sourcePath,
                             FileMode.Open,
                             FileAccess.Read,
                             FileShare.Read,
                             128 * 1024,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var destination = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             128 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await source.CopyToAsync(destination, 128 * 1024, cancellationToken);
                await destination.FlushAsync(cancellationToken);
            }

            if (!await FileHashing.MatchesAsync(
                    temporaryPath,
                    expectedSize,
                    expectedSha256))
            {
                throw new ManifestIntegrityException(
                    $"Object verification failed after copying {sourcePath}.");
            }

            File.Move(temporaryPath, objectPath, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static async Task WriteAtomicallyAsync(
        string path,
        byte[] content,
        CancellationToken cancellationToken)
    {
        var temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
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

    private static bool IsWithin(string rootPath, string candidatePath)
    {
        var root = Path.GetFullPath(rootPath);
        var candidate = Path.GetFullPath(candidatePath);
        if (string.Equals(root, candidate, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var rootPrefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        return candidate.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase);
    }
}

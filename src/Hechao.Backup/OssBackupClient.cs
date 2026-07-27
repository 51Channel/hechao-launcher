using System.Security.Cryptography;
using AlibabaCloud.OSS.V2;
using AlibabaCloud.OSS.V2.Credentials;
using AlibabaCloud.OSS.V2.Models;

namespace Hechao.Backup;

internal sealed record OssBackupUploadResult(
    string Key,
    long Length,
    string Sha256,
    bool Uploaded);

internal sealed class OssBackupClient : IDisposable
{
    private readonly Client client;

    internal OssBackupClient(
        string accessKeyId,
        string accessKeySecret,
        string region,
        string endpoint)
    {
        if (string.IsNullOrWhiteSpace(accessKeyId) ||
            string.IsNullOrWhiteSpace(accessKeySecret))
        {
            throw new ArgumentException(
                "OSS_ACCESS_KEY_ID and OSS_ACCESS_KEY_SECRET are required.");
        }

        var configuration = Configuration.LoadDefault();
        configuration.CredentialsProvider = new StaticCredentialsProvider(
            accessKeyId,
            accessKeySecret);
        configuration.Region = ValidateSimpleName(region, "region");
        configuration.Endpoint = ValidateEndpoint(endpoint);
        configuration.UserAgent = "hechao-backup/0.1.0";
        client = new Client(configuration);
    }

    internal static OssBackupClient FromEnvironment(
        string region,
        string endpoint) =>
        new(
            Environment.GetEnvironmentVariable("OSS_ACCESS_KEY_ID") ?? string.Empty,
            Environment.GetEnvironmentVariable("OSS_ACCESS_KEY_SECRET") ?? string.Empty,
            region,
            endpoint);

    internal async Task<OssBackupUploadResult> UploadAsync(
        string bucket,
        string key,
        string inputPath,
        CancellationToken cancellationToken)
    {
        bucket = ValidateSimpleName(bucket, "bucket");
        key = ValidateObjectKey(key);
        var input = new FileInfo(Path.GetFullPath(inputPath));
        if (!input.Exists ||
            input.Length <= 0 ||
            (input.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("A non-empty regular backup file is required.");
        }

        var sha256 = BackupEnvelope.ComputeSha256(input.FullName);
        var existing = await HeadAsync(
            bucket,
            key,
            cancellationToken);
        if (existing is not null)
        {
            ValidateRemote(existing, input.Length, sha256, key);
            return new OssBackupUploadResult(
                key,
                input.Length,
                sha256,
                Uploaded: false);
        }

        var contentMd5 = ComputeMd5(input.FullName);
        await using var body = input.OpenRead();
        await client.PutObjectAsync(
            new PutObjectRequest
            {
                Bucket = bucket,
                Key = key,
                Body = body,
                ContentLength = input.Length,
                ContentMd5 = contentMd5,
                ContentType = "application/octet-stream",
                CacheControl = "private, no-store",
                Acl = "private",
                ForbidOverwrite = true,
                Metadata = new Dictionary<string, string>(
                    StringComparer.Ordinal)
                {
                    ["sha256"] = sha256,
                    ["format"] = "hcbackup-1"
                }
            },
            cancellationToken: cancellationToken);

        var uploaded = await HeadAsync(
            bucket,
            key,
            cancellationToken)
            ?? throw new IOException(
                "OSS backup upload completed without a readable object.");
        ValidateRemote(uploaded, input.Length, sha256, key);
        return new OssBackupUploadResult(
            key,
            input.Length,
            sha256,
            Uploaded: true);
    }

    internal async Task<string> DownloadAsync(
        string bucket,
        string key,
        string outputPath,
        string? expectedSha256,
        CancellationToken cancellationToken)
    {
        bucket = ValidateSimpleName(bucket, "bucket");
        key = ValidateObjectKey(key);
        var output = Path.GetFullPath(outputPath);
        if (File.Exists(output) || Directory.Exists(output))
        {
            throw new IOException($"Output already exists: {output}");
        }

        Directory.CreateDirectory(
            Path.GetDirectoryName(output)
            ?? throw new IOException("Output path has no parent directory."));
        var temporaryOutput = $"{output}.tmp-{Guid.NewGuid():N}";
        try
        {
            var result = await client.GetObjectAsync(
                new GetObjectRequest
                {
                    Bucket = bucket,
                    Key = key
                },
                cancellationToken: cancellationToken);
            await using var body = result.Body
                ?? throw new IOException("OSS returned an empty response body.");
            await using (var destination = new FileStream(
                temporaryOutput,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1024 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await body.CopyToAsync(destination, cancellationToken);
                await destination.FlushAsync(cancellationToken);
                destination.Flush(flushToDisk: true);
            }

            var downloadedSha256 = BackupEnvelope.ComputeSha256(
                temporaryOutput);
            var metadataSha256 = FindMetadata(result.Metadata, "sha256");
            if (!string.IsNullOrWhiteSpace(metadataSha256) &&
                !string.Equals(
                    downloadedSha256,
                    metadataSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new CryptographicException(
                    "Downloaded OSS backup does not match its SHA-256 metadata.");
            }

            if (!string.IsNullOrWhiteSpace(expectedSha256) &&
                !string.Equals(
                    downloadedSha256,
                    NormalizeSha256(expectedSha256),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new CryptographicException(
                    "Downloaded OSS backup does not match the expected SHA-256.");
            }

            File.Move(temporaryOutput, output);
            return downloadedSha256;
        }
        catch
        {
            TryDelete(temporaryOutput);
            throw;
        }
    }

    private async Task<HeadObjectResult?> HeadAsync(
        string bucket,
        string key,
        CancellationToken cancellationToken)
    {
        try
        {
            return await client.HeadObjectAsync(
                new HeadObjectRequest
                {
                    Bucket = bucket,
                    Key = key
                },
                cancellationToken: cancellationToken);
        }
        catch (Exception exception) when (IsNotFound(exception))
        {
            return null;
        }
    }

    private static void ValidateRemote(
        HeadObjectResult remote,
        long expectedLength,
        string expectedSha256,
        string key)
    {
        var remoteSha256 = FindMetadata(remote.Metadata, "sha256");
        if (remote.ContentLength != expectedLength ||
            !string.Equals(
                remoteSha256,
                expectedSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException(
                $"OSS object {key} already exists with different content. " +
                "Refusing to overwrite an immutable backup.");
        }
    }

    internal static string ValidateObjectKey(string key)
    {
        key = key.Trim();
        if (key.Length is < 3 or > 900 ||
            key.StartsWith('/') ||
            key.EndsWith('/') ||
            key.Contains('\\') ||
            key.Contains("//", StringComparison.Ordinal) ||
            key.Split('/').Any(segment =>
                segment is "." or ".." || segment.Length == 0) ||
            key.Any(char.IsControl))
        {
            throw new ArgumentException("OSS backup object key is invalid.");
        }

        return key;
    }

    private static string ValidateSimpleName(string value, string name)
    {
        value = value.Trim();
        if (value.Length is < 3 or > 100 ||
            !value.All(character =>
                char.IsAsciiLetterOrDigit(character) ||
                character is '-' or '_'))
        {
            throw new ArgumentException($"{name} is invalid.");
        }

        return value;
    }

    private static string ValidateEndpoint(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint) ||
            !string.Equals(
                endpoint.Scheme,
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(endpoint.UserInfo) ||
            !string.IsNullOrEmpty(endpoint.Query) ||
            !string.IsNullOrEmpty(endpoint.Fragment))
        {
            throw new ArgumentException("OSS endpoint must be an HTTPS URL.");
        }

        return endpoint.AbsoluteUri.TrimEnd('/');
    }

    private static string ComputeMd5(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToBase64String(MD5.HashData(stream));
    }

    private static string? FindMetadata(
        IEnumerable<KeyValuePair<string, string>>? metadata,
        string name) =>
        metadata?
            .FirstOrDefault(entry =>
                string.Equals(
                    entry.Key,
                    name,
                    StringComparison.OrdinalIgnoreCase))
            .Value;

    private static string NormalizeSha256(string value)
    {
        value = value.Trim();
        if (value.Length != 64 || !value.All(Uri.IsHexDigit))
        {
            throw new ArgumentException("Expected SHA-256 is invalid.");
        }

        return value.ToUpperInvariant();
    }

    private static bool IsNotFound(Exception exception)
    {
        for (Exception? current = exception;
             current is not null;
             current = current.InnerException)
        {
            if (current is ServiceException serviceException &&
                serviceException.StatusCode == 404 &&
                serviceException.ErrorCode is
                    "NoSuchKey" or "NoSuchObject" or "NotFound")
            {
                return true;
            }
        }

        return false;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // Preserve the original failure.
        }
    }

    public void Dispose() => client.Dispose();
}

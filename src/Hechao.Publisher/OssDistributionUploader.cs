using System.Collections.Concurrent;
using System.Security.Cryptography;
using AlibabaCloud.OSS.V2;
using AlibabaCloud.OSS.V2.Credentials;
using AlibabaCloud.OSS.V2.Models;

internal sealed record OssUploadOptions(
    string DistributionDirectory,
    string Bucket,
    string Region,
    string Endpoint,
    string ObjectPrefix,
    string CredentialPath,
    string CredentialEntropyLabel,
    int Parallelism);

internal sealed record OssUploadResult(
    int Uploaded,
    int AlreadyPresent,
    long UploadedBytes);

internal sealed class OssDistributionUploader(OssUploadOptions options)
{
    public async Task<OssUploadResult> UploadAsync(CancellationToken cancellationToken)
    {
        var objects = ValidateAndEnumerateObjects(options.DistributionDirectory);
        var credential = OssCredentialStore.Load(
            options.CredentialPath,
            options.CredentialEntropyLabel);
        var configuration = Configuration.LoadDefault();
        configuration.CredentialsProvider = new StaticCredentialsProvider(
            credential.AccessKeyId,
            credential.AccessKeySecret);
        configuration.Region = ValidateSimpleName(options.Region, "region");
        configuration.Endpoint = ValidateHttpsEndpoint(options.Endpoint);
        configuration.UserAgent = PublisherProductInfo.UserAgent;
        using var client = new Client(configuration);

        var uploaded = 0;
        var alreadyPresent = 0;
        long uploadedBytes = 0;
        var failures = new ConcurrentQueue<Exception>();
        await Parallel.ForEachAsync(
            objects,
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = options.Parallelism
            },
            async (item, token) =>
            {
                try
                {
                    var contentMd5 = await ComputeContentMd5Async(item.Path, token);
                    var request = new PutObjectRequest
                    {
                        Bucket = ValidateSimpleName(options.Bucket, "bucket"),
                        Key = BuildObjectKey(options.ObjectPrefix, item.Digest),
                        Body = File.OpenRead(item.Path),
                        ContentLength = item.Length,
                        ContentMd5 = contentMd5,
                        ContentType = "application/octet-stream",
                        CacheControl = "public, max-age=31536000, immutable",
                        ForbidOverwrite = true,
                        Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["sha256"] = item.Digest
                        }
                    };
                    try
                    {
                        await client.PutObjectAsync(request, cancellationToken: token);
                        Interlocked.Increment(ref uploaded);
                        Interlocked.Add(ref uploadedBytes, item.Length);
                    }
                    catch (ServiceException exception) when (
                        exception.StatusCode == 409 &&
                        exception.ErrorCode is "FileAlreadyExists" or "ObjectAlreadyExists")
                    {
                        Interlocked.Increment(ref alreadyPresent);
                    }
                    finally
                    {
                        request.Body?.Dispose();
                    }

                    var completed = Volatile.Read(ref uploaded) +
                                    Volatile.Read(ref alreadyPresent);
                    if (completed % 100 == 0 || completed == objects.Count)
                    {
                        Console.WriteLine(
                            $"OSS progress: {completed}/{objects.Count} " +
                            $"uploaded={Volatile.Read(ref uploaded)} " +
                            $"existing={Volatile.Read(ref alreadyPresent)}");
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    failures.Enqueue(new IOException(
                        $"Unable to upload immutable object {item.Digest}.",
                        exception));
                }
            });

        if (!failures.IsEmpty)
        {
            throw new AggregateException(
                $"OSS upload failed for {failures.Count} object(s).",
                failures);
        }

        return new OssUploadResult(uploaded, alreadyPresent, uploadedBytes);
    }

    internal static IReadOnlyList<DistributionObject> ValidateAndEnumerateObjects(
        string distributionDirectory)
    {
        var root = Path.GetFullPath(distributionDirectory);
        var rootInfo = new DirectoryInfo(root);
        var objectRoot = new DirectoryInfo(Path.Combine(root, "objects"));
        if (!rootInfo.Exists ||
            !objectRoot.Exists ||
            (rootInfo.Attributes & FileAttributes.ReparsePoint) != 0 ||
            (objectRoot.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new PublisherUsageException("The distribution object directory is invalid.");
        }

        var objects = new List<DistributionObject>();
        foreach (var prefixDirectory in objectRoot.EnumerateDirectories())
        {
            if ((prefixDirectory.Attributes & FileAttributes.ReparsePoint) != 0 ||
                prefixDirectory.Name.Length != 2 ||
                !prefixDirectory.Name.All(Uri.IsHexDigit))
            {
                throw new PublisherUsageException(
                    $"Invalid distribution object prefix: {prefixDirectory.Name}");
            }

            foreach (var file in prefixDirectory.EnumerateFiles())
            {
                if ((file.Attributes & FileAttributes.ReparsePoint) != 0 ||
                    file.Name.Length != 64 ||
                    !file.Name.All(Uri.IsHexDigit) ||
                    !file.Name.StartsWith(
                        prefixDirectory.Name,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new PublisherUsageException(
                        $"Invalid distribution object name: {file.Name}");
                }

                var expectedDigest = file.Name.ToLowerInvariant();
                using var objectStream = new FileStream(
                    file.FullName,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 1024 * 1024,
                    FileOptions.SequentialScan);
                var actualDigest = Convert.ToHexString(
                    SHA256.HashData(objectStream)).ToLowerInvariant();
                if (!string.Equals(expectedDigest, actualDigest, StringComparison.Ordinal))
                {
                    throw new PublisherUsageException(
                        $"Distribution object digest mismatch: {file.Name}");
                }

                objects.Add(new DistributionObject(
                    file.FullName,
                    expectedDigest,
                    file.Length));
            }
        }

        if (objects.Count == 0)
        {
            throw new PublisherUsageException("The distribution contains no objects.");
        }

        return objects
            .OrderBy(item => item.Digest, StringComparer.Ordinal)
            .ToArray();
    }

    private static async Task<string> ComputeContentMd5Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var digest = await MD5.HashDataAsync(stream, cancellationToken);
        return Convert.ToBase64String(digest);
    }

    private static string BuildObjectKey(string objectPrefix, string digest)
    {
        var normalizedPrefix = objectPrefix.Trim('/');
        if (string.IsNullOrWhiteSpace(normalizedPrefix) ||
            normalizedPrefix.Contains('\\') ||
            normalizedPrefix.Split('/').Any(segment => segment is "" or "." or ".."))
        {
            throw new PublisherUsageException("The OSS object prefix is invalid.");
        }

        return $"{normalizedPrefix}/{digest[..2]}/{digest}";
    }

    private static string ValidateSimpleName(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > 128 ||
            !value.All(character =>
                character is >= 'a' and <= 'z' or
                    >= '0' and <= '9' or '-'))
        {
            throw new PublisherUsageException($"The OSS {fieldName} is invalid.");
        }

        return value;
    }

    private static string ValidateHttpsEndpoint(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint) ||
            !string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(endpoint.Query) ||
            !string.IsNullOrEmpty(endpoint.Fragment) ||
            endpoint.AbsolutePath is not "/")
        {
            throw new PublisherUsageException("The OSS endpoint must be an HTTPS origin.");
        }

        return endpoint.AbsoluteUri.TrimEnd('/');
    }
}

internal sealed record DistributionObject(
    string Path,
    string Digest,
    long Length);

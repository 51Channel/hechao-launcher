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

internal sealed record OssRemoteObject(
    long ContentLength,
    IReadOnlyDictionary<string, string> Metadata);

internal static class OssServiceExceptionClassifier
{
    public static bool Matches(
        Exception exception,
        int statusCode,
        params string[] errorCodes)
    {
        var serviceException = Find(exception);
        return serviceException?.StatusCode == statusCode &&
               errorCodes.Contains(
                   serviceException.ErrorCode,
                   StringComparer.Ordinal);
    }

    public static bool ContainsServiceException(Exception exception) =>
        Find(exception) is not null;

    private static ServiceException? Find(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is ServiceException serviceException)
            {
                return serviceException;
            }
        }

        return null;
    }
}

internal interface IOssObjectStore : IDisposable
{
    Task<OssRemoteObject?> HeadAsync(
        string bucket,
        string key,
        CancellationToken cancellationToken);

    Task PutAsync(
        string bucket,
        string key,
        string path,
        long length,
        string contentMd5,
        string sha256,
        CancellationToken cancellationToken);
}

internal sealed class AlibabaOssObjectStore : IOssObjectStore
{
    private readonly Client client;

    public AlibabaOssObjectStore(
        OssCredential credential,
        string region,
        string endpoint)
    {
        var configuration = Configuration.LoadDefault();
        configuration.CredentialsProvider = new StaticCredentialsProvider(
            credential.AccessKeyId,
            credential.AccessKeySecret);
        configuration.Region = region;
        configuration.Endpoint = endpoint;
        configuration.UserAgent = PublisherProductInfo.UserAgent;
        client = new Client(configuration);
    }

    public async Task<OssRemoteObject?> HeadAsync(
        string bucket,
        string key,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await client.HeadObjectAsync(
                new HeadObjectRequest
                {
                    Bucket = bucket,
                    Key = key
                },
                cancellationToken: cancellationToken);
            return new OssRemoteObject(
                result.ContentLength ?? -1,
                new Dictionary<string, string>(
                    result.Metadata ?? new Dictionary<string, string>(),
                    StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception exception) when (
            OssServiceExceptionClassifier.Matches(
                exception,
                404,
                "NoSuchKey",
                "NoSuchObject",
                "NotFound"))
        {
            return null;
        }
    }

    public async Task PutAsync(
        string bucket,
        string key,
        string path,
        long length,
        string contentMd5,
        string sha256,
        CancellationToken cancellationToken)
    {
        using var body = File.OpenRead(path);
        await client.PutObjectAsync(
            new PutObjectRequest
            {
                Bucket = bucket,
                Key = key,
                Body = body,
                ContentLength = length,
                ContentMd5 = contentMd5,
                ContentType = "application/octet-stream",
                CacheControl = "public, max-age=31536000, immutable",
                ForbidOverwrite = true,
                Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["sha256"] = sha256
                }
            },
            cancellationToken: cancellationToken);
    }

    public void Dispose()
    {
        client.Dispose();
    }
}

internal sealed class OssDistributionUploader
{
    private readonly OssUploadOptions options;
    private readonly IOssObjectStore? objectStore;

    public OssDistributionUploader(OssUploadOptions options)
        : this(options, objectStore: null)
    {
    }

    internal OssDistributionUploader(
        OssUploadOptions options,
        IOssObjectStore? objectStore)
    {
        this.options = options;
        this.objectStore = objectStore;
    }

    public async Task<OssUploadResult> UploadAsync(CancellationToken cancellationToken)
    {
        var objects = ValidateAndEnumerateObjects(options.DistributionDirectory);
        var bucket = ValidateSimpleName(options.Bucket, "bucket");
        var region = ValidateSimpleName(options.Region, "region");
        var endpoint = ValidateHttpsEndpoint(options.Endpoint);
        using var store = objectStore ?? CreateObjectStore(region, endpoint);

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
                    var key = BuildObjectKey(options.ObjectPrefix, item.Digest);
                    var remoteObject = await store.HeadAsync(bucket, key, token);
                    if (remoteObject is not null)
                    {
                        ValidateRemoteObject(key, item, remoteObject);
                        Interlocked.Increment(ref alreadyPresent);
                        ReportProgress(
                            objects.Count,
                            uploaded,
                            alreadyPresent);
                        return;
                    }

                    var contentMd5 = await ComputeContentMd5Async(item.Path, token);
                    try
                    {
                        await store.PutAsync(
                            bucket,
                            key,
                            item.Path,
                            item.Length,
                            contentMd5,
                            item.Digest,
                            token);
                        var uploadedObject = await store.HeadAsync(
                            bucket,
                            key,
                            token);
                        if (uploadedObject is null)
                        {
                            throw new IOException(
                                $"OSS object {key} was not visible after upload.");
                        }

                        ValidateRemoteObject(key, item, uploadedObject);
                        Interlocked.Increment(ref uploaded);
                        Interlocked.Add(ref uploadedBytes, item.Length);
                    }
                    catch (Exception exception) when (
                        OssServiceExceptionClassifier.Matches(
                            exception,
                            409,
                            "FileAlreadyExists",
                            "ObjectAlreadyExists"))
                    {
                        var concurrentObject = await store.HeadAsync(
                            bucket,
                            key,
                            token);
                        if (concurrentObject is null)
                        {
                            throw new IOException(
                                $"OSS reported that {key} already exists, but its " +
                                "metadata could not be retrieved.");
                        }

                        ValidateRemoteObject(key, item, concurrentObject);
                        Interlocked.Increment(ref alreadyPresent);
                    }

                    ReportProgress(
                        objects.Count,
                        uploaded,
                        alreadyPresent);
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

    private IOssObjectStore CreateObjectStore(string region, string endpoint)
    {
        var credential = OssCredentialStore.Load(
            options.CredentialPath,
            options.CredentialEntropyLabel);
        return new AlibabaOssObjectStore(credential, region, endpoint);
    }

    private static void ValidateRemoteObject(
        string key,
        DistributionObject localObject,
        OssRemoteObject remoteObject)
    {
        if (remoteObject.ContentLength != localObject.Length)
        {
            throw new IOException(
                $"OSS object {key} already exists with length " +
                $"{remoteObject.ContentLength}, expected {localObject.Length}. " +
                "Refusing to overwrite it.");
        }

        var remoteDigest = remoteObject.Metadata
            .FirstOrDefault(entry =>
                string.Equals(
                    entry.Key,
                    "sha256",
                    StringComparison.OrdinalIgnoreCase))
            .Value;
        if (!string.Equals(
                remoteDigest,
                localObject.Digest,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException(
                $"OSS object {key} already exists with SHA-256 metadata " +
                $"'{remoteDigest ?? "<missing>"}', expected {localObject.Digest}. " +
                "Refusing to overwrite it.");
        }
    }

    private static void ReportProgress(
        int total,
        int uploaded,
        int alreadyPresent)
    {
        var completed = Volatile.Read(ref uploaded) +
                        Volatile.Read(ref alreadyPresent);
        if (completed % 100 == 0 || completed == total)
        {
            Console.WriteLine(
                $"OSS progress: {completed}/{total} " +
                $"uploaded={Volatile.Read(ref uploaded)} " +
                $"existing={Volatile.Read(ref alreadyPresent)}");
        }
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
        foreach (var prefixEntry in objectRoot.EnumerateFileSystemInfos())
        {
            if (prefixEntry is not DirectoryInfo prefixDirectory)
            {
                throw new PublisherUsageException(
                    $"Unexpected entry in the distribution object root: {prefixEntry.Name}");
            }

            if ((prefixDirectory.Attributes & FileAttributes.ReparsePoint) != 0 ||
                prefixDirectory.Name.Length != 2 ||
                !prefixDirectory.Name.All(Uri.IsHexDigit))
            {
                throw new PublisherUsageException(
                    $"Invalid distribution object prefix: {prefixDirectory.Name}");
            }

            foreach (var objectEntry in prefixDirectory.EnumerateFileSystemInfos())
            {
                if (objectEntry is not FileInfo file)
                {
                    throw new PublisherUsageException(
                        $"Unexpected entry in distribution object prefix {prefixDirectory.Name}: " +
                        objectEntry.Name);
                }

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

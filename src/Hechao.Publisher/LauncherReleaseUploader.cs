using System.Security.Cryptography;
using AlibabaCloud.OSS.V2;
using AlibabaCloud.OSS.V2.Credentials;
using AlibabaCloud.OSS.V2.Models;

internal sealed record LauncherReleaseUploadOptions(
    string InstallerPath,
    string Version,
    string ExpectedSha256,
    string Bucket,
    string Region,
    string Endpoint,
    string DownloadEndpoint,
    string CredentialPath,
    string CredentialEntropyLabel,
    TimeSpan DownloadLinkLifetime);

internal sealed record LauncherReleaseUploadResult(
    bool Uploaded,
    string ObjectKey,
    long Length,
    string Sha256,
    string DownloadUrl,
    DateTimeOffset DownloadUrlExpiresAt);

internal interface ILauncherReleaseObjectStore : IDisposable
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
        string version,
        string fileName,
        CancellationToken cancellationToken);

    string CreatePresignedGetUrl(
        string bucket,
        string key,
        string fileName,
        DateTimeOffset expiresAt);
}

internal sealed class AlibabaLauncherReleaseObjectStore
    : ILauncherReleaseObjectStore
{
    private readonly Client uploadClient;
    private readonly Client downloadClient;
    private readonly string downloadHost;

    public AlibabaLauncherReleaseObjectStore(
        OssCredential credential,
        string region,
        string endpoint,
        string downloadEndpoint)
    {
        var uploadConfiguration = CreateConfiguration(
            credential,
            region,
            endpoint);
        uploadClient = new Client(uploadConfiguration);

        var downloadConfiguration = CreateConfiguration(
            credential,
            region,
            downloadEndpoint);
        downloadConfiguration.UseCName = true;
        downloadClient = new Client(downloadConfiguration);
        downloadHost = new Uri(downloadEndpoint).Host;
    }

    public async Task<OssRemoteObject?> HeadAsync(
        string bucket,
        string key,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await uploadClient.HeadObjectAsync(
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
        catch (ServiceException exception) when (
            exception.StatusCode == 404 &&
            exception.ErrorCode is "NoSuchKey" or "NoSuchObject" or "NotFound")
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
        string version,
        string fileName,
        CancellationToken cancellationToken)
    {
        using var body = File.OpenRead(path);
        await uploadClient.PutObjectAsync(
            new PutObjectRequest
            {
                Bucket = bucket,
                Key = key,
                Body = body,
                ContentLength = length,
                ContentMd5 = contentMd5,
                ContentType = "application/vnd.microsoft.portable-executable",
                ContentDisposition = CreateContentDisposition(fileName),
                CacheControl = "private, no-store",
                Acl = "private",
                ForbidOverwrite = true,
                Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["sha256"] = sha256,
                    ["release-version"] = version,
                    ["original-filename"] = fileName
                }
            },
            cancellationToken: cancellationToken);
    }

    public string CreatePresignedGetUrl(
        string bucket,
        string key,
        string fileName,
        DateTimeOffset expiresAt)
    {
        var result = downloadClient.Presign(
            new GetObjectRequest
            {
                Bucket = bucket,
                Key = key,
                ResponseContentDisposition = CreateContentDisposition(fileName)
            },
            expiresAt.UtcDateTime);
        if (!Uri.TryCreate(result.Url, UriKind.Absolute, out var url) ||
            !string.Equals(url.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(url.Host, downloadHost, StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException(
                "OSS returned an invalid launcher download URL.");
        }

        return url.AbsoluteUri;
    }

    public void Dispose()
    {
        uploadClient.Dispose();
        downloadClient.Dispose();
    }

    private static Configuration CreateConfiguration(
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
        return configuration;
    }

    private static string CreateContentDisposition(string fileName) =>
        $"attachment; filename=\"{fileName}\"";
}

internal sealed class LauncherReleaseUploader
{
    private const long MaximumInstallerBytes = 2L * 1024 * 1024 * 1024;
    private readonly LauncherReleaseUploadOptions options;
    private readonly ILauncherReleaseObjectStore? objectStore;

    public LauncherReleaseUploader(LauncherReleaseUploadOptions options)
        : this(options, objectStore: null)
    {
    }

    internal LauncherReleaseUploader(
        LauncherReleaseUploadOptions options,
        ILauncherReleaseObjectStore? objectStore)
    {
        this.options = options;
        this.objectStore = objectStore;
    }

    public async Task<LauncherReleaseUploadResult> UploadAsync(
        CancellationToken cancellationToken)
    {
        var version = ValidateVersion(options.Version);
        var installer = ValidateInstaller(options.InstallerPath, version);
        var expectedSha256 = ValidateSha256(options.ExpectedSha256);
        var actualSha256 = await ComputeSha256Async(
            installer.FullName,
            cancellationToken);
        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(actualSha256),
                Convert.FromHexString(expectedSha256)))
        {
            throw new PublisherUsageException(
                $"Launcher installer SHA-256 mismatch. Expected {expectedSha256}, " +
                $"actual {actualSha256}.");
        }

        var bucket = ValidateSimpleName(options.Bucket, "bucket");
        var region = ValidateSimpleName(options.Region, "region");
        var endpoint = ValidateHttpsOrigin(options.Endpoint, "endpoint");
        var downloadEndpoint = ValidateHttpsOrigin(
            options.DownloadEndpoint,
            "download endpoint");
        ValidateDownloadLinkLifetime(options.DownloadLinkLifetime);
        var objectKey = BuildObjectKey(version, installer.Name);

        using var store = objectStore ??
                          CreateObjectStore(region, endpoint, downloadEndpoint);
        var remoteObject = await HeadRemoteObjectAsync(
            store,
            bucket,
            objectKey,
            cancellationToken);
        var uploaded = false;
        if (remoteObject is not null)
        {
            ValidateRemoteObject(
                objectKey,
                installer,
                actualSha256,
                version,
                remoteObject);
        }
        else
        {
            var contentMd5 = await ComputeContentMd5Async(
                installer.FullName,
                cancellationToken);
            try
            {
                await store.PutAsync(
                    bucket,
                    objectKey,
                    installer.FullName,
                    installer.Length,
                    contentMd5,
                    actualSha256,
                    version,
                    installer.Name,
                    cancellationToken);
                uploaded = true;
            }
            catch (ServiceException exception) when (
                exception.StatusCode == 409 &&
                exception.ErrorCode is "FileAlreadyExists" or "ObjectAlreadyExists")
            {
                uploaded = false;
            }
            catch (ServiceException exception)
            {
                throw new IOException(
                    $"Unable to upload OSS launcher release {objectKey}.",
                    exception);
            }

            remoteObject = await HeadRemoteObjectAsync(
                store,
                bucket,
                objectKey,
                cancellationToken);
            if (remoteObject is null)
            {
                throw new IOException(
                    $"OSS launcher release {objectKey} was not visible after upload.");
            }

            ValidateRemoteObject(
                objectKey,
                installer,
                actualSha256,
                version,
                remoteObject);
        }

        var expiresAt = DateTimeOffset.UtcNow.Add(
            options.DownloadLinkLifetime);
        var downloadUrl = store.CreatePresignedGetUrl(
            bucket,
            objectKey,
            installer.Name,
            expiresAt);
        return new LauncherReleaseUploadResult(
            uploaded,
            objectKey,
            installer.Length,
            actualSha256,
            downloadUrl,
            expiresAt);
    }

    private static async Task<OssRemoteObject?> HeadRemoteObjectAsync(
        ILauncherReleaseObjectStore store,
        string bucket,
        string objectKey,
        CancellationToken cancellationToken)
    {
        try
        {
            return await store.HeadAsync(
                bucket,
                objectKey,
                cancellationToken);
        }
        catch (ServiceException exception)
        {
            throw new IOException(
                $"Unable to read OSS launcher release {objectKey}.",
                exception);
        }
    }

    internal static string BuildObjectKey(string version, string fileName)
    {
        var validatedVersion = ValidateVersion(version);
        var expectedFileName =
            $"Hechao-Launcher-Setup-{validatedVersion}-win-x64.exe";
        if (!string.Equals(fileName, expectedFileName, StringComparison.Ordinal))
        {
            throw new PublisherUsageException(
                $"Launcher installer must be named {expectedFileName}.");
        }

        return $"releases/launcher/{validatedVersion}/{expectedFileName}";
    }

    private ILauncherReleaseObjectStore CreateObjectStore(
        string region,
        string endpoint,
        string downloadEndpoint)
    {
        var credential = OssCredentialStore.Load(
            options.CredentialPath,
            options.CredentialEntropyLabel);
        return new AlibabaLauncherReleaseObjectStore(
            credential,
            region,
            endpoint,
            downloadEndpoint);
    }

    private static FileInfo ValidateInstaller(string path, string version)
    {
        var installer = new FileInfo(Path.GetFullPath(path));
        var expectedFileName =
            $"Hechao-Launcher-Setup-{version}-win-x64.exe";
        if (!installer.Exists ||
            installer.Length is <= 0 or > MaximumInstallerBytes ||
            (installer.Attributes & FileAttributes.ReparsePoint) != 0 ||
            !string.Equals(
                installer.Name,
                expectedFileName,
                StringComparison.Ordinal))
        {
            throw new PublisherUsageException(
                $"Launcher installer must be a regular file named {expectedFileName}.");
        }

        return installer;
    }

    private static void ValidateRemoteObject(
        string key,
        FileInfo installer,
        string sha256,
        string version,
        OssRemoteObject remoteObject)
    {
        if (remoteObject.ContentLength != installer.Length)
        {
            throw new IOException(
                $"OSS launcher release {key} already exists with length " +
                $"{remoteObject.ContentLength}, expected {installer.Length}. " +
                "Refusing to overwrite it.");
        }

        ValidateMetadata(key, remoteObject, "sha256", sha256);
        ValidateMetadata(key, remoteObject, "release-version", version);
        ValidateMetadata(
            key,
            remoteObject,
            "original-filename",
            installer.Name);
    }

    private static void ValidateMetadata(
        string key,
        OssRemoteObject remoteObject,
        string name,
        string expectedValue)
    {
        var actualValue = remoteObject.Metadata
            .FirstOrDefault(entry =>
                string.Equals(
                    entry.Key,
                    name,
                    StringComparison.OrdinalIgnoreCase))
            .Value;
        if (!string.Equals(
                actualValue,
                expectedValue,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException(
                $"OSS launcher release {key} already exists with {name} metadata " +
                $"'{actualValue ?? "<missing>"}', expected {expectedValue}. " +
                "Refusing to overwrite it.");
        }
    }

    private static string ValidateVersion(string value)
    {
        if (value.Length is <= 0 or > 32 ||
            !Version.TryParse(value, out var version) ||
            version.Build < 0 ||
            version.Revision >= 0 ||
            !string.Equals(
                value,
                version.ToString(3),
                StringComparison.Ordinal))
        {
            throw new PublisherUsageException(
                "Launcher release version must use canonical major.minor.patch format.");
        }

        return value;
    }

    private static string ValidateSha256(string value)
    {
        if (value.Length != 64 || !value.All(Uri.IsHexDigit))
        {
            throw new PublisherUsageException(
                "Launcher installer SHA-256 must be a 64-character hex digest.");
        }

        return value.ToLowerInvariant();
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

    private static string ValidateHttpsOrigin(string value, string fieldName)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint) ||
            !string.Equals(
                endpoint.Scheme,
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(endpoint.Query) ||
            !string.IsNullOrEmpty(endpoint.Fragment) ||
            endpoint.AbsolutePath is not "/")
        {
            throw new PublisherUsageException(
                $"The OSS {fieldName} must be an HTTPS origin.");
        }

        return endpoint.AbsoluteUri.TrimEnd('/');
    }

    private static void ValidateDownloadLinkLifetime(TimeSpan lifetime)
    {
        if (lifetime < TimeSpan.FromMinutes(5) ||
            lifetime > TimeSpan.FromDays(1))
        {
            throw new PublisherUsageException(
                "The internal download link lifetime must be between 5 and 1440 minutes.");
        }
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
            bufferSize: 1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var digest = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(digest).ToLowerInvariant();
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
}

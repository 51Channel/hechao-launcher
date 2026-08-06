using System.Security.Cryptography;
using AlibabaCloud.OSS.V2;

namespace Hechao.Publisher.Tests;

public sealed class OssDistributionUploaderTests
{
    [Fact]
    public void ServiceExceptionClassifier_FindsNestedSdkError()
    {
        var serviceException = new ServiceException(
            404,
            new Dictionary<string, string>
            {
                ["Code"] = "NoSuchKey",
                ["Message"] = "missing"
            },
            new Dictionary<string, string>(),
            new Dictionary<string, string>());
        var operationException = new InvalidOperationException(
            "operation failed",
            serviceException);

        Assert.True(
            OssServiceExceptionClassifier.Matches(
                operationException,
                404,
                "NoSuchKey",
                "NotFound"));
        Assert.True(
            OssServiceExceptionClassifier.ContainsServiceException(
                operationException));
        Assert.False(
            OssServiceExceptionClassifier.Matches(
                operationException,
                409,
                "FileAlreadyExists"));
    }

    [Fact]
    public void ValidateAndEnumerateObjects_AcceptsContentAddressedObjects()
    {
        using var distribution = TestDistribution.Create("content"u8.ToArray());

        var objects = OssDistributionUploader.ValidateAndEnumerateObjects(
            distribution.Path);

        var item = Assert.Single(objects);
        Assert.Equal(distribution.Digest, item.Digest);
        Assert.Equal(7, item.Length);
    }

    [Fact]
    public void ValidateAndEnumerateObjects_RejectsDigestMismatch()
    {
        using var distribution = TestDistribution.Create("content"u8.ToArray());
        File.WriteAllText(distribution.ObjectPath, "changed");

        Assert.Throws<PublisherUsageException>(
            () => OssDistributionUploader.ValidateAndEnumerateObjects(
                distribution.Path));
    }

    [Fact]
    public void ValidateAndEnumerateObjects_RejectsUnexpectedRootFile()
    {
        using var distribution = TestDistribution.Create("content"u8.ToArray());
        File.WriteAllText(
            System.IO.Path.Combine(distribution.Path, "objects", "notes.txt"),
            "not an object");

        Assert.Throws<PublisherUsageException>(
            () => OssDistributionUploader.ValidateAndEnumerateObjects(
                distribution.Path));
    }

    [Fact]
    public void ValidateAndEnumerateObjects_RejectsNestedPrefixDirectory()
    {
        using var distribution = TestDistribution.Create("content"u8.ToArray());
        Directory.CreateDirectory(System.IO.Path.Combine(
            distribution.Path,
            "objects",
            distribution.Digest[..2],
            "nested"));

        Assert.Throws<PublisherUsageException>(
            () => OssDistributionUploader.ValidateAndEnumerateObjects(
                distribution.Path));
    }

    [Fact]
    public async Task UploadAsync_SkipsMatchingRemoteObject()
    {
        using var distribution = TestDistribution.Create("content"u8.ToArray());
        var store = new FakeObjectStore(
            new OssRemoteObject(
                7,
                new Dictionary<string, string>
                {
                    ["SHA256"] = distribution.Digest.ToUpperInvariant()
                }));
        var uploader = new OssDistributionUploader(
            CreateOptions(distribution.Path),
            store);

        var result = await uploader.UploadAsync(CancellationToken.None);

        Assert.Equal(0, result.Uploaded);
        Assert.Equal(1, result.AlreadyPresent);
        Assert.Equal(0, result.UploadedBytes);
        Assert.Equal(1, store.HeadCalls);
        Assert.Equal(0, store.PutCalls);
        Assert.Equal(
            $"objects/{distribution.Digest[..2]}/{distribution.Digest}",
            store.LastKey);
    }

    [Fact]
    public async Task UploadAsync_UploadsMissingRemoteObject()
    {
        using var distribution = TestDistribution.Create("content"u8.ToArray());
        var store = new FakeObjectStore(remoteObject: null);
        var uploader = new OssDistributionUploader(
            CreateOptions(distribution.Path),
            store);

        var result = await uploader.UploadAsync(CancellationToken.None);

        Assert.Equal(1, result.Uploaded);
        Assert.Equal(0, result.AlreadyPresent);
        Assert.Equal(7, result.UploadedBytes);
        Assert.Equal(2, store.HeadCalls);
        Assert.Equal(1, store.PutCalls);
        Assert.Equal(distribution.Digest, store.LastSha256);
        Assert.False(string.IsNullOrWhiteSpace(store.LastContentMd5));
    }

    [Fact]
    public async Task UploadAsync_ReportsProcessedObjectsAndBytes()
    {
        using var distribution = TestDistribution.Create("content"u8.ToArray());
        var store = new FakeObjectStore(remoteObject: null);
        var samples = new List<OssUploadProgress>();
        var uploader = new OssDistributionUploader(
            CreateOptions(distribution.Path),
            store);

        await uploader.UploadAsync(
            (sample, _) =>
            {
                samples.Add(sample);
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.Equal(2, samples.Count);
        Assert.Equal(new OssUploadProgress(0, 1, 0, 0, 0, 7), samples[0]);
        Assert.Equal(new OssUploadProgress(1, 1, 1, 0, 7, 7), samples[1]);
    }

    [Fact]
    public async Task UploadAsync_RejectsRemoteLengthMismatchWithoutUploading()
    {
        using var distribution = TestDistribution.Create("content"u8.ToArray());
        var store = new FakeObjectStore(
            new OssRemoteObject(
                8,
                new Dictionary<string, string>
                {
                    ["sha256"] = distribution.Digest
                }));
        var uploader = new OssDistributionUploader(
            CreateOptions(distribution.Path),
            store);

        var exception = await Assert.ThrowsAsync<AggregateException>(
            () => uploader.UploadAsync(CancellationToken.None));

        Assert.Contains("Refusing to overwrite it.", exception.ToString());
        Assert.Equal(0, store.PutCalls);
    }

    [Fact]
    public async Task UploadAsync_RejectsRemoteDigestMismatchWithoutUploading()
    {
        using var distribution = TestDistribution.Create("content"u8.ToArray());
        var store = new FakeObjectStore(
            new OssRemoteObject(
                7,
                new Dictionary<string, string>
                {
                    ["sha256"] = new string('0', 64)
                }));
        var uploader = new OssDistributionUploader(
            CreateOptions(distribution.Path),
            store);

        var exception = await Assert.ThrowsAsync<AggregateException>(
            () => uploader.UploadAsync(CancellationToken.None));

        Assert.Contains("Refusing to overwrite it.", exception.ToString());
        Assert.Equal(0, store.PutCalls);
    }

    private static OssUploadOptions CreateOptions(string distributionDirectory)
    {
        return new OssUploadOptions(
            distributionDirectory,
            "hechaoworld",
            "cn-shanghai",
            "https://oss-cn-shanghai.aliyuncs.com",
            "objects",
            "unused.dpapi",
            "unused-entropy",
            1);
    }

    private sealed class FakeObjectStore(OssRemoteObject? remoteObject)
        : IOssObjectStore
    {
        private OssRemoteObject? currentRemoteObject = remoteObject;

        public int HeadCalls { get; private set; }
        public int PutCalls { get; private set; }
        public string? LastKey { get; private set; }
        public string? LastContentMd5 { get; private set; }
        public string? LastSha256 { get; private set; }

        public Task<OssRemoteObject?> HeadAsync(
            string bucket,
            string key,
            CancellationToken cancellationToken)
        {
            Assert.Equal("hechaoworld", bucket);
            cancellationToken.ThrowIfCancellationRequested();
            HeadCalls++;
            LastKey = key;
            return Task.FromResult(currentRemoteObject);
        }

        public Task PutAsync(
            string bucket,
            string key,
            string path,
            long length,
            string contentMd5,
            string sha256,
            CancellationToken cancellationToken)
        {
            Assert.Equal("hechaoworld", bucket);
            Assert.True(File.Exists(path));
            Assert.Equal(7, length);
            cancellationToken.ThrowIfCancellationRequested();
            PutCalls++;
            LastKey = key;
            LastContentMd5 = contentMd5;
            LastSha256 = sha256;
            currentRemoteObject = new OssRemoteObject(
                length,
                new Dictionary<string, string>
                {
                    ["sha256"] = sha256
                });
            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }
    }

    private sealed class TestDistribution(
        string path,
        string objectPath,
        string digest) : IDisposable
    {
        public string Path { get; } = path;
        public string ObjectPath { get; } = objectPath;
        public string Digest { get; } = digest;

        public static TestDistribution Create(byte[] content)
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "Hechao.Publisher.Tests",
                Guid.NewGuid().ToString("N"));
            var digest = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
            var prefixDirectory = System.IO.Path.Combine(
                path,
                "objects",
                digest[..2]);
            Directory.CreateDirectory(prefixDirectory);
            var objectPath = System.IO.Path.Combine(prefixDirectory, digest);
            File.WriteAllBytes(objectPath, content);
            return new TestDistribution(path, objectPath, digest);
        }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}

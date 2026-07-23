using System.Security.Cryptography;

namespace Hechao.Publisher.Tests;

public sealed class OssDistributionUploaderTests
{
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

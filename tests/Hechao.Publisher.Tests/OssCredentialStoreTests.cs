using System.Security.Cryptography;

namespace Hechao.Publisher.Tests;

public sealed class OssCredentialStoreTests
{
    [Fact]
    public void ProtectAndLoad_RoundTripsCredentialWithoutPlaintextFile()
    {
        const string entropyLabel = "Hechao.Publisher.Tests/OssCredential/v1";
        using var directory = new TemporaryDirectory();
        var encryptedPath = Path.Combine(directory.Path, "credential.dpapi");
        var metadataPath = Path.Combine(directory.Path, "credential.meta.json");
        var credential = new OssCredential(
            "TestAccessKeyId012345",
            "TestAccessKeySecret0123456789");
        var metadata = new OssCredentialMetadata(
            1,
            "Alibaba Cloud RAM",
            "test-publisher",
            "TestPublisherPolicy",
            "test-bucket",
            "objects/*",
            "Windows DPAPI CurrentUser",
            entropyLabel,
            DateTimeOffset.UtcNow,
            string.Empty);

        OssCredentialStore.Protect(
            credential,
            encryptedPath,
            metadataPath,
            entropyLabel,
            metadata);
        var loaded = OssCredentialStore.Load(encryptedPath, entropyLabel);

        Assert.Equal(credential, loaded);
        Assert.True(File.Exists(metadataPath));
        Assert.False(File.Exists(Path.Combine(directory.Path, "credential.json")));
    }

    [Fact]
    public void Protect_RefusesToOverwriteExistingCredential()
    {
        const string entropyLabel = "Hechao.Publisher.Tests/OssCredential/v1";
        using var directory = new TemporaryDirectory();
        var encryptedPath = Path.Combine(directory.Path, "credential.dpapi");
        var metadataPath = Path.Combine(directory.Path, "credential.meta.json");
        File.WriteAllText(encryptedPath, "existing");

        Assert.Throws<PublisherUsageException>(() => OssCredentialStore.Protect(
            new OssCredential(
                "TestAccessKeyId012345",
                "TestAccessKeySecret0123456789"),
            encryptedPath,
            metadataPath,
            entropyLabel,
            new OssCredentialMetadata(
                1,
                "Alibaba Cloud RAM",
                "test-publisher",
                "TestPublisherPolicy",
                "test-bucket",
                "objects/*",
                "Windows DPAPI CurrentUser",
                entropyLabel,
                DateTimeOffset.UtcNow,
                string.Empty)));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "Hechao.Publisher.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}

using System.Security.Cryptography;

namespace Hechao.Publisher.Tests;

public sealed class OssCredentialStoreTests
{
    [Fact]
    public void ProtectAndLoad_RoundTripsCredentialWithoutPlaintextFile()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

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
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

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

    [Fact]
    public void Load_ReadsSystemdCredentialJson()
    {
        using var directory = new TemporaryDirectory();
        var credentialPath = Path.Combine(directory.Path, "oss-credential");
        File.WriteAllText(
            credentialPath,
            """
            {
              "accessKeyId": "TestAccessKeyId012345",
              "accessKeySecret": "TestAccessKeySecret0123456789"
            }
            """);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                credentialPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        var credential = OssCredentialStore.Load(
            credentialPath,
            entropyLabel: null);

        Assert.Equal("TestAccessKeyId012345", credential.AccessKeyId);
        Assert.Equal(
            "TestAccessKeySecret0123456789",
            credential.AccessKeySecret);
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

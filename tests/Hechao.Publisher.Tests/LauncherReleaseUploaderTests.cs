using System.Security.Cryptography;

namespace Hechao.Publisher.Tests;

public sealed class LauncherReleaseUploaderTests
{
    [Fact]
    public void CredentialInput_ParsesSystemdCredentialWithoutEntropy()
    {
        var options = CommandOptions.Parse(
        [
            "--credential-systemd",
            "credentials/oss-publisher-credential",
        ]);

        var input = LauncherReleaseCredentialInput.Parse(options);

        Assert.Equal(
            Path.GetFullPath("credentials/oss-publisher-credential"),
            input.Path);
        Assert.Null(input.EntropyLabel);
    }

    [Fact]
    public void CredentialInput_ParsesDpapiCredentialWithEntropy()
    {
        var options = CommandOptions.Parse(
        [
            "--credential-dpapi",
            "credentials/oss.dpapi",
            "--dpapi-entropy-label",
            "Hechao/Oss/v1",
        ]);

        var input = LauncherReleaseCredentialInput.Parse(options);

        Assert.Equal(Path.GetFullPath("credentials/oss.dpapi"), input.Path);
        Assert.Equal("Hechao/Oss/v1", input.EntropyLabel);
    }

    [Fact]
    public void CredentialInput_RejectsMultipleCredentialModes()
    {
        var options = CommandOptions.Parse(
        [
            "--credential-dpapi",
            "credentials/oss.dpapi",
            "--credential-systemd",
            "credentials/oss-publisher-credential",
            "--dpapi-entropy-label",
            "Hechao/Oss/v1",
        ]);

        Assert.Throws<PublisherUsageException>(
            () => LauncherReleaseCredentialInput.Parse(options));
    }

    [Fact]
    public void CredentialInput_RejectsDpapiEntropyWithSystemdCredential()
    {
        var options = CommandOptions.Parse(
        [
            "--credential-systemd",
            "credentials/oss-publisher-credential",
            "--dpapi-entropy-label",
            "Hechao/Oss/v1",
        ]);

        Assert.Throws<PublisherUsageException>(
            () => LauncherReleaseCredentialInput.Parse(options));
    }

    [Fact]
    public void BuildObjectKey_UsesCanonicalReleasePath()
    {
        var key = LauncherReleaseUploader.BuildObjectKey(
            "0.10.0",
            "Hechao-Launcher-Setup-0.10.0-win-x64.exe");

        Assert.Equal(
            "releases/launcher/0.10.0/" +
            "Hechao-Launcher-Setup-0.10.0-win-x64.exe",
            key);
    }

    [Theory]
    [InlineData("0.10")]
    [InlineData("0.10.0.1")]
    [InlineData("00.10.0")]
    [InlineData("../0.10.0")]
    public void BuildObjectKey_RejectsNonCanonicalVersion(string version)
    {
        Assert.Throws<PublisherUsageException>(
            () => LauncherReleaseUploader.BuildObjectKey(
                version,
                $"Hechao-Launcher-Setup-{version}-win-x64.exe"));
    }

    [Fact]
    public async Task UploadAsync_RejectsLocalDigestMismatchBeforeOssRequest()
    {
        using var installer = TestInstaller.Create(
            "0.10.0",
            "installer"u8.ToArray());
        var store = new FakeReleaseObjectStore(remoteObject: null);
        var uploader = new LauncherReleaseUploader(
            CreateOptions(installer, new string('0', 64)),
            store);

        var exception = await Assert.ThrowsAsync<PublisherUsageException>(
            () => uploader.UploadAsync(CancellationToken.None));

        Assert.Contains("SHA-256 mismatch", exception.Message);
        Assert.Equal(0, store.HeadCalls);
        Assert.Equal(0, store.PutCalls);
        Assert.Equal(0, store.PresignCalls);
    }

    [Fact]
    public async Task UploadAsync_SkipsMatchingReleaseAndCreatesPrivateLink()
    {
        using var installer = TestInstaller.Create(
            "0.10.0",
            "installer"u8.ToArray());
        var store = new FakeReleaseObjectStore(
            CreateRemoteObject(installer));
        var uploader = new LauncherReleaseUploader(
            CreateOptions(installer, installer.Digest.ToUpperInvariant()),
            store);

        var result = await uploader.UploadAsync(CancellationToken.None);

        Assert.False(result.Uploaded);
        Assert.Equal(installer.Digest, result.Sha256);
        Assert.Equal(installer.Length, result.Length);
        Assert.Equal(1, store.HeadCalls);
        Assert.Equal(0, store.PutCalls);
        Assert.Equal(1, store.PresignCalls);
        Assert.StartsWith(
            "https://download.hechao.world/",
            result.DownloadUrl,
            StringComparison.Ordinal);
        Assert.True(result.DownloadUrlExpiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task UploadAsync_UploadsMissingReleaseAndVerifiesIt()
    {
        using var installer = TestInstaller.Create(
            "0.10.0",
            "installer"u8.ToArray());
        var store = new FakeReleaseObjectStore(remoteObject: null);
        var uploader = new LauncherReleaseUploader(
            CreateOptions(installer, installer.Digest),
            store);

        var result = await uploader.UploadAsync(CancellationToken.None);

        Assert.True(result.Uploaded);
        Assert.Equal(2, store.HeadCalls);
        Assert.Equal(1, store.PutCalls);
        Assert.Equal(1, store.PresignCalls);
        Assert.Equal("0.10.0", store.LastVersion);
        Assert.Equal(installer.FileName, store.LastFileName);
        Assert.Equal(installer.Digest, store.LastSha256);
        Assert.False(string.IsNullOrWhiteSpace(store.LastContentMd5));
    }

    [Fact]
    public async Task UploadAsync_RejectsMismatchedExistingRelease()
    {
        using var installer = TestInstaller.Create(
            "0.10.0",
            "installer"u8.ToArray());
        var store = new FakeReleaseObjectStore(
            new OssRemoteObject(
                installer.Length,
                new Dictionary<string, string>
                {
                    ["sha256"] = new string('0', 64),
                    ["release-version"] = installer.Version,
                    ["original-filename"] = installer.FileName
                }));
        var uploader = new LauncherReleaseUploader(
            CreateOptions(installer, installer.Digest),
            store);

        var exception = await Assert.ThrowsAsync<IOException>(
            () => uploader.UploadAsync(CancellationToken.None));

        Assert.Contains("Refusing to overwrite it.", exception.Message);
        Assert.Equal(0, store.PutCalls);
        Assert.Equal(0, store.PresignCalls);
    }

    [Fact]
    public async Task UploadAsync_RejectsIncompleteExistingMetadata()
    {
        using var installer = TestInstaller.Create(
            "0.10.0",
            "installer"u8.ToArray());
        var store = new FakeReleaseObjectStore(
            new OssRemoteObject(
                installer.Length,
                new Dictionary<string, string>
                {
                    ["sha256"] = installer.Digest
                }));
        var uploader = new LauncherReleaseUploader(
            CreateOptions(installer, installer.Digest),
            store);

        var exception = await Assert.ThrowsAsync<IOException>(
            () => uploader.UploadAsync(CancellationToken.None));

        Assert.Contains("release-version", exception.Message);
        Assert.Equal(0, store.PutCalls);
        Assert.Equal(0, store.PresignCalls);
    }

    private static LauncherReleaseUploadOptions CreateOptions(
        TestInstaller installer,
        string expectedSha256)
    {
        return new LauncherReleaseUploadOptions(
            installer.Path,
            installer.Version,
            expectedSha256,
            "hechaoworld",
            "cn-shanghai",
            "https://oss-cn-shanghai.aliyuncs.com",
            "https://download.hechao.world",
            "unused.dpapi",
            "unused-entropy",
            TimeSpan.FromMinutes(60));
    }

    private static OssRemoteObject CreateRemoteObject(TestInstaller installer)
    {
        return new OssRemoteObject(
            installer.Length,
            new Dictionary<string, string>
            {
                ["sha256"] = installer.Digest,
                ["release-version"] = installer.Version,
                ["original-filename"] = installer.FileName
            });
    }

    private sealed class FakeReleaseObjectStore(OssRemoteObject? remoteObject)
        : ILauncherReleaseObjectStore
    {
        private OssRemoteObject? currentRemoteObject = remoteObject;

        public int HeadCalls { get; private set; }
        public int PutCalls { get; private set; }
        public int PresignCalls { get; private set; }
        public string? LastContentMd5 { get; private set; }
        public string? LastSha256 { get; private set; }
        public string? LastVersion { get; private set; }
        public string? LastFileName { get; private set; }

        public Task<OssRemoteObject?> HeadAsync(
            string bucket,
            string key,
            CancellationToken cancellationToken)
        {
            Assert.Equal("hechaoworld", bucket);
            Assert.StartsWith("releases/launcher/", key, StringComparison.Ordinal);
            cancellationToken.ThrowIfCancellationRequested();
            HeadCalls++;
            return Task.FromResult(currentRemoteObject);
        }

        public Task PutAsync(
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
            Assert.Equal("hechaoworld", bucket);
            Assert.True(File.Exists(path));
            cancellationToken.ThrowIfCancellationRequested();
            PutCalls++;
            LastContentMd5 = contentMd5;
            LastSha256 = sha256;
            LastVersion = version;
            LastFileName = fileName;
            currentRemoteObject = new OssRemoteObject(
                length,
                new Dictionary<string, string>
                {
                    ["sha256"] = sha256,
                    ["release-version"] = version,
                    ["original-filename"] = fileName
                });
            return Task.CompletedTask;
        }

        public string CreatePresignedGetUrl(
            string bucket,
            string key,
            string fileName,
            DateTimeOffset expiresAt)
        {
            Assert.Equal("hechaoworld", bucket);
            Assert.Contains(fileName, key, StringComparison.Ordinal);
            Assert.True(expiresAt > DateTimeOffset.UtcNow);
            PresignCalls++;
            return $"https://download.hechao.world/{key}?signature=test";
        }

        public void Dispose()
        {
        }
    }

    private sealed class TestInstaller(
        string directory,
        string path,
        string version,
        string fileName,
        string digest,
        long length) : IDisposable
    {
        public string Path { get; } = path;
        public string Version { get; } = version;
        public string FileName { get; } = fileName;
        public string Digest { get; } = digest;
        public long Length { get; } = length;

        public static TestInstaller Create(string version, byte[] content)
        {
            var directory = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "Hechao.Publisher.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var fileName =
                $"Hechao-Launcher-Setup-{version}-win-x64.exe";
            var path = System.IO.Path.Combine(directory, fileName);
            File.WriteAllBytes(path, content);
            return new TestInstaller(
                directory,
                path,
                version,
                fileName,
                Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(),
                content.Length);
        }

        public void Dispose()
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

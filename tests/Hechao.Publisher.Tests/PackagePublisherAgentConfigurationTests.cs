using System.Text.Json;

namespace Hechao.Publisher.Tests;

public sealed class PackagePublisherAgentConfigurationTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "hechao-publisher-agent-config-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Load_AcceptsSecretPathsButNoSecretValues()
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "agent.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            apiBaseUrl = "https://launcher-api.example",
            agentId = "publisher-main",
            tokenPath = Path.Combine(root, "token.dat"),
            stateDirectory = Path.Combine(root, "state"),
            pollSeconds = 3,
            signingKeyId = "release-primary",
            signingKeyPath = Path.Combine(root, "signing.dpapi"),
            signingKeyEntropyLabel = "Hechao/Signing/v1",
            signingKeyBlobSha256 = new string('a', 64),
            ossBucket = "hechaoworld",
            ossRegion = "cn-shanghai",
            ossEndpoint = "https://oss-cn-shanghai.aliyuncs.com",
            ossObjectPrefix = "objects",
            ossCredentialPath = Path.Combine(root, "oss.dpapi"),
            ossCredentialEntropyLabel = "Hechao/Oss/v1",
            parallelism = 8
        }));

        var configuration = PackagePublisherAgentConfiguration.Load(path);

        Assert.Equal("publisher-main", configuration.AgentId);
        Assert.Equal(Path.Combine(root, "state"), configuration.StateDirectory);
        Assert.DoesNotContain("secret", File.ReadAllText(path), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Load_RejectsApiPathAndOverlappingProtectedFiles()
    {
        var sharedPath = Path.Combine(root, "shared.dat");
        var configuration = new PackagePublisherAgentConfiguration
        {
            ApiBaseUrl = "https://launcher-api.example/not-an-origin",
            AgentId = "publisher-main",
            TokenPath = sharedPath,
            StateDirectory = Path.Combine(root, "state"),
            SigningKeyId = "release-primary",
            SigningKeyPath = sharedPath,
            SigningKeyEntropyLabel = "Hechao/Signing/v1",
            OssBucket = "hechaoworld",
            OssRegion = "cn-shanghai",
            OssEndpoint = "https://oss-cn-shanghai.aliyuncs.com",
            OssObjectPrefix = "objects",
            OssCredentialPath = Path.Combine(root, "oss.dat"),
            OssCredentialEntropyLabel = "Hechao/Oss/v1"
        };

        Assert.Throws<InvalidDataException>(configuration.Validate);
    }

    [Fact]
    public void Load_ResolvesSystemdCredentialNamesInsideRuntimeDirectory()
    {
        Directory.CreateDirectory(root);
        var credentials = Path.Combine(root, "credentials");
        Directory.CreateDirectory(credentials);
        var path = Path.Combine(root, "agent-systemd.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            apiBaseUrl = "http://127.0.0.1:8090",
            agentId = "publisher-main",
            secretStorage = "systemd-credentials",
            tokenPath = "publisher-token",
            stateDirectory = Path.Combine(root, "state"),
            signingKeyId = "release-primary",
            signingKeyPath = "distribution-signing-key",
            ossBucket = "hechaoworld",
            ossRegion = "cn-shanghai",
            ossEndpoint = "https://oss-cn-shanghai.aliyuncs.com",
            ossObjectPrefix = "objects",
            ossCredentialPath = "oss-publisher-credential",
            parallelism = 4,
            minimumFreeBytes = 2L * 1024 * 1024 * 1024
        }));

        var configuration = PackagePublisherAgentConfiguration.Load(
            path,
            credentials);

        Assert.True(configuration.UsesSystemdCredentials);
        Assert.Equal(
            Path.Combine(credentials, "publisher-token"),
            configuration.TokenPath);
        Assert.Equal(
            Path.Combine(credentials, "distribution-signing-key"),
            configuration.SigningKeyPath);
        Assert.Null(configuration.SigningKeyEntropyLabel);
        Assert.Null(configuration.OssCredentialEntropyLabel);
    }

    [Fact]
    public void Load_RejectsSystemdCredentialPathTraversal()
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "agent-systemd-invalid.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            apiBaseUrl = "http://127.0.0.1:8090",
            agentId = "publisher-main",
            secretStorage = "systemd-credentials",
            tokenPath = "../publisher-token",
            stateDirectory = Path.Combine(root, "state"),
            signingKeyId = "release-primary",
            signingKeyPath = "distribution-signing-key",
            ossBucket = "hechaoworld",
            ossRegion = "cn-shanghai",
            ossEndpoint = "https://oss-cn-shanghai.aliyuncs.com",
            ossObjectPrefix = "objects",
            ossCredentialPath = "oss-publisher-credential"
        }));

        Assert.Throws<InvalidDataException>(() =>
            PackagePublisherAgentConfiguration.Load(
                path,
                Path.Combine(root, "credentials")));
    }

    [Fact]
    public void ProtectedTokenStore_RoundTripsCurrentUserDpapi()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "token.dat");
        var token = new string('A', 48);

        PackagePublisherProtectedTokenStore.Protect(token, path);

        Assert.Equal(token, PackagePublisherProtectedTokenStore.Read(path));
        Assert.DoesNotContain(token, Convert.ToBase64String(File.ReadAllBytes(path)));
    }

    [Fact]
    public void SystemdCredentialStore_ReadsStrictTokenFile()
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "publisher-token");
        var token = new string('B', 48);
        File.WriteAllText(path, token + Environment.NewLine);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        Assert.Equal(
            token,
            PackagePublisherSystemdCredentialStore.ReadToken(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

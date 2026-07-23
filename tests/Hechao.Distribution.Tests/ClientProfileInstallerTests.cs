using System.Security.Cryptography;

namespace Hechao.Distribution.Tests;

public sealed class ClientProfileInstallerTests
{
    [Fact]
    public async Task InstallAsync_ActivatesVerifiedProfileAndKeepsPreviousVersion()
    {
        var content = "new-client-content"u8.ToArray();
        var digest = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        var handler = new RangeResponseHandler(content);
        using var httpClient = new HttpClient(handler);
        var installer = new ClientProfileInstaller(new ResumableFileDownloader(httpClient));
        using var temporary = new TemporaryDirectory();
        var profileId = "activity-neoforge-1.21.11";
        var activeDirectory = Path.Combine(temporary.Path, profileId);
        Directory.CreateDirectory(activeDirectory);
        await File.WriteAllTextAsync(Path.Combine(activeDirectory, "old-file.txt"), "old-client-content");
        var manifest = new ClientManifest(
            1,
            profileId,
            "2.0.0",
            "1.21.11",
            "21",
            "NeoForge",
            "21.11.42",
            DateTimeOffset.Parse("2026-07-22T00:00:00Z"),
            [new ClientManifestFile(
                "mods/example.jar",
                content.Length,
                digest,
                "https://download.hechao.world/object")],
            ["old-file.txt"]);
        var verified = new VerifiedClientManifest(manifest, new string('a', 64), "release-2026");

        await installer.InstallAsync(
            verified,
            new ClientInstallationOptions(temporary.Path, KeepObjectCache: true));

        Assert.Equal(content, await File.ReadAllBytesAsync(Path.Combine(activeDirectory, "mods", "example.jar")));
        Assert.True(File.Exists(Path.Combine(activeDirectory, ".hechao-install.json")));
        Assert.False(File.Exists(Path.Combine(activeDirectory, "old-file.txt")));
        Assert.Equal(
            "old-client-content",
            await File.ReadAllTextAsync(Path.Combine(temporary.Path, $".{profileId}.previous", "old-file.txt")));
    }

    [Fact]
    public async Task InstallAsync_RejectsConcurrentInstallerForSameProfile()
    {
        var content = "new-client-content"u8.ToArray();
        var manifest = ManifestTestData.CreateManifest(content);
        var handler = new RangeResponseHandler(content);
        using var httpClient = new HttpClient(handler);
        var installer = new ClientProfileInstaller(new ResumableFileDownloader(httpClient));
        using var temporary = new TemporaryDirectory();
        var lockDirectory = Path.Combine(temporary.Path, ".hechao", "locks");
        Directory.CreateDirectory(lockDirectory);
        await using var heldLock = new FileStream(
            Path.Combine(lockDirectory, manifest.ProfileId + ".lock"),
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None);

        await Assert.ThrowsAsync<ProfileInstallInProgressException>(() =>
            installer.InstallAsync(
                new VerifiedClientManifest(manifest, new string('a', 64), "release-2026"),
                new ClientInstallationOptions(temporary.Path)));

        Assert.Empty(handler.RequestedOffsets);
    }
}

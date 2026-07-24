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
        var layout = new ClientStorageLayout(temporary.Path);
        var activeDirectory = layout.GetProfileRoot(profileId);
        var activeGameDirectory = layout.GetProfileGameDirectory(profileId);
        Directory.CreateDirectory(activeGameDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(activeGameDirectory, "old-file.txt"),
            "old-client-content");
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

        Assert.Equal(
            content,
            await File.ReadAllBytesAsync(
                Path.Combine(activeGameDirectory, "mods", "example.jar")));
        Assert.True(File.Exists(Path.Combine(
            activeDirectory,
            ClientStorageLayout.InstallStateFileName)));
        Assert.False(File.Exists(Path.Combine(activeGameDirectory, "old-file.txt")));
        Assert.Equal(
            "old-client-content",
            await File.ReadAllTextAsync(Path.Combine(
                layout.GetPreviousProfileRoot(profileId),
                ClientStorageLayout.GameDirectoryName,
                "old-file.txt")));
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

    [Fact]
    public async Task InstallAsync_PreservesWritableGameDataAcrossUpdates()
    {
        var content = "updated-mod"u8.ToArray();
        var manifest = ManifestTestData.CreateManifest(content);
        var digest = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        manifest = manifest with
        {
            Files =
            [
                .. manifest.Files,
                new ClientManifestFile(
                    "options.txt",
                    content.Length,
                    digest,
                    "https://download.hechao.world/objects/options")
            ],
            DeletePaths = ["saves"]
        };
        var handler = new RangeResponseHandler(content);
        using var httpClient = new HttpClient(handler);
        var installer = new ClientProfileInstaller(new ResumableFileDownloader(httpClient));
        using var temporary = new TemporaryDirectory();
        var layout = new ClientStorageLayout(temporary.Path);
        var gameDirectory = layout.GetProfileGameDirectory(manifest.ProfileId);
        Directory.CreateDirectory(Path.Combine(gameDirectory, "saves", "recording-world"));
        await File.WriteAllTextAsync(
            Path.Combine(gameDirectory, "saves", "recording-world", "level.dat"),
            "world-data");
        await File.WriteAllTextAsync(
            Path.Combine(gameDirectory, "options.txt"),
            "fov:0.0");
        Directory.CreateDirectory(Path.Combine(gameDirectory, "mods"));
        await File.WriteAllTextAsync(
            Path.Combine(gameDirectory, "mods", "removed.jar"),
            "old-mod");

        await installer.InstallAsync(
            new VerifiedClientManifest(manifest, new string('a', 64), "release-2026"),
            new ClientInstallationOptions(temporary.Path));

        Assert.Equal(
            "world-data",
            await File.ReadAllTextAsync(
                Path.Combine(gameDirectory, "saves", "recording-world", "level.dat")));
        Assert.Equal(
            "fov:0.0",
            await File.ReadAllTextAsync(Path.Combine(gameDirectory, "options.txt")));
        Assert.False(File.Exists(Path.Combine(gameDirectory, "mods", "removed.jar")));
    }

    [Fact]
    public async Task InstallAsync_HardLinksShareableFilesToTheObjectStoreOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var content = "asset-index"u8.ToArray();
        var manifest = ManifestTestData.CreateManifest(
            content,
            "assets/indexes/hechao.json");
        var handler = new RangeResponseHandler(content);
        using var httpClient = new HttpClient(handler);
        var installer = new ClientProfileInstaller(new ResumableFileDownloader(httpClient));
        using var temporary = new TemporaryDirectory();
        var layout = new ClientStorageLayout(temporary.Path);

        await installer.InstallAsync(
            new VerifiedClientManifest(manifest, new string('a', 64), "release-2026"),
            new ClientInstallationOptions(temporary.Path, KeepObjectCache: true));

        var digest = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        var cachePath = Path.Combine(layout.ObjectCacheRoot, digest[..2], digest);
        var installedPath = Path.Combine(
            layout.GetProfileGameDirectory(manifest.ProfileId),
            "assets",
            "indexes",
            "hechao.json");
        var replacement = "asset-next!"u8.ToArray();
        Assert.Equal(content.Length, replacement.Length);

        await File.WriteAllBytesAsync(installedPath, replacement);

        Assert.Equal(replacement, await File.ReadAllBytesAsync(cachePath));
    }
}

using System.Security.Cryptography;
using System.Text.Json;

namespace Hechao.Distribution.Tests;

public sealed class ClientProfileInstallerTests
{
    [Fact]
    public async Task GetPreviousStateAsync_ReturnsOnlyValidPreviousInstallation()
    {
        var content = "unused"u8.ToArray();
        using var httpClient = new HttpClient(new RangeResponseHandler(content));
        var installer = new ClientProfileInstaller(new ResumableFileDownloader(httpClient));
        using var temporary = new TemporaryDirectory();
        var profileId = "base-1.21.11";

        Assert.Null(await installer.GetPreviousStateAsync(temporary.Path, profileId));

        var layout = new ClientStorageLayout(temporary.Path);
        var previousDirectory = layout.GetPreviousProfileRoot(profileId);
        await WriteInstalledProfileAsync(
            previousDirectory,
            profileId,
            "1.0.0",
            "previous-client");

        var state = await installer.GetPreviousStateAsync(temporary.Path, profileId);

        Assert.NotNull(state);
        Assert.Equal("1.0.0", state.Version);
    }

    [Fact]
    public async Task RollbackAsync_ActivatesPreviousVersionAndPreservesCurrentPlayerData()
    {
        var content = "unused"u8.ToArray();
        using var httpClient = new HttpClient(new RangeResponseHandler(content));
        var installer = new ClientProfileInstaller(new ResumableFileDownloader(httpClient));
        using var temporary = new TemporaryDirectory();
        var profileId = "base-1.21.11";
        var layout = new ClientStorageLayout(temporary.Path);
        var activeDirectory = layout.GetProfileRoot(profileId);
        var previousDirectory = layout.GetPreviousProfileRoot(profileId);
        await WriteInstalledProfileAsync(
            activeDirectory,
            profileId,
            "2.0.0",
            "current-client");
        await WriteInstalledProfileAsync(
            previousDirectory,
            profileId,
            "1.0.0",
            "previous-client");
        var activeGameDirectory = Path.Combine(
            activeDirectory,
            ClientStorageLayout.GameDirectoryName);
        var previousGameDirectory = Path.Combine(
            previousDirectory,
            ClientStorageLayout.GameDirectoryName);
        Directory.CreateDirectory(Path.Combine(
            activeGameDirectory,
            "saves",
            "current-world"));
        await File.WriteAllTextAsync(
            Path.Combine(activeGameDirectory, "saves", "current-world", "level.dat"),
            "current-world-data");
        await File.WriteAllTextAsync(
            Path.Combine(activeGameDirectory, "options.txt"),
            "current-options");
        Directory.CreateDirectory(Path.Combine(
            previousGameDirectory,
            "saves",
            "old-world"));
        await File.WriteAllTextAsync(
            Path.Combine(previousGameDirectory, "saves", "old-world", "level.dat"),
            "old-world-data");
        await File.WriteAllTextAsync(
            Path.Combine(previousGameDirectory, "options.txt"),
            "old-options");

        var activatedState = await installer.RollbackAsync(
            temporary.Path,
            profileId);

        Assert.Equal("1.0.0", activatedState.Version);
        Assert.Equal(
            "previous-client",
            await File.ReadAllTextAsync(Path.Combine(
                layout.GetProfileGameDirectory(profileId),
                "managed.txt")));
        Assert.Equal(
            "current-world-data",
            await File.ReadAllTextAsync(Path.Combine(
                layout.GetProfileGameDirectory(profileId),
                "saves",
                "current-world",
                "level.dat")));
        Assert.Equal(
            "current-options",
            await File.ReadAllTextAsync(Path.Combine(
                layout.GetProfileGameDirectory(profileId),
                "options.txt")));
        Assert.False(Directory.Exists(Path.Combine(
            layout.GetProfileGameDirectory(profileId),
            "saves",
            "old-world")));
        Assert.Equal(
            "current-client",
            await File.ReadAllTextAsync(Path.Combine(
                layout.GetPreviousProfileRoot(profileId),
                ClientStorageLayout.GameDirectoryName,
                "managed.txt")));
        Assert.Equal(
            "2.0.0",
            (await installer.GetPreviousStateAsync(temporary.Path, profileId))!.Version);
    }

    [Fact]
    public async Task RollbackAsync_RejectsConcurrentInstallerForSameProfile()
    {
        var content = "unused"u8.ToArray();
        using var httpClient = new HttpClient(new RangeResponseHandler(content));
        var installer = new ClientProfileInstaller(new ResumableFileDownloader(httpClient));
        using var temporary = new TemporaryDirectory();
        var profileId = "base-1.21.11";
        var layout = new ClientStorageLayout(temporary.Path);
        layout.EnsureBaseDirectories();
        await WriteInstalledProfileAsync(
            layout.GetProfileRoot(profileId),
            profileId,
            "2.0.0",
            "current-client");
        await WriteInstalledProfileAsync(
            layout.GetPreviousProfileRoot(profileId),
            profileId,
            "1.0.0",
            "previous-client");
        await using var heldLock = new FileStream(
            Path.Combine(layout.LocksRoot, profileId + ".lock"),
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None);

        await Assert.ThrowsAsync<ProfileInstallInProgressException>(() =>
            installer.RollbackAsync(temporary.Path, profileId));
    }

    [Fact]
    public async Task RollbackAsync_RejectsMissingOrInvalidPreviousInstallation()
    {
        var content = "unused"u8.ToArray();
        using var httpClient = new HttpClient(new RangeResponseHandler(content));
        var installer = new ClientProfileInstaller(new ResumableFileDownloader(httpClient));
        using var temporary = new TemporaryDirectory();
        var profileId = "base-1.21.11";
        var layout = new ClientStorageLayout(temporary.Path);
        await WriteInstalledProfileAsync(
            layout.GetProfileRoot(profileId),
            profileId,
            "2.0.0",
            "current-client");

        await Assert.ThrowsAsync<ProfileRollbackUnavailableException>(() =>
            installer.RollbackAsync(temporary.Path, profileId));

        Assert.Equal(
            "current-client",
            await File.ReadAllTextAsync(Path.Combine(
                layout.GetProfileGameDirectory(profileId),
                "managed.txt")));
    }

    [Fact]
    public async Task RollbackAsync_RestoresBothVersionsWhenActivationFails()
    {
        var content = "unused"u8.ToArray();
        using var httpClient = new HttpClient(new RangeResponseHandler(content));
        var switcher = new AtomicProfileDirectorySwitcher(
            () => throw new IOException("simulated activation failure"));
        var installer = new ClientProfileInstaller(
            new ResumableFileDownloader(httpClient),
            switcher);
        using var temporary = new TemporaryDirectory();
        var profileId = "base-1.21.11";
        var layout = new ClientStorageLayout(temporary.Path);
        await WriteInstalledProfileAsync(
            layout.GetProfileRoot(profileId),
            profileId,
            "2.0.0",
            "current-client");
        await WriteInstalledProfileAsync(
            layout.GetPreviousProfileRoot(profileId),
            profileId,
            "1.0.0",
            "previous-client");

        await Assert.ThrowsAsync<IOException>(() =>
            installer.RollbackAsync(temporary.Path, profileId));

        Assert.Equal(
            "current-client",
            await File.ReadAllTextAsync(Path.Combine(
                layout.GetProfileGameDirectory(profileId),
                "managed.txt")));
        Assert.Equal(
            "previous-client",
            await File.ReadAllTextAsync(Path.Combine(
                layout.GetPreviousProfileRoot(profileId),
                ClientStorageLayout.GameDirectoryName,
                "managed.txt")));
        Assert.Empty(Directory.EnumerateDirectories(
            layout.InstancesRoot,
            $".{profileId}.staging-*"));
    }

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

    [Fact]
    public async Task InstallAsync_KeepsActiveProfileWhenObjectServiceIsUnavailable()
    {
        var content = "new-client-content"u8.ToArray();
        var manifest = ManifestTestData.CreateManifest(content);
        var handler = new ServiceUnavailableHandler();
        using var httpClient = new HttpClient(handler);
        var installer = new ClientProfileInstaller(new ResumableFileDownloader(
            httpClient,
            retryDelay: static (_, _) => Task.CompletedTask));
        using var temporary = new TemporaryDirectory();
        var layout = new ClientStorageLayout(temporary.Path);
        var activeGameDirectory = layout.GetProfileGameDirectory(manifest.ProfileId);
        Directory.CreateDirectory(activeGameDirectory);
        var activeMarker = Path.Combine(activeGameDirectory, "current-version.txt");
        await File.WriteAllTextAsync(activeMarker, "working-version");

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            installer.InstallAsync(
                new VerifiedClientManifest(manifest, new string('a', 64), "release-2026"),
                new ClientInstallationOptions(temporary.Path)));

        Assert.Equal("working-version", await File.ReadAllTextAsync(activeMarker));
        Assert.False(Directory.Exists(layout.GetPreviousProfileRoot(manifest.ProfileId)));
        Assert.Empty(Directory.EnumerateDirectories(
            layout.InstancesRoot,
            $".{manifest.ProfileId}.staging-*"));
        Assert.Equal(5, handler.RequestCount);
    }

    [Fact]
    public async Task InstallAsync_RepairsTamperedManagedFileAndRetainsPreviousVersion()
    {
        var content = "verified-client-content"u8.ToArray();
        var manifest = ManifestTestData.CreateManifest(content);
        var handler = new RangeResponseHandler(content);
        using var httpClient = new HttpClient(handler);
        var installer = new ClientProfileInstaller(new ResumableFileDownloader(httpClient));
        using var temporary = new TemporaryDirectory();
        var layout = new ClientStorageLayout(temporary.Path);
        var activeGameDirectory = layout.GetProfileGameDirectory(manifest.ProfileId);
        var managedPath = Path.Combine(activeGameDirectory, "mods", "example.jar");
        Directory.CreateDirectory(Path.GetDirectoryName(managedPath)!);
        await File.WriteAllTextAsync(managedPath, "tampered");

        await installer.InstallAsync(
            new VerifiedClientManifest(manifest, new string('a', 64), "release-2026"),
            new ClientInstallationOptions(temporary.Path));

        Assert.Equal(content, await File.ReadAllBytesAsync(managedPath));
        Assert.Equal(
            "tampered",
            await File.ReadAllTextAsync(Path.Combine(
                layout.GetPreviousProfileRoot(manifest.ProfileId),
                ClientStorageLayout.GameDirectoryName,
                "mods",
                "example.jar")));
        Assert.Single(handler.RequestedOffsets);
    }

    [Fact]
    public async Task InstallAsync_DownloadsMissingObjectsWithBoundedConcurrency()
    {
        const int fileCount = 12;
        const int maximumConcurrency = 4;
        var objects = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var files = new List<ClientManifestFile>(fileCount);
        for (var index = 0; index < fileCount; index++)
        {
            var content = System.Text.Encoding.UTF8.GetBytes($"parallel-object-{index}");
            var digest = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
            var url = $"https://download.hechao.world/objects/{index}";
            objects.Add(url, content);
            files.Add(new ClientManifestFile(
                $"assets/objects/object-{index}.bin",
                content.Length,
                digest,
                url));
        }

        var manifest = ManifestTestData.CreateManifest(objects.Values.First()) with
        {
            Files = files
        };
        var handler = new DelayedObjectResponseHandler(
            objects,
            TimeSpan.FromMilliseconds(75));
        using var httpClient = new HttpClient(handler);
        var installer = new ClientProfileInstaller(
            new ResumableFileDownloader(httpClient),
            maxConcurrentDownloads: maximumConcurrency);
        using var temporary = new TemporaryDirectory();
        var progressSamples = new List<long>();
        var progress = new SynchronousProgress<ClientInstallProgress>(
            value => progressSamples.Add(value.CompletedBytes));

        await installer.InstallAsync(
            new VerifiedClientManifest(manifest, new string('a', 64), "release-2026"),
            new ClientInstallationOptions(temporary.Path),
            progress);

        Assert.Equal(fileCount, handler.RequestCount);
        Assert.InRange(handler.MaximumConcurrentRequests, 2, maximumConcurrency);
        Assert.NotEmpty(progressSamples);
        Assert.True(progressSamples.SequenceEqual(progressSamples.Order()));
        Assert.Equal(files.Sum(file => file.Size), progressSamples[^1]);

        var gameDirectory = new ClientStorageLayout(temporary.Path)
            .GetProfileGameDirectory(manifest.ProfileId);
        for (var index = 0; index < fileCount; index++)
        {
            Assert.Equal(
                objects[$"https://download.hechao.world/objects/{index}"],
                await File.ReadAllBytesAsync(Path.Combine(
                    gameDirectory,
                    "assets",
                    "objects",
                    $"object-{index}.bin")));
        }
    }

    [Fact]
    public async Task InstallAsync_DownloadsDuplicateObjectOnlyOnce()
    {
        var content = "shared-object"u8.ToArray();
        var digest = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        var url = "https://download.hechao.world/objects/shared";
        var manifest = ManifestTestData.CreateManifest(content) with
        {
            Files =
            [
                new ClientManifestFile(
                    "assets/indexes/first.json",
                    content.Length,
                    digest,
                    url),
                new ClientManifestFile(
                    "libraries/example/second.jar",
                    content.Length,
                    digest,
                    url)
            ]
        };
        var handler = new RangeResponseHandler(content);
        using var httpClient = new HttpClient(handler);
        var installer = new ClientProfileInstaller(
            new ResumableFileDownloader(httpClient),
            maxConcurrentDownloads: 4);
        using var temporary = new TemporaryDirectory();

        await installer.InstallAsync(
            new VerifiedClientManifest(manifest, new string('a', 64), "release-2026"),
            new ClientInstallationOptions(temporary.Path));

        var gameDirectory = new ClientStorageLayout(temporary.Path)
            .GetProfileGameDirectory(manifest.ProfileId);
        Assert.Single(handler.RequestedOffsets);
        Assert.Equal(
            content,
            await File.ReadAllBytesAsync(Path.Combine(
                gameDirectory,
                "assets",
                "indexes",
                "first.json")));
        Assert.Equal(
            content,
            await File.ReadAllBytesAsync(Path.Combine(
                gameDirectory,
                "libraries",
                "example",
                "second.jar")));
    }

    private static async Task WriteInstalledProfileAsync(
        string profileDirectory,
        string profileId,
        string version,
        string managedContent)
    {
        var gameDirectory = Path.Combine(
            profileDirectory,
            ClientStorageLayout.GameDirectoryName);
        Directory.CreateDirectory(gameDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(gameDirectory, "managed.txt"),
            managedContent);
        var state = new InstalledProfileState(
            ClientStorageLayout.CurrentStorageSchemaVersion,
            profileId,
            version,
            new string('a', 64),
            "release-test",
            DateTimeOffset.UtcNow);
        await using var stream = new FileStream(
            Path.Combine(
                profileDirectory,
                ClientStorageLayout.InstallStateFileName),
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            16 * 1024,
            FileOptions.Asynchronous);
        await JsonSerializer.SerializeAsync(stream, state);
    }
}

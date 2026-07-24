using System.Text.Json;
using Hechao.Distribution;
using Hechao.Launcher.Services;

namespace Hechao.Launcher.Tests;

public sealed class LauncherSettingsStoreTests
{
    [Fact]
    public async Task Load_MigratesLegacyCustomRootAndPersistsSchemaVersion()
    {
        using var temporary = new TemporaryDirectory();
        var settingsPath = Path.Combine(temporary.Path, "settings.json");
        var legacyRoot = Path.Combine(temporary.Path, "custom-client-root");
        var profileId = "base-1.21.11";
        var legacyProfile = Path.Combine(legacyRoot, profileId);
        Directory.CreateDirectory(Path.Combine(legacyProfile, "versions"));
        await File.WriteAllTextAsync(
            Path.Combine(legacyProfile, "versions", "base.json"),
            "{}");
        await File.WriteAllTextAsync(
            settingsPath,
            JsonSerializer.Serialize(new
            {
                SelectedServerId = "lobby",
                Memory = "8 GB",
                ClientDirectory = legacyRoot,
                CheckForUpdates = true,
                KeepDownloadsAfterClose = true,
                CloseLauncherAfterGameStart = false,
                OpenDownloadsWhenInstalling = true,
                StartupPage = "服务器"
            }));

        var store = new JsonLauncherSettingsStore(
            settingsPath,
            new ClientStorageMigrator());
        var settings = store.Load();
        var layout = new ClientStorageLayout(legacyRoot);

        Assert.Equal(
            ClientStorageLayout.CurrentStorageSchemaVersion,
            settings.StorageSchemaVersion);
        Assert.True(ClientStorageLayout.PathsEqual(legacyRoot, settings.ClientDirectory));
        Assert.True(File.Exists(Path.Combine(
            layout.GetProfileGameDirectory(profileId),
            "versions",
            "base.json")));

        using var saved = JsonDocument.Parse(await File.ReadAllTextAsync(settingsPath));
        Assert.Equal(
            ClientStorageLayout.CurrentStorageSchemaVersion,
            saved.RootElement
                .GetProperty(nameof(LauncherSettings.StorageSchemaVersion))
                .GetInt32());
    }

    [Fact]
    public async Task Load_PreparesCurrentDataRootWithoutMovingUnrelatedFiles()
    {
        using var temporary = new TemporaryDirectory();
        var settingsPath = Path.Combine(temporary.Path, "settings.json");
        var dataRoot = Path.Combine(temporary.Path, "game-data");
        Directory.CreateDirectory(dataRoot);
        await File.WriteAllTextAsync(
            Path.Combine(dataRoot, "readme.txt"),
            "keep");
        var expected = new LauncherSettings(ClientDirectory: dataRoot);
        await File.WriteAllTextAsync(
            settingsPath,
            JsonSerializer.Serialize(expected));

        var settings = new JsonLauncherSettingsStore(
            settingsPath,
            new ClientStorageMigrator()).Load();
        var layout = new ClientStorageLayout(dataRoot);

        Assert.Equal(expected, settings);
        Assert.True(Directory.Exists(layout.InstancesRoot));
        Assert.True(Directory.Exists(layout.ObjectCacheRoot));
        Assert.True(Directory.Exists(layout.RuntimeRoot));
        Assert.Equal(
            "keep",
            await File.ReadAllTextAsync(Path.Combine(dataRoot, "readme.txt")));
    }

    [Fact]
    public async Task Load_RejectsSettingsFromANewerStorageSchema()
    {
        using var temporary = new TemporaryDirectory();
        var settingsPath = Path.Combine(temporary.Path, "settings.json");
        await File.WriteAllTextAsync(
            settingsPath,
            JsonSerializer.Serialize(new LauncherSettings(
                ClientDirectory: temporary.Path,
                StorageSchemaVersion:
                    ClientStorageLayout.CurrentStorageSchemaVersion + 1)));

        var store = new JsonLauncherSettingsStore(
            settingsPath,
            new ClientStorageMigrator());

        Assert.Throws<ClientStorageMigrationException>(() => store.Load());
    }

    [Fact]
    public async Task Load_DoesNotSilentlyResetUnreadableSettings()
    {
        using var temporary = new TemporaryDirectory();
        var settingsPath = Path.Combine(temporary.Path, "settings.json");
        await File.WriteAllTextAsync(settingsPath, "{not-json");
        var store = new JsonLauncherSettingsStore(
            settingsPath,
            new ClientStorageMigrator());

        Assert.Throws<ClientStorageMigrationException>(() => store.Load());
        Assert.Equal("{not-json", await File.ReadAllTextAsync(settingsPath));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "Hechao.Launcher.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}

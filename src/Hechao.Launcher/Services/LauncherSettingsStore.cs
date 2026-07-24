using System.IO;
using System.Text.Json;
using Hechao.Distribution;

namespace Hechao.Launcher.Services;

public sealed record LauncherSettings(
    string SelectedServerId = "lobby",
    string Memory = "6 GB",
    string ClientDirectory = JsonLauncherSettingsStore.DefaultClientDataDirectory,
    bool CheckForUpdates = true,
    bool KeepDownloadsAfterClose = true,
    bool CloseLauncherAfterGameStart = false,
    bool OpenDownloadsWhenInstalling = true,
    string StartupPage = "服务器",
    int StorageSchemaVersion = ClientStorageLayout.CurrentStorageSchemaVersion);

public interface ILauncherSettingsStore
{
    LauncherSettings Load();
    void Save(LauncherSettings settings);
}

public sealed class JsonLauncherSettingsStore : ILauncherSettingsStore
{
    public const string DefaultClientDataDirectory = "%LocalAppData%\\Hechao\\GameData";

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private readonly string _settingsPath;
    private readonly ClientStorageMigrator _storageMigrator;

    public JsonLauncherSettingsStore()
        : this(GetDefaultSettingsPath(), new ClientStorageMigrator())
    {
    }

    internal JsonLauncherSettingsStore(
        string settingsPath,
        ClientStorageMigrator storageMigrator)
    {
        _settingsPath = Path.GetFullPath(settingsPath);
        _storageMigrator = storageMigrator;
    }

    public LauncherSettings Load()
    {
        if (!File.Exists(_settingsPath))
        {
            return InitializeDefaultStorage();
        }

        LauncherSettings settings;
        var hasStorageSchemaVersion = false;
        try
        {
            var json = File.ReadAllText(_settingsPath);
            using var document = JsonDocument.Parse(json);
            hasStorageSchemaVersion = document.RootElement
                .EnumerateObject()
                .Any(property => string.Equals(
                    property.Name,
                    nameof(LauncherSettings.StorageSchemaVersion),
                    StringComparison.OrdinalIgnoreCase));
            settings = JsonSerializer.Deserialize<LauncherSettings>(json) ??
                new LauncherSettings();
        }
        catch (Exception exception) when (
            exception is JsonException or IOException or UnauthorizedAccessException)
        {
            throw new ClientStorageMigrationException(
                "启动器设置文件无法读取；为避免隐藏原有游戏数据，启动器没有重置目录。",
                exception);
        }

        if (hasStorageSchemaVersion &&
            settings.StorageSchemaVersion == ClientStorageLayout.CurrentStorageSchemaVersion)
        {
            EnsureCurrentStorage(settings.ClientDirectory);
            return settings;
        }

        if (hasStorageSchemaVersion &&
            settings.StorageSchemaVersion > ClientStorageLayout.CurrentStorageSchemaVersion)
        {
            throw new ClientStorageMigrationException(
                "当前启动器版本无法读取由更高版本创建的游戏数据设置。");
        }

        return UpgradeLegacyStorage(settings);
    }

    public void Save(LauncherSettings settings)
    {
        var directory = Path.GetDirectoryName(_settingsPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = _settingsPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, SerializerOptions));
        File.Move(temporaryPath, _settingsPath, true);
    }

    private LauncherSettings InitializeDefaultStorage()
    {
        var settings = new LauncherSettings();
        var legacyRoot = ClientStorageLayout.GetLegacyDefaultInstancesRoot();
        MigrateStorage(legacyRoot, settings.ClientDirectory);
        SaveStorageSettings(settings);
        return settings;
    }

    private LauncherSettings UpgradeLegacyStorage(LauncherSettings settings)
    {
        var legacyRoot = string.IsNullOrWhiteSpace(settings.ClientDirectory)
            ? ClientStorageLayout.GetLegacyDefaultInstancesRoot()
            : settings.ClientDirectory;
        var dataRoot = ClientStorageLayout.PathsEqual(
            legacyRoot,
            ClientStorageLayout.GetLegacyDefaultInstancesRoot())
            ? DefaultClientDataDirectory
            : ClientStorageLayout.NormalizeRoot(legacyRoot);

        MigrateStorage(legacyRoot, dataRoot);
        var upgraded = settings with
        {
            ClientDirectory = dataRoot,
            StorageSchemaVersion = ClientStorageLayout.CurrentStorageSchemaVersion
        };
        SaveStorageSettings(upgraded);
        return upgraded;
    }

    private void EnsureCurrentStorage(string dataRoot)
    {
        try
        {
            new ClientStorageLayout(dataRoot).EnsureBaseDirectories();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            ArgumentException or NotSupportedException)
        {
            throw new ClientStorageMigrationException(
                "无法准备赫朝启动器的游戏数据目录。",
                exception);
        }
    }

    private void MigrateStorage(string legacyRoot, string dataRoot)
    {
        try
        {
            _storageMigrator.Migrate(legacyRoot, dataRoot);
        }
        catch (ClientStorageMigrationException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            ArgumentException or NotSupportedException)
        {
            throw new ClientStorageMigrationException(
                "无法迁移旧版游戏数据；原目录未被主动删除。",
                exception);
        }
    }

    private void SaveStorageSettings(LauncherSettings settings)
    {
        try
        {
            Save(settings);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            throw new ClientStorageMigrationException(
                "游戏数据目录已准备完成，但启动器无法保存新的目录设置。",
                exception);
        }
    }

    private static string GetDefaultSettingsPath()
    {
        var applicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(applicationData, "Hechao", "Launcher", "settings.json");
    }
}

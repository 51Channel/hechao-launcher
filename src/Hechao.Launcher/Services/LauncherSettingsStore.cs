using System.IO;
using System.Text.Json;

namespace Hechao.Launcher.Services;

public sealed record LauncherSettings(
    string SelectedServerId = "lobby",
    string Memory = "6 GB",
    string ClientDirectory = "%AppData%\\Hechao\\instances",
    bool CheckForUpdates = true,
    bool KeepDownloadsAfterClose = true,
    bool CloseLauncherAfterGameStart = false,
    bool OpenDownloadsWhenInstalling = true,
    string StartupPage = "服务器");

public interface ILauncherSettingsStore
{
    LauncherSettings Load();
    void Save(LauncherSettings settings);
}

public sealed class JsonLauncherSettingsStore : ILauncherSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private readonly string _settingsPath;

    public JsonLauncherSettingsStore()
    {
        var applicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _settingsPath = Path.Combine(applicationData, "Hechao", "Launcher", "settings.json");
    }

    public LauncherSettings Load()
    {
        try
        {
            return File.Exists(_settingsPath)
                ? JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(_settingsPath)) ?? new LauncherSettings()
                : new LauncherSettings();
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return new LauncherSettings();
        }
    }

    public void Save(LauncherSettings settings)
    {
        var directory = Path.GetDirectoryName(_settingsPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = _settingsPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, SerializerOptions));
        File.Move(temporaryPath, _settingsPath, true);
    }
}

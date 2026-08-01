using System.IO;
using System.Text.Json;
using Hechao.Distribution;

namespace Hechao.Launcher.Services;

internal sealed record PersistedMinecraftProcess(
    string ProfileId,
    string? ServerId,
    int ProcessId,
    string ExecutablePath,
    DateTimeOffset StartedAt,
    string? DataRoot = null);

internal interface IMinecraftRunningStateStore
{
    PersistedMinecraftProcess? Load();

    void Save(PersistedMinecraftProcess process);

    void ClearIfMatches(int processId, DateTimeOffset startedAt);
}

internal sealed class NullMinecraftRunningStateStore : IMinecraftRunningStateStore
{
    public static NullMinecraftRunningStateStore Instance { get; } = new();

    private NullMinecraftRunningStateStore()
    {
    }

    public PersistedMinecraftProcess? Load() => null;

    public void Save(PersistedMinecraftProcess process)
    {
    }

    public void ClearIfMatches(int processId, DateTimeOffset startedAt)
    {
    }
}

internal sealed class JsonMinecraftRunningStateStore : IMinecraftRunningStateStore
{
    private const int MaximumStateBytes = 16 * 1024;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly object _gate = new();
    private readonly string _statePath;

    public JsonMinecraftRunningStateStore(string statePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statePath);
        _statePath = Path.GetFullPath(statePath);
    }

    public static JsonMinecraftRunningStateStore CreateDefault()
    {
        var localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        return new JsonMinecraftRunningStateStore(Path.Combine(
            localApplicationData,
            "Hechao",
            "Launcher",
            "running-game.json"));
    }

    public PersistedMinecraftProcess? Load()
    {
        lock (_gate)
        {
            return LoadCore();
        }
    }

    public void Save(PersistedMinecraftProcess process)
    {
        ArgumentNullException.ThrowIfNull(process);
        Validate(process);

        lock (_gate)
        {
            var directory = Path.GetDirectoryName(_statePath)
                ?? throw new InvalidOperationException(
                    "The Minecraft process state path has no parent directory.");
            Directory.CreateDirectory(directory);

            var temporaryPath = _statePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(
                    temporaryPath,
                    JsonSerializer.Serialize(process, SerializerOptions));
                File.Move(temporaryPath, _statePath, overwrite: true);
            }
            finally
            {
                try
                {
                    File.Delete(temporaryPath);
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

    public void ClearIfMatches(int processId, DateTimeOffset startedAt)
    {
        lock (_gate)
        {
            var current = LoadCore();
            if (current is null ||
                current.ProcessId != processId ||
                !StartedAtMatches(current.StartedAt, startedAt))
            {
                return;
            }

            try
            {
                File.Delete(_statePath);
            }
            catch (FileNotFoundException)
            {
            }
        }
    }

    private PersistedMinecraftProcess? LoadCore()
    {
        if (!File.Exists(_statePath))
        {
            return null;
        }

        try
        {
            var info = new FileInfo(_statePath);
            if (info.Length is <= 0 or > MaximumStateBytes)
            {
                return null;
            }

            var process = JsonSerializer.Deserialize<PersistedMinecraftProcess>(
                File.ReadAllText(_statePath));
            if (process is null)
            {
                return null;
            }

            Validate(process);
            return process with
            {
                ExecutablePath = Path.GetFullPath(process.ExecutablePath),
                DataRoot = string.IsNullOrWhiteSpace(process.DataRoot)
                    ? null
                    : Path.GetFullPath(process.DataRoot)
            };
        }
        catch (Exception exception) when (
            exception is JsonException or IOException or UnauthorizedAccessException or
            ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static void Validate(PersistedMinecraftProcess process)
    {
        ManifestValidator.ValidateProfileId(process.ProfileId);
        if (!string.IsNullOrWhiteSpace(process.ServerId))
        {
            ManifestValidator.ValidateProfileId(process.ServerId);
        }

        if (process.ProcessId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(process),
                process.ProcessId,
                "The Minecraft process ID must be positive.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(process.ExecutablePath);
        _ = Path.GetFullPath(process.ExecutablePath);
        if (!string.IsNullOrWhiteSpace(process.DataRoot))
        {
            _ = Path.GetFullPath(process.DataRoot);
        }

        if (process.StartedAt == default)
        {
            throw new ArgumentException(
                "The Minecraft process start time is required.",
                nameof(process));
        }
    }

    internal static bool StartedAtMatches(
        DateTimeOffset expected,
        DateTimeOffset actual) =>
        Math.Abs((expected - actual).TotalSeconds) <= 2;
}

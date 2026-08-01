using System.IO;
using System.Text.Json;
using Hechao.Distribution;
using Hechao.Launcher.Infrastructure;

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
    private static readonly TimeSpan DefaultLockWaitTimeout = TimeSpan.FromSeconds(3);
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _statePath;
    private readonly string _lockPath;
    private readonly TimeSpan _lockWaitTimeout;

    public JsonMinecraftRunningStateStore(
        string statePath,
        TimeSpan? lockWaitTimeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statePath);
        _statePath = Path.GetFullPath(statePath);
        _lockPath = _statePath + ".lock";
        _lockWaitTimeout = lockWaitTimeout ?? DefaultLockWaitTimeout;
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
        using var stateLock = PathFileLock.Acquire(_statePath, _lockPath, _lockWaitTimeout);
        return LoadCore();
    }

    public void Save(PersistedMinecraftProcess process)
    {
        ArgumentNullException.ThrowIfNull(process);
        Validate(process);

        using var stateLock = PathFileLock.Acquire(_statePath, _lockPath, _lockWaitTimeout);
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
        using var stateLock = PathFileLock.Acquire(_statePath, _lockPath, _lockWaitTimeout);
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

    internal string LockPath => _lockPath;

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

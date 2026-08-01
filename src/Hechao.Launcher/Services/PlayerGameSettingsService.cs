using System.IO;
using System.Text;
using Hechao.Distribution;
using Hechao.Launcher.Infrastructure;

namespace Hechao.Launcher.Services;

public interface IPlayerGameSettingsService
{
    Task ImportLatestAsync(
        string dataRoot,
        CancellationToken cancellationToken = default);

    Task CaptureProfileAsync(
        string dataRoot,
        string profileId,
        CancellationToken cancellationToken = default);

    Task ApplyToProfileAsync(
        string dataRoot,
        string profileId,
        CancellationToken cancellationToken = default);
}

public sealed class NullPlayerGameSettingsService : IPlayerGameSettingsService
{
    public static NullPlayerGameSettingsService Instance { get; } = new();

    private NullPlayerGameSettingsService()
    {
    }

    public Task ImportLatestAsync(
        string dataRoot,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task CaptureProfileAsync(
        string dataRoot,
        string profileId,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task ApplyToProfileAsync(
        string dataRoot,
        string profileId,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public sealed class PlayerGameSettingsService : IPlayerGameSettingsService
{
    private static readonly HashSet<string> ProfileScopedKeys = new(
        [
            "resourcePacks",
            "resourcePack",
            "incompatibleResourcePacks",
            "serverResourcePacks",
            "version",
            "lastServer"
        ],
        StringComparer.OrdinalIgnoreCase);

    private static readonly UTF8Encoding Utf8WithoutBom = new(false);
    private static readonly TimeSpan DefaultLockWaitTimeout = TimeSpan.FromSeconds(5);
    private const string PlayerSettingsLockFileName = "player-settings.lock";

    private readonly TimeSpan _lockWaitTimeout;

    public PlayerGameSettingsService(TimeSpan? lockWaitTimeout = null)
    {
        _lockWaitTimeout = lockWaitTimeout ?? DefaultLockWaitTimeout;
    }

    public Task ImportLatestAsync(
        string dataRoot,
        CancellationToken cancellationToken = default) =>
        RunLockedAsync(
            dataRoot,
            ImportLatestCoreAsync,
            cancellationToken);

    public Task CaptureProfileAsync(
        string dataRoot,
        string profileId,
        CancellationToken cancellationToken = default)
    {
        ManifestValidator.ValidateProfileId(profileId);
        return RunLockedAsync(
            dataRoot,
            (layout, token) => ImportCandidatesAsync(
                layout,
                [layout.GetProfileGameDirectory(profileId)],
                token),
            cancellationToken);
    }

    public Task ApplyToProfileAsync(
        string dataRoot,
        string profileId,
        CancellationToken cancellationToken = default)
    {
        ManifestValidator.ValidateProfileId(profileId);
        return RunLockedAsync(
            dataRoot,
            async (layout, token) =>
            {
                if (!File.Exists(layout.PlayerOptionsPath))
                {
                    return;
                }

                var targetPath = Path.Combine(
                    layout.GetProfileGameDirectory(profileId),
                    "options.txt");
                var targetExists = File.Exists(targetPath);
                var shared = await OptionDocument.ReadAsync(
                    layout.PlayerOptionsPath,
                    token).ConfigureAwait(false);
                var target = targetExists
                    ? await OptionDocument.ReadAsync(targetPath, token).ConfigureAwait(false)
                    : new OptionDocument();

                var changed = false;
                foreach (var (key, value) in shared.Settings)
                {
                    if (ProfileScopedKeys.Contains(key))
                    {
                        continue;
                    }

                    if (IsKeyBinding(key) && targetExists && !target.Contains(key))
                    {
                        continue;
                    }

                    changed |= target.Set(key, value);
                }

                if (changed || !targetExists)
                {
                    await WriteAtomicAsync(targetPath, target.ToLines(), token)
                        .ConfigureAwait(false);
                }
            },
            cancellationToken);
    }

    private async Task RunLockedAsync(
        string dataRoot,
        Func<ClientStorageLayout, CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        try
        {
            var layout = new ClientStorageLayout(dataRoot);
            layout.EnsureBaseDirectories();

            using var playerSettingsLock = await PathFileLock.AcquireAsync(
                    layout.DataRoot,
                    Path.Combine(layout.LocksRoot, PlayerSettingsLockFileName),
                    _lockWaitTimeout,
                    cancellationToken)
                .ConfigureAwait(false);

            await action(layout, cancellationToken).ConfigureAwait(false);
        }
        catch (PathFileLockTimeoutException exception)
        {
            throw new PlayerGameSettingsException(
                "Timed out waiting for exclusive access to Minecraft player settings.",
                exception);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            throw new PlayerGameSettingsException(
                "Unable to synchronize Minecraft player settings.",
                exception);
        }
    }

    internal static string GetLockPath(string dataRoot)
    {
        var layout = new ClientStorageLayout(dataRoot);
        return Path.Combine(layout.LocksRoot, PlayerSettingsLockFileName);
    }

    private static Task ImportLatestCoreAsync(
        ClientStorageLayout layout,
        CancellationToken cancellationToken)
    {
        var profileDirectories = Directory
            .EnumerateDirectories(
                layout.InstancesRoot,
                "*",
                SearchOption.TopDirectoryOnly)
            .Where(path => !Path.GetFileName(path).StartsWith(".", StringComparison.Ordinal))
            .Select(path => Path.Combine(path, ClientStorageLayout.GameDirectoryName));

        return ImportCandidatesAsync(layout, profileDirectories, cancellationToken);
    }

    private static async Task ImportCandidatesAsync(
        ClientStorageLayout layout,
        IEnumerable<string> gameDirectories,
        CancellationToken cancellationToken)
    {
        var sharedExists = File.Exists(layout.PlayerOptionsPath);
        var sharedWriteTime = sharedExists
            ? File.GetLastWriteTimeUtc(layout.PlayerOptionsPath)
            : DateTime.MinValue;
        var candidates = gameDirectories
            .Select(path => Path.Combine(path, "options.txt"))
            .Where(File.Exists)
            .Select(path => new OptionCandidate(path, File.GetLastWriteTimeUtc(path)))
            .Where(candidate => !sharedExists || candidate.LastWriteTimeUtc > sharedWriteTime)
            .OrderBy(candidate => candidate.LastWriteTimeUtc)
            .ThenBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (candidates.Length == 0)
        {
            return;
        }

        var shared = sharedExists
            ? await OptionDocument.ReadAsync(layout.PlayerOptionsPath, cancellationToken)
                .ConfigureAwait(false)
            : new OptionDocument();
        var changed = !sharedExists;

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = await OptionDocument.ReadAsync(candidate.Path, cancellationToken)
                .ConfigureAwait(false);
            foreach (var (key, value) in source.Settings)
            {
                if (!ProfileScopedKeys.Contains(key))
                {
                    changed |= shared.Set(key, value);
                }
            }
        }

        if (changed)
        {
            await WriteAtomicAsync(
                    layout.PlayerOptionsPath,
                    shared.ToLines(),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            File.SetLastWriteTimeUtc(layout.PlayerOptionsPath, DateTime.UtcNow);
        }
    }

    private static bool IsKeyBinding(string key) =>
        key.StartsWith("key_", StringComparison.Ordinal);

    private static async Task WriteAtomicAsync(
        string path,
        IReadOnlyList<string> lines,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("The player settings path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await File.WriteAllLinesAsync(
                    temporaryPath,
                    lines,
                    Utf8WithoutBom,
                    cancellationToken)
                .ConfigureAwait(false);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private sealed record OptionCandidate(
        string Path,
        DateTime LastWriteTimeUtc);

    private sealed class OptionDocument
    {
        private readonly List<OptionEntry> _entries = [];
        private readonly Dictionary<string, int> _indexes = new(StringComparer.Ordinal);

        public IEnumerable<KeyValuePair<string, string>> Settings =>
            _entries
                .Where(entry => entry.Key is not null)
                .Select(entry => new KeyValuePair<string, string>(entry.Key!, entry.Value));

        public bool Contains(string key) => _indexes.ContainsKey(key);

        public bool Set(string key, string value)
        {
            if (_indexes.TryGetValue(key, out var index))
            {
                if (string.Equals(_entries[index].Value, value, StringComparison.Ordinal))
                {
                    return false;
                }

                _entries[index] = new OptionEntry(key, value);
                return true;
            }

            _indexes.Add(key, _entries.Count);
            _entries.Add(new OptionEntry(key, value));
            return true;
        }

        public IReadOnlyList<string> ToLines() =>
            _entries
                .Select(entry => entry.Key is null
                    ? entry.Value
                    : $"{entry.Key}:{entry.Value}")
                .ToArray();

        public static async Task<OptionDocument> ReadAsync(
            string path,
            CancellationToken cancellationToken)
        {
            var document = new OptionDocument();
            var lines = await File.ReadAllLinesAsync(path, cancellationToken)
                .ConfigureAwait(false);
            foreach (var line in lines)
            {
                var separator = line.IndexOf(':');
                if (separator <= 0)
                {
                    document._entries.Add(new OptionEntry(null, line));
                    continue;
                }

                var key = line[..separator];
                var value = line[(separator + 1)..];
                if (document._indexes.TryGetValue(key, out var existingIndex))
                {
                    document._entries[existingIndex] = new OptionEntry(key, value);
                    continue;
                }

                document._indexes.Add(key, document._entries.Count);
                document._entries.Add(new OptionEntry(key, value));
            }

            return document;
        }

        private sealed record OptionEntry(string? Key, string Value);
    }
}

public sealed class PlayerGameSettingsException : IOException
{
    public PlayerGameSettingsException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

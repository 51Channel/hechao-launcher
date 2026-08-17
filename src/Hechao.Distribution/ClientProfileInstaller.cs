using System.Text.Json;

namespace Hechao.Distribution;

public enum ClientInstallPhase
{
    Checking,
    Downloading,
    Staging,
    Switching,
    PreparingRuntime,
    Complete
}

public enum LocalProfileState
{
    Missing,
    UpdateRequired,
    Ready
}

public sealed record ClientInstallProgress(
    ClientInstallPhase Phase,
    double Percent,
    string CurrentPath,
    long CompletedBytes,
    long TotalBytes);

public sealed record InstalledProfileState(
    int SchemaVersion,
    string ProfileId,
    string Version,
    string ManifestSha256,
    string SigningKeyId,
    DateTimeOffset InstalledAt);

public sealed record ClientInstallationOptions(
    string DataRoot,
    bool KeepObjectCache = true);

public sealed class ClientProfileInstaller(
    ResumableFileDownloader downloader,
    AtomicProfileDirectorySwitcher? directorySwitcher = null,
    int maxConcurrentDownloads = 16)
{
    public const int DefaultMaxConcurrentDownloads = 16;

    private static readonly string[] PreservedGamePaths =
    [
        "saves",
        "screenshots",
        "resourcepacks",
        "shaderpacks",
        "logs",
        "crash-reports",
        "options.txt",
        "optionsof.txt",
        "servers.dat"
    ];

    private static readonly string[] ProtectedGamePaths =
    [
        "saves",
        "screenshots",
        "logs",
        "crash-reports",
        "options.txt",
        "optionsof.txt",
        "servers.dat"
    ];

    private static readonly JsonSerializerOptions StateJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly AtomicProfileDirectorySwitcher _directorySwitcher =
        directorySwitcher ?? new AtomicProfileDirectorySwitcher();

    private readonly int _maxConcurrentDownloads =
        maxConcurrentDownloads is >= 1 and <= 64
            ? maxConcurrentDownloads
            : throw new ArgumentOutOfRangeException(
                nameof(maxConcurrentDownloads),
                maxConcurrentDownloads,
                "Concurrent downloads must be between 1 and 64.");

    public async Task<LocalProfileState> GetLocalStateAsync(
        string dataRoot,
        string profileId,
        string expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ManifestValidator.ValidateProfileId(profileId);
        var layout = new ClientStorageLayout(dataRoot);
        var activeDirectory = layout.GetProfileRoot(profileId);
        var gameDirectory = layout.GetProfileGameDirectory(profileId);
        var statePath = Path.Combine(
            activeDirectory,
            ClientStorageLayout.InstallStateFileName);
        if (!File.Exists(statePath) || !Directory.Exists(gameDirectory))
        {
            return LocalProfileState.Missing;
        }

        try
        {
            await using var stream = File.OpenRead(statePath);
            var state = await JsonSerializer.DeserializeAsync<InstalledProfileState>(
                stream,
                StateJsonOptions,
                cancellationToken);
            if (state is null ||
                state.SchemaVersion != ClientStorageLayout.CurrentStorageSchemaVersion ||
                !string.Equals(state.ProfileId, profileId, StringComparison.Ordinal))
            {
                return LocalProfileState.Missing;
            }

            return string.Equals(state.Version, expectedVersion, StringComparison.Ordinal)
                ? LocalProfileState.Ready
                : LocalProfileState.UpdateRequired;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return LocalProfileState.Missing;
        }
    }

    public Task<InstalledProfileState?> GetPreviousStateAsync(
        string dataRoot,
        string profileId,
        CancellationToken cancellationToken = default)
    {
        ManifestValidator.ValidateProfileId(profileId);
        var layout = new ClientStorageLayout(dataRoot);
        return ReadInstalledStateAsync(
            layout.GetPreviousProfileRoot(profileId),
            profileId,
            cancellationToken);
    }

    public Task<bool> DeleteAsync(
        string dataRoot,
        string profileId,
        CancellationToken cancellationToken = default)
    {
        ManifestValidator.ValidateProfileId(profileId);
        var layout = new ClientStorageLayout(dataRoot);
        layout.EnsureBaseDirectories();

        return Task.Run(
            () =>
            {
                using var installationLock = AcquireInstallationLock(layout, profileId);
                cancellationToken.ThrowIfCancellationRequested();

                var stagingPrefix = $".{profileId}.staging-";
                var paths = new List<string>
                {
                    layout.GetProfileRoot(profileId),
                    layout.GetPreviousProfileRoot(profileId)
                };
                paths.AddRange(
                    Directory.EnumerateDirectories(
                            layout.InstancesRoot,
                            "*",
                            SearchOption.TopDirectoryOnly)
                        .Where(path => Path.GetFileName(path).StartsWith(
                            stagingPrefix,
                            StringComparison.OrdinalIgnoreCase)));

                var deleted = false;
                foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!Directory.Exists(path))
                    {
                        continue;
                    }

                    DeleteDirectoryTree(path, cancellationToken);
                    deleted = true;
                }

                return deleted;
            },
            cancellationToken);
    }

    public async Task<InstalledProfileState> RollbackAsync(
        string dataRoot,
        string profileId,
        CancellationToken cancellationToken = default)
    {
        ManifestValidator.ValidateProfileId(profileId);
        var layout = new ClientStorageLayout(dataRoot);
        layout.EnsureBaseDirectories();
        var activeDirectory = layout.GetProfileRoot(profileId);
        var activeGameDirectory = layout.GetProfileGameDirectory(profileId);
        var previousDirectory = layout.GetPreviousProfileRoot(profileId);
        var previousGameDirectory = Path.Combine(
            previousDirectory,
            ClientStorageLayout.GameDirectoryName);
        var stagingDirectory = layout.CreateStagingProfileRoot(profileId);
        var stagingGameDirectory = Path.Combine(
            stagingDirectory,
            ClientStorageLayout.GameDirectoryName);

        await using var installationLock = AcquireInstallationLock(layout, profileId);
        var activeState = await ReadInstalledStateAsync(
            activeDirectory,
            profileId,
            cancellationToken);
        var previousState = await ReadInstalledStateAsync(
            previousDirectory,
            profileId,
            cancellationToken);
        if (activeState is null || previousState is null ||
            !Directory.Exists(activeGameDirectory) ||
            !Directory.Exists(previousGameDirectory))
        {
            throw new ProfileRollbackUnavailableException(profileId);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var preserveStagingAfterFailure = false;
        try
        {
            CloneDirectory(
                previousDirectory,
                stagingDirectory,
                preferHardLinks: true,
                cancellationToken);
            PreserveWritableGameData(
                activeGameDirectory,
                stagingGameDirectory,
                replaceDestination: true,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _directorySwitcher.Switch(
                stagingDirectory,
                activeDirectory,
                previousDirectory);
            return previousState;
        }
        catch (ProfileRollbackException)
        {
            preserveStagingAfterFailure = true;
            throw;
        }
        finally
        {
            if (!preserveStagingAfterFailure)
            {
                TryDeleteDirectory(stagingDirectory);
            }
        }
    }

    public async Task InstallAsync(
        VerifiedClientManifest verifiedManifest,
        ClientInstallationOptions options,
        IProgress<ClientInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(verifiedManifest);
        ArgumentNullException.ThrowIfNull(options);
        ManifestValidator.Validate(verifiedManifest.Manifest);

        var manifest = verifiedManifest.Manifest;
        var layout = new ClientStorageLayout(options.DataRoot);
        layout.EnsureBaseDirectories();
        var activeDirectory = layout.GetProfileRoot(manifest.ProfileId);
        var activeGameDirectory = layout.GetProfileGameDirectory(manifest.ProfileId);
        var stagingDirectory = layout.CreateStagingProfileRoot(manifest.ProfileId);
        var stagingGameDirectory = Path.Combine(
            stagingDirectory,
            ClientStorageLayout.GameDirectoryName);
        var previousDirectory = layout.GetPreviousProfileRoot(manifest.ProfileId);
        await using var installationLock = AcquireInstallationLock(layout, manifest.ProfileId);

        EnsureDiskSpace(layout.DataRoot, manifest);
        Directory.CreateDirectory(stagingGameDirectory);
        PreserveWritableGameData(
            activeGameDirectory,
            stagingGameDirectory,
            replaceDestination: false,
            cancellationToken);
        ApplyDeletePaths(stagingGameDirectory, manifest.DeletePaths);

        var totalBytes = manifest.Files.Sum(file => file.Size);
        long checkedBytes = 0;
        var usedCachePaths = new List<string>(manifest.Files.Count);
        var pendingFiles = new List<PendingInstallFile>(manifest.Files.Count);
        var preserveStagingAfterSwitchFailure = false;

        try
        {
            foreach (var file in manifest.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new ClientInstallProgress(
                    ClientInstallPhase.Checking,
                    CalculatePercent(checkedBytes, totalBytes, 0, 10),
                    file.Path,
                    checkedBytes,
                    totalBytes));

                var stagedPath = ManifestValidator.ResolveManagedPath(stagingGameDirectory, file.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(stagedPath)!);
                if (IsProtectedGamePath(file.Path) && File.Exists(stagedPath))
                {
                    checkedBytes = checked(checkedBytes + file.Size);
                    progress?.Report(new ClientInstallProgress(
                        ClientInstallPhase.Checking,
                        CalculatePercent(checkedBytes, totalBytes, 0, 10),
                        file.Path,
                        checkedBytes,
                        totalBytes));
                    continue;
                }

                var activePath = ManifestValidator.ResolveManagedPath(activeGameDirectory, file.Path);
                var normalizedDigest = file.Sha256.ToLowerInvariant();
                var cachePath = Path.Combine(
                    layout.ObjectCacheRoot,
                    normalizedDigest[..2],
                    normalizedDigest);
                usedCachePaths.Add(cachePath);

                if (await FileHashing.MatchesAsync(
                        activePath,
                        file.Size,
                        file.Sha256,
                        cancellationToken))
                {
                    if (IsShareablePath(file.Path) && options.KeepObjectCache)
                    {
                        await EnsureCachedFromActiveAsync(
                            activePath,
                            cachePath,
                            file,
                            cancellationToken);
                        MaterializeFile(cachePath, stagedPath, preferHardLink: true);
                    }
                    else
                    {
                        File.Copy(activePath, stagedPath, overwrite: true);
                    }
                }
                else
                {
                    if (await FileHashing.MatchesAsync(
                            cachePath,
                            file.Size,
                            file.Sha256,
                            cancellationToken))
                    {
                        MaterializeFile(
                            cachePath,
                            stagedPath,
                            preferHardLink: IsShareablePath(file.Path));
                    }
                    else
                    {
                        pendingFiles.Add(new PendingInstallFile(file, cachePath, stagedPath));
                    }
                }

                checkedBytes = checked(checkedBytes + file.Size);
                progress?.Report(new ClientInstallProgress(
                    ClientInstallPhase.Checking,
                    CalculatePercent(checkedBytes, totalBytes, 0, 10),
                    file.Path,
                    checkedBytes,
                    totalBytes));
            }

            if (pendingFiles.Count > 0)
            {
                await DownloadPendingFilesAsync(
                    pendingFiles,
                    progress,
                    cancellationToken);

                var pendingBytes = pendingFiles.Sum(item => item.File.Size);
                long stagedBytes = 0;
                var lastStagingReport = Environment.TickCount64;
                for (var index = 0; index < pendingFiles.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var pendingFile = pendingFiles[index];
                    MaterializeFile(
                        pendingFile.CachePath,
                        pendingFile.StagedPath,
                        preferHardLink: IsShareablePath(pendingFile.File.Path));
                    stagedBytes = checked(stagedBytes + pendingFile.File.Size);

                    var now = Environment.TickCount64;
                    if (index == pendingFiles.Count - 1 ||
                        now - lastStagingReport >= 100)
                    {
                        lastStagingReport = now;
                        progress?.Report(new ClientInstallProgress(
                            ClientInstallPhase.Staging,
                            CalculatePercent(stagedBytes, pendingBytes, 80, 19),
                            pendingFile.File.Path,
                            stagedBytes,
                            pendingBytes));
                    }
                }
            }
            else
            {
                progress?.Report(new ClientInstallProgress(
                    ClientInstallPhase.Staging,
                    99,
                    string.Empty,
                    totalBytes,
                    totalBytes));
            }

            await WriteStateAsync(stagingDirectory, verifiedManifest, cancellationToken);
            progress?.Report(new ClientInstallProgress(
                ClientInstallPhase.Switching,
                99,
                string.Empty,
                totalBytes,
                totalBytes));
            _directorySwitcher.Switch(stagingDirectory, activeDirectory, previousDirectory);

            if (!options.KeepObjectCache)
            {
                foreach (var cachePath in usedCachePaths.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    TryDeleteFile(cachePath);
                }
            }

            progress?.Report(new ClientInstallProgress(
                ClientInstallPhase.Complete,
                100,
                string.Empty,
                totalBytes,
                totalBytes));
        }
        catch (ProfileRollbackException)
        {
            preserveStagingAfterSwitchFailure = true;
            throw;
        }
        finally
        {
            if (!preserveStagingAfterSwitchFailure)
            {
                TryDeleteDirectory(stagingDirectory);
            }
        }
    }

    private async Task DownloadPendingFilesAsync(
        IReadOnlyList<PendingInstallFile> pendingFiles,
        IProgress<ClientInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        var groups = pendingFiles
            .GroupBy(item => item.CachePath, StringComparer.OrdinalIgnoreCase)
            .Select(group => new PendingDownloadGroup(
                group.First().File,
                group.Key,
                group.ToArray()))
            .ToArray();
        var totalDownloadBytes = groups.Sum(group => group.File.Size);
        var groupProgress = new long[groups.Length];
        var progressGate = new object();
        long aggregateDownloadedBytes = 0;
        long lastReportedBytes = 0;
        var lastReportTick = Environment.TickCount64;

        progress?.Report(new ClientInstallProgress(
            ClientInstallPhase.Downloading,
            10,
            string.Empty,
            0,
            totalDownloadBytes));

        void ReportDownloadProgress(
            int groupIndex,
            PendingDownloadGroup group,
            FileDownloadProgress value)
        {
            lock (progressGate)
            {
                var objectBytes = Math.Clamp(value.BytesDownloaded, 0, group.File.Size);
                if (objectBytes <= groupProgress[groupIndex])
                {
                    return;
                }

                aggregateDownloadedBytes = checked(
                    aggregateDownloadedBytes +
                    objectBytes -
                    groupProgress[groupIndex]);
                groupProgress[groupIndex] = objectBytes;
                var currentBytes = Math.Min(
                    totalDownloadBytes,
                    aggregateDownloadedBytes);
                var now = Environment.TickCount64;
                if (currentBytes < totalDownloadBytes && now - lastReportTick < 100)
                {
                    return;
                }

                lastReportTick = now;
                lastReportedBytes = Math.Max(lastReportedBytes, currentBytes);
                progress?.Report(new ClientInstallProgress(
                    ClientInstallPhase.Downloading,
                    CalculatePercent(lastReportedBytes, totalDownloadBytes, 10, 70),
                    group.File.Path,
                    lastReportedBytes,
                    totalDownloadBytes));
            }
        }

        await Parallel.ForEachAsync(
            Enumerable.Range(0, groups.Length),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = _maxConcurrentDownloads,
                CancellationToken = cancellationToken
            },
            async (groupIndex, workerCancellationToken) =>
            {
                var group = groups[groupIndex];
                var downloadProgress = new InlineProgress<FileDownloadProgress>(
                    value => ReportDownloadProgress(groupIndex, group, value));
                await downloader.DownloadAsync(
                    group.File,
                    group.CachePath,
                    downloadProgress,
                    workerCancellationToken);
            });

        progress?.Report(new ClientInstallProgress(
            ClientInstallPhase.Downloading,
            80,
            string.Empty,
            totalDownloadBytes,
            totalDownloadBytes));
    }

    private static async Task WriteStateAsync(
        string stagingDirectory,
        VerifiedClientManifest verifiedManifest,
        CancellationToken cancellationToken)
    {
        var state = new InstalledProfileState(
            ClientStorageLayout.CurrentStorageSchemaVersion,
            verifiedManifest.Manifest.ProfileId,
            verifiedManifest.Manifest.Version,
            verifiedManifest.EnvelopeSha256,
            verifiedManifest.KeyId,
            DateTimeOffset.UtcNow);
        var statePath = Path.Combine(
            stagingDirectory,
            ClientStorageLayout.InstallStateFileName);
        await using var stream = new FileStream(
            statePath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            16 * 1024,
            FileOptions.Asynchronous);
        await JsonSerializer.SerializeAsync(stream, state, StateJsonOptions, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static FileStream AcquireInstallationLock(
        ClientStorageLayout layout,
        string profileId)
    {
        Directory.CreateDirectory(layout.LocksRoot);
        var lockPath = Path.Combine(layout.LocksRoot, profileId + ".lock");
        try
        {
            return new FileStream(
                lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                4096,
                FileOptions.Asynchronous);
        }
        catch (IOException exception)
        {
            throw new ProfileInstallInProgressException(profileId, exception);
        }
    }

    private static void EnsureDiskSpace(
        string dataRoot,
        ClientManifest manifest)
    {
        long stageBytes = 0;
        foreach (var file in manifest.Files)
        {
            stageBytes = checked(stageBytes + file.Size);
        }

        var requiredBytes = checked(stageBytes * 2 + 32L * 1024 * 1024);
        var root = Path.GetPathRoot(Path.GetFullPath(dataRoot));
        if (string.IsNullOrWhiteSpace(root))
        {
            return;
        }

        var drive = new DriveInfo(root);
        if (drive.IsReady && drive.AvailableFreeSpace < requiredBytes)
        {
            throw new InsufficientDiskSpaceException(requiredBytes, drive.AvailableFreeSpace);
        }
    }

    private static double CalculatePercent(long completedBytes, long totalBytes, double offset, double span)
    {
        if (totalBytes <= 0)
        {
            return offset + span;
        }

        return Math.Clamp(offset + (completedBytes / (double)totalBytes * span), 0, 100);
    }

    private static async Task<InstalledProfileState?> ReadInstalledStateAsync(
        string profileDirectory,
        string profileId,
        CancellationToken cancellationToken)
    {
        var gameDirectory = Path.Combine(
            profileDirectory,
            ClientStorageLayout.GameDirectoryName);
        var statePath = Path.Combine(
            profileDirectory,
            ClientStorageLayout.InstallStateFileName);
        if (!Directory.Exists(profileDirectory) ||
            !Directory.Exists(gameDirectory) ||
            !File.Exists(statePath))
        {
            return null;
        }

        try
        {
            RejectReparsePoint(profileDirectory);
            RejectReparsePoint(gameDirectory);
            RejectReparsePoint(statePath);
            await using var stream = File.OpenRead(statePath);
            var state = await JsonSerializer.DeserializeAsync<InstalledProfileState>(
                stream,
                StateJsonOptions,
                cancellationToken);
            return state is not null &&
                   state.SchemaVersion == ClientStorageLayout.CurrentStorageSchemaVersion &&
                   string.Equals(state.ProfileId, profileId, StringComparison.Ordinal)
                ? state
                : null;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static void PreserveWritableGameData(
        string activeGameDirectory,
        string stagingGameDirectory,
        bool replaceDestination,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(activeGameDirectory))
        {
            return;
        }

        RejectReparsePoint(activeGameDirectory);
        foreach (var relativePath in PreservedGamePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = ManifestValidator.ResolveManagedPath(
                activeGameDirectory,
                relativePath);
            var destination = ManifestValidator.ResolveManagedPath(
                stagingGameDirectory,
                relativePath);
            if (replaceDestination)
            {
                DeleteEntry(destination);
            }

            if (File.Exists(source))
            {
                RejectReparsePoint(source);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination, overwrite: true);
            }
            else if (Directory.Exists(source))
            {
                CloneDirectory(
                    source,
                    destination,
                    preferHardLinks: false,
                    cancellationToken);
            }
        }
    }

    private static void ApplyDeletePaths(
        string stagingGameDirectory,
        IReadOnlyList<string> deletePaths)
    {
        foreach (var relativePath in deletePaths)
        {
            if (IsProtectedGamePath(relativePath))
            {
                continue;
            }

            var path = ManifestValidator.ResolveManagedPath(
                stagingGameDirectory,
                relativePath);
            if (File.Exists(path))
            {
                RejectReparsePoint(path);
                File.Delete(path);
            }
            else if (Directory.Exists(path))
            {
                RejectReparsePoint(path);
                Directory.Delete(path, recursive: true);
            }
        }
    }

    private static async Task EnsureCachedFromActiveAsync(
        string activePath,
        string cachePath,
        ClientManifestFile file,
        CancellationToken cancellationToken)
    {
        if (await FileHashing.MatchesAsync(
                cachePath,
                file.Size,
                file.Sha256,
                cancellationToken))
        {
            return;
        }

        TryDeleteFile(cachePath);
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        if (!HardLinkFile.TryCreate(cachePath, activePath))
        {
            File.Copy(activePath, cachePath, overwrite: false);
        }
    }

    private static void MaterializeFile(
        string sourcePath,
        string destinationPath,
        bool preferHardLink)
    {
        TryDeleteFile(destinationPath);
        if (!preferHardLink || !HardLinkFile.TryCreate(destinationPath, sourcePath))
        {
            File.Copy(sourcePath, destinationPath, overwrite: true);
        }
    }

    private static bool IsShareablePath(string relativePath) =>
        relativePath.StartsWith("assets/", StringComparison.OrdinalIgnoreCase) ||
        relativePath.StartsWith("libraries/", StringComparison.OrdinalIgnoreCase);

    private static bool IsProtectedGamePath(string relativePath) =>
        ProtectedGamePaths.Any(protectedPath =>
            string.Equals(
                relativePath,
                protectedPath,
                StringComparison.OrdinalIgnoreCase) ||
            relativePath.StartsWith(
                protectedPath + "/",
                StringComparison.OrdinalIgnoreCase));

    private static void CloneDirectory(
        string source,
        string destination,
        bool preferHardLinks,
        CancellationToken cancellationToken)
    {
        RejectReparsePoint(source);
        Directory.CreateDirectory(destination);
        foreach (var entry in Directory.EnumerateFileSystemEntries(source))
        {
            cancellationToken.ThrowIfCancellationRequested();
            RejectReparsePoint(entry);
            var destinationEntry = Path.Combine(destination, Path.GetFileName(entry));
            if (Directory.Exists(entry))
            {
                CloneDirectory(
                    entry,
                    destinationEntry,
                    preferHardLinks,
                    cancellationToken);
            }
            else if (!preferHardLinks ||
                     !HardLinkFile.TryCreate(destinationEntry, entry))
            {
                File.Copy(entry, destinationEntry, overwrite: true);
            }
        }
    }

    private static void DeleteEntry(string path)
    {
        if (File.Exists(path))
        {
            RejectReparsePoint(path);
            File.Delete(path);
        }
        else if (Directory.Exists(path))
        {
            RejectReparsePoint(path);
            Directory.Delete(path, recursive: true);
        }
    }

    private static void RejectReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                $"The client profile contains an unsupported link: {path}");
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void DeleteDirectoryTree(
        string path,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Directory.Exists(path))
        {
            return;
        }

        var rootAttributes = File.GetAttributes(path);
        if ((rootAttributes & FileAttributes.ReparsePoint) != 0)
        {
            Directory.Delete(path, recursive: false);
            return;
        }

        foreach (var entry in Directory.EnumerateFileSystemEntries(path))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.Directory) != 0)
            {
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    Directory.Delete(entry, recursive: false);
                }
                else
                {
                    DeleteDirectoryTree(entry, cancellationToken);
                }

                continue;
            }

            if ((attributes & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(entry, attributes & ~FileAttributes.ReadOnly);
            }

            File.Delete(entry);
        }

        File.SetAttributes(path, FileAttributes.Normal);
        Directory.Delete(path, recursive: false);
    }

    private sealed record PendingInstallFile(
        ClientManifestFile File,
        string CachePath,
        string StagedPath);

    private sealed record PendingDownloadGroup(
        ClientManifestFile File,
        string CachePath,
        IReadOnlyList<PendingInstallFile> Files);

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}

public sealed class InsufficientDiskSpaceException(long requiredBytes, long availableBytes)
    : IOException($"The installation requires {requiredBytes} bytes but only {availableBytes} bytes are available.")
{
    public long RequiredBytes { get; } = requiredBytes;
    public long AvailableBytes { get; } = availableBytes;
}

public sealed class ProfileInstallInProgressException(string profileId, Exception innerException)
    : IOException($"Another process is already installing profile {profileId}.", innerException);

public sealed class ProfileRollbackUnavailableException(string profileId)
    : IOException($"Profile {profileId} does not have a valid previous installation.");

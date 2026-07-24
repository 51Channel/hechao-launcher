using System.Text.Json;

namespace Hechao.Distribution;

public enum ClientInstallPhase
{
    Checking,
    Downloading,
    Staging,
    Switching,
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
    AtomicProfileDirectorySwitcher? directorySwitcher = null)
{
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
        PreserveWritableGameData(activeGameDirectory, stagingGameDirectory);
        ApplyDeletePaths(stagingGameDirectory, manifest.DeletePaths);

        var totalBytes = manifest.Files.Sum(file => file.Size);
        long completedBytes = 0;
        var usedCachePaths = new List<string>(manifest.Files.Count);

        try
        {
            foreach (var file in manifest.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new ClientInstallProgress(
                    ClientInstallPhase.Checking,
                    CalculatePercent(completedBytes, totalBytes, 0, 10),
                    file.Path,
                    completedBytes,
                    totalBytes));

                var stagedPath = ManifestValidator.ResolveManagedPath(stagingGameDirectory, file.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(stagedPath)!);
                if (IsProtectedGamePath(file.Path) && File.Exists(stagedPath))
                {
                    completedBytes = checked(completedBytes + file.Size);
                    progress?.Report(new ClientInstallProgress(
                        ClientInstallPhase.Staging,
                        CalculatePercent(completedBytes, totalBytes, 10, 80),
                        file.Path,
                        completedBytes,
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
                    var fileStartBytes = completedBytes;
                    var downloadProgress = new Progress<FileDownloadProgress>(value =>
                    {
                        var currentCompleted = Math.Min(totalBytes, fileStartBytes + value.BytesDownloaded);
                        progress?.Report(new ClientInstallProgress(
                            ClientInstallPhase.Downloading,
                            CalculatePercent(currentCompleted, totalBytes, 10, 80),
                            file.Path,
                            currentCompleted,
                        totalBytes));
                    });
                    await downloader.DownloadAsync(file, cachePath, downloadProgress, cancellationToken);
                    MaterializeFile(
                        cachePath,
                        stagedPath,
                        preferHardLink: IsShareablePath(file.Path));
                }

                completedBytes = checked(completedBytes + file.Size);
                progress?.Report(new ClientInstallProgress(
                    ClientInstallPhase.Staging,
                    CalculatePercent(completedBytes, totalBytes, 10, 80),
                    file.Path,
                    completedBytes,
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
        finally
        {
            TryDeleteDirectory(stagingDirectory);
        }
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

    private static void PreserveWritableGameData(
        string activeGameDirectory,
        string stagingGameDirectory)
    {
        if (!Directory.Exists(activeGameDirectory))
        {
            return;
        }

        RejectReparsePoint(activeGameDirectory);
        foreach (var relativePath in PreservedGamePaths)
        {
            var source = ManifestValidator.ResolveManagedPath(
                activeGameDirectory,
                relativePath);
            var destination = ManifestValidator.ResolveManagedPath(
                stagingGameDirectory,
                relativePath);
            if (File.Exists(source))
            {
                RejectReparsePoint(source);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination, overwrite: true);
            }
            else if (Directory.Exists(source))
            {
                CopyDirectory(source, destination);
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

    private static void CopyDirectory(string source, string destination)
    {
        RejectReparsePoint(source);
        Directory.CreateDirectory(destination);
        foreach (var entry in Directory.EnumerateFileSystemEntries(source))
        {
            RejectReparsePoint(entry);
            var destinationEntry = Path.Combine(destination, Path.GetFileName(entry));
            if (Directory.Exists(entry))
            {
                CopyDirectory(entry, destinationEntry);
            }
            else
            {
                File.Copy(entry, destinationEntry, overwrite: true);
            }
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
}

public sealed class InsufficientDiskSpaceException(long requiredBytes, long availableBytes)
    : IOException($"The installation requires {requiredBytes} bytes but only {availableBytes} bytes are available.")
{
    public long RequiredBytes { get; } = requiredBytes;
    public long AvailableBytes { get; } = availableBytes;
}

public sealed class ProfileInstallInProgressException(string profileId, Exception innerException)
    : IOException($"Another process is already installing profile {profileId}.", innerException);

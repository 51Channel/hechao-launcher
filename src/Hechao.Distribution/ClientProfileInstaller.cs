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
    string InstancesRoot,
    bool KeepObjectCache = true);

public sealed class ClientProfileInstaller(
    ResumableFileDownloader downloader,
    AtomicProfileDirectorySwitcher? directorySwitcher = null)
{
    private const string StateFileName = ".hechao-install.json";
    private static readonly JsonSerializerOptions StateJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly AtomicProfileDirectorySwitcher _directorySwitcher =
        directorySwitcher ?? new AtomicProfileDirectorySwitcher();

    public async Task<LocalProfileState> GetLocalStateAsync(
        string instancesRoot,
        string profileId,
        string expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ManifestValidator.ValidateProfileId(profileId);
        var activeDirectory = GetActiveDirectory(instancesRoot, profileId);
        var statePath = Path.Combine(activeDirectory, StateFileName);
        if (!File.Exists(statePath))
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
                state.SchemaVersion != 1 ||
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
        var instancesRoot = Path.GetFullPath(Environment.ExpandEnvironmentVariables(options.InstancesRoot));
        Directory.CreateDirectory(instancesRoot);
        var activeDirectory = GetActiveDirectory(instancesRoot, manifest.ProfileId);
        var stagingDirectory = Path.Combine(
            instancesRoot,
            $".{manifest.ProfileId}.staging-{Guid.NewGuid():N}");
        var previousDirectory = Path.Combine(instancesRoot, $".{manifest.ProfileId}.previous");
        var objectCache = Path.Combine(instancesRoot, ".hechao", "cache", "objects");
        await using var installationLock = AcquireInstallationLock(instancesRoot, manifest.ProfileId);

        EnsureDiskSpace(instancesRoot, manifest);
        Directory.CreateDirectory(stagingDirectory);

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

                var stagedPath = ManifestValidator.ResolveManagedPath(stagingDirectory, file.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(stagedPath)!);
                var activePath = ManifestValidator.ResolveManagedPath(activeDirectory, file.Path);

                if (await FileHashing.MatchesAsync(
                        activePath,
                        file.Size,
                        file.Sha256,
                        cancellationToken))
                {
                    File.Copy(activePath, stagedPath, overwrite: true);
                }
                else
                {
                    var normalizedDigest = file.Sha256.ToLowerInvariant();
                    var cachePath = Path.Combine(objectCache, normalizedDigest[..2], normalizedDigest);
                    usedCachePaths.Add(cachePath);
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
                    File.Copy(cachePath, stagedPath, overwrite: true);
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

    private static string GetActiveDirectory(string instancesRoot, string profileId)
    {
        var root = Path.GetFullPath(Environment.ExpandEnvironmentVariables(instancesRoot));
        return ManifestValidator.ResolveManagedPath(root, profileId);
    }

    private static async Task WriteStateAsync(
        string stagingDirectory,
        VerifiedClientManifest verifiedManifest,
        CancellationToken cancellationToken)
    {
        var state = new InstalledProfileState(
            1,
            verifiedManifest.Manifest.ProfileId,
            verifiedManifest.Manifest.Version,
            verifiedManifest.EnvelopeSha256,
            verifiedManifest.KeyId,
            DateTimeOffset.UtcNow);
        var statePath = Path.Combine(stagingDirectory, StateFileName);
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

    private static FileStream AcquireInstallationLock(string instancesRoot, string profileId)
    {
        var lockDirectory = Path.Combine(instancesRoot, ".hechao", "locks");
        Directory.CreateDirectory(lockDirectory);
        var lockPath = Path.Combine(lockDirectory, profileId + ".lock");
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
        string instancesRoot,
        ClientManifest manifest)
    {
        long stageBytes = 0;
        foreach (var file in manifest.Files)
        {
            stageBytes = checked(stageBytes + file.Size);
        }

        var requiredBytes = checked(stageBytes * 2 + 32L * 1024 * 1024);
        var root = Path.GetPathRoot(Path.GetFullPath(instancesRoot));
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

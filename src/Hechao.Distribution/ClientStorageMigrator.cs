using System.Text.Json;

namespace Hechao.Distribution;

public sealed record ClientStorageMigrationResult(
    string DataRoot,
    int MigratedProfiles,
    int MigratedPreviousProfiles,
    int MigratedCacheFiles,
    bool LegacyRootRetained);

public sealed class ClientStorageMigrator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public ClientStorageMigrationResult Migrate(
        string legacyInstancesRoot,
        string dataRoot)
    {
        var legacyRoot = ClientStorageLayout.NormalizeRoot(legacyInstancesRoot);
        var layout = new ClientStorageLayout(dataRoot);
        var legacyDirectories = DiscoverLegacyDirectories(legacyRoot);

        layout.EnsureBaseDirectories();

        var migratedProfiles = 0;
        var migratedPreviousProfiles = 0;
        foreach (var legacyDirectory in legacyDirectories)
        {
            var targetDirectory = Path.Combine(layout.InstancesRoot, legacyDirectory.TargetName);
            if (!ClientStorageLayout.PathsEqual(legacyDirectory.SourcePath, targetDirectory))
            {
                if (Directory.Exists(targetDirectory))
                {
                    UpgradeProfileDirectory(targetDirectory);
                    continue;
                }

                MoveDirectoryPreservingSourceOnFailure(
                    legacyDirectory.SourcePath,
                    targetDirectory);
            }

            UpgradeProfileDirectory(targetDirectory);
            if (legacyDirectory.IsPrevious)
            {
                migratedPreviousProfiles++;
            }
            else
            {
                migratedProfiles++;
            }
        }

        foreach (var profileDirectory in DiscoverLegacyDirectories(layout.InstancesRoot))
        {
            UpgradeProfileDirectory(profileDirectory.SourcePath);
        }

        var migratedCacheFiles = MigrateObjectCache(legacyRoot, layout.ObjectCacheRoot);
        WriteMigrationMarker(
            layout,
            legacyRoot,
            migratedProfiles,
            migratedPreviousProfiles,
            migratedCacheFiles);

        return new ClientStorageMigrationResult(
            layout.DataRoot,
            migratedProfiles,
            migratedPreviousProfiles,
            migratedCacheFiles,
            Directory.Exists(legacyRoot));
    }

    internal static void UpgradeProfileDirectory(string profileRoot)
    {
        var normalizedProfileRoot = ClientStorageLayout.NormalizeRoot(profileRoot);
        var gameDirectory = Path.Combine(
            normalizedProfileRoot,
            ClientStorageLayout.GameDirectoryName);
        if (!Directory.Exists(gameDirectory))
        {
            var temporaryGameDirectory = Path.Combine(
                normalizedProfileRoot,
                $".minecraft.migrating-{Guid.NewGuid():N}");
            Directory.CreateDirectory(temporaryGameDirectory);
            var movedPaths = new List<(string Source, string Destination)>();
            try
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(normalizedProfileRoot).ToArray())
                {
                    var name = Path.GetFileName(entry);
                    if (string.Equals(
                            name,
                            ClientStorageLayout.InstallStateFileName,
                            StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(
                            entry,
                            temporaryGameDirectory,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    RejectReparsePointsRecursively(entry);
                    var destination = Path.Combine(temporaryGameDirectory, name);
                    MoveFileSystemEntry(entry, destination);
                    movedPaths.Add((entry, destination));
                }

                Directory.Move(temporaryGameDirectory, gameDirectory);
            }
            catch
            {
                for (var index = movedPaths.Count - 1; index >= 0; index--)
                {
                    var moved = movedPaths[index];
                    if (File.Exists(moved.Destination) || Directory.Exists(moved.Destination))
                    {
                        MoveFileSystemEntry(moved.Destination, moved.Source);
                    }
                }

                TryDeleteDirectory(temporaryGameDirectory);
                throw;
            }
        }

        UpgradeInstallState(normalizedProfileRoot);
    }

    private static IReadOnlyList<LegacyProfileDirectory> DiscoverLegacyDirectories(
        string legacyRoot)
    {
        if (!Directory.Exists(legacyRoot))
        {
            return [];
        }

        var directories = new List<LegacyProfileDirectory>();
        foreach (var directory in Directory.EnumerateDirectories(legacyRoot).ToArray())
        {
            RejectReparsePoint(directory);
            var name = Path.GetFileName(directory);
            if (string.Equals(name, "instances", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "shared", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, ".hechao", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (TryParsePreviousProfileId(name, out var previousProfileId) &&
                IsRecognizedProfileDirectory(directory))
            {
                directories.Add(new LegacyProfileDirectory(
                    directory,
                    $".{previousProfileId}.previous",
                    IsPrevious: true));
                continue;
            }

            if (IsValidProfileId(name) && IsRecognizedProfileDirectory(directory))
            {
                directories.Add(new LegacyProfileDirectory(
                    directory,
                    name,
                    IsPrevious: false));
            }
        }

        return directories;
    }

    private static bool IsRecognizedProfileDirectory(string path) =>
        File.Exists(Path.Combine(path, ClientStorageLayout.InstallStateFileName)) ||
        File.Exists(Path.Combine(path, "hechao-profile.json")) ||
        Directory.Exists(Path.Combine(path, "versions"));

    private static bool IsValidProfileId(string value)
    {
        try
        {
            ManifestValidator.ValidateProfileId(value);
            return true;
        }
        catch (ManifestFormatException)
        {
            return false;
        }
    }

    private static bool TryParsePreviousProfileId(string name, out string profileId)
    {
        const string suffix = ".previous";
        profileId = string.Empty;
        if (!name.StartsWith('.') ||
            !name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        profileId = name[1..^suffix.Length];
        return IsValidProfileId(profileId);
    }

    private static void UpgradeInstallState(string profileRoot)
    {
        var statePath = Path.Combine(
            profileRoot,
            ClientStorageLayout.InstallStateFileName);
        if (!File.Exists(statePath))
        {
            return;
        }

        try
        {
            var state = JsonSerializer.Deserialize<InstalledProfileState>(
                File.ReadAllText(statePath),
                JsonOptions);
            if (state is null ||
                state.SchemaVersion == ClientStorageLayout.CurrentStorageSchemaVersion)
            {
                return;
            }

            if (state.SchemaVersion > ClientStorageLayout.CurrentStorageSchemaVersion)
            {
                throw new ClientStorageMigrationException(
                    $"The profile was created by a newer launcher: {statePath}");
            }

            var upgraded = state with
            {
                SchemaVersion = ClientStorageLayout.CurrentStorageSchemaVersion
            };
            WriteJsonAtomically(statePath, upgraded);
        }
        catch (JsonException exception)
        {
            throw new ClientStorageMigrationException(
                $"The profile state is invalid: {statePath}",
                exception);
        }
    }

    private static int MigrateObjectCache(
        string legacyRoot,
        string objectCacheRoot)
    {
        var legacyCacheRoot = Path.Combine(legacyRoot, ".hechao", "cache", "objects");
        if (!Directory.Exists(legacyCacheRoot) ||
            ClientStorageLayout.PathsEqual(legacyCacheRoot, objectCacheRoot))
        {
            return 0;
        }

        var migratedFiles = 0;
        MigrateObjectCacheDirectory(
            legacyCacheRoot,
            legacyCacheRoot,
            objectCacheRoot,
            ref migratedFiles);

        TryDeleteDirectory(legacyCacheRoot);
        return migratedFiles;
    }

    private static void MoveDirectoryPreservingSourceOnFailure(
        string source,
        string destination)
    {
        RejectReparsePointsRecursively(source);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        try
        {
            Directory.Move(source, destination);
            return;
        }
        catch (IOException)
        {
        }

        var staging = destination + $".migrating-{Guid.NewGuid():N}";
        try
        {
            CopyDirectory(source, staging);
            Directory.Move(staging, destination);
            Directory.Delete(source, recursive: true);
        }
        catch
        {
            TryDeleteDirectory(staging);
            throw;
        }
    }

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
                File.Copy(entry, destinationEntry, overwrite: false);
            }
        }
    }

    private static void MigrateObjectCacheDirectory(
        string cacheRoot,
        string sourceDirectory,
        string objectCacheRoot,
        ref int migratedFiles)
    {
        RejectReparsePoint(sourceDirectory);
        foreach (var entry in Directory.EnumerateFileSystemEntries(sourceDirectory))
        {
            RejectReparsePoint(entry);
            if (Directory.Exists(entry))
            {
                MigrateObjectCacheDirectory(
                    cacheRoot,
                    entry,
                    objectCacheRoot,
                    ref migratedFiles);
                TryDeleteDirectory(entry);
                continue;
            }

            var relativePath = Path.GetRelativePath(cacheRoot, entry);
            var destinationFile = Path.Combine(objectCacheRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
            if (File.Exists(destinationFile))
            {
                if (new FileInfo(entry).Length != new FileInfo(destinationFile).Length)
                {
                    continue;
                }

                File.Delete(entry);
            }
            else
            {
                MoveFileWithCopyFallback(entry, destinationFile);
            }

            migratedFiles++;
        }
    }

    private static void RejectReparsePointsRecursively(string path)
    {
        RejectReparsePoint(path);
        if (!Directory.Exists(path))
        {
            return;
        }

        foreach (var entry in Directory.EnumerateFileSystemEntries(path))
        {
            RejectReparsePointsRecursively(entry);
        }
    }

    private static void MoveFileWithCopyFallback(string source, string destination)
    {
        try
        {
            File.Move(source, destination);
        }
        catch (IOException)
        {
            File.Copy(source, destination, overwrite: false);
            File.Delete(source);
        }
    }

    private static void MoveFileSystemEntry(string source, string destination)
    {
        if (Directory.Exists(source))
        {
            Directory.Move(source, destination);
        }
        else
        {
            File.Move(source, destination);
        }
    }

    private static void RejectReparsePoint(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new ClientStorageMigrationException(
                $"The legacy client directory contains an unsupported link: {path}");
        }
    }

    private static void WriteMigrationMarker(
        ClientStorageLayout layout,
        string legacyRoot,
        int migratedProfiles,
        int migratedPreviousProfiles,
        int migratedCacheFiles)
    {
        Directory.CreateDirectory(layout.InternalRoot);
        var marker = new StorageMigrationMarker(
            ClientStorageLayout.CurrentStorageSchemaVersion,
            legacyRoot,
            DateTimeOffset.UtcNow,
            migratedProfiles,
            migratedPreviousProfiles,
            migratedCacheFiles);
        WriteJsonAtomically(layout.MigrationMarkerPath, marker);
    }

    private static void WriteJsonAtomically<T>(string path, T value)
    {
        var temporaryPath = path + ".tmp";
        File.WriteAllText(
            temporaryPath,
            JsonSerializer.Serialize(value, JsonOptions));
        File.Move(temporaryPath, path, overwrite: true);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path) &&
                !Directory.EnumerateFileSystemEntries(path).Any())
            {
                Directory.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record LegacyProfileDirectory(
        string SourcePath,
        string TargetName,
        bool IsPrevious);

    private sealed record StorageMigrationMarker(
        int SchemaVersion,
        string LegacyRoot,
        DateTimeOffset MigratedAt,
        int MigratedProfiles,
        int MigratedPreviousProfiles,
        int MigratedCacheFiles);
}

public sealed class ClientStorageMigrationException : IOException
{
    public ClientStorageMigrationException(string message)
        : base(message)
    {
    }

    public ClientStorageMigrationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

namespace Hechao.Distribution;

public sealed class ClientStorageLayout
{
    public const int LegacyStorageSchemaVersion = 1;
    public const int CurrentStorageSchemaVersion = 2;
    public const string GameDirectoryName = ".minecraft";
    public const string InstallStateFileName = ".hechao-install.json";

    public ClientStorageLayout(string dataRoot)
    {
        if (string.IsNullOrWhiteSpace(dataRoot))
        {
            throw new ArgumentException("The client data root is required.", nameof(dataRoot));
        }

        DataRoot = NormalizeRoot(dataRoot);
    }

    public string DataRoot { get; }
    public string InstancesRoot => Path.Combine(DataRoot, "instances");
    public string SharedRoot => Path.Combine(DataRoot, "shared");
    public string ObjectCacheRoot => Path.Combine(SharedRoot, "objects");
    public string RuntimeRoot => Path.Combine(SharedRoot, "runtime");
    public string InternalRoot => Path.Combine(DataRoot, ".hechao");
    public string LocksRoot => Path.Combine(InternalRoot, "locks");
    public string MigrationMarkerPath => Path.Combine(InternalRoot, "storage-layout.json");

    public string GetProfileRoot(string profileId)
    {
        ManifestValidator.ValidateProfileId(profileId);
        return ManifestValidator.ResolveManagedPath(InstancesRoot, profileId);
    }

    public string GetProfileGameDirectory(string profileId) =>
        Path.Combine(GetProfileRoot(profileId), GameDirectoryName);

    public string GetPreviousProfileRoot(string profileId)
    {
        ManifestValidator.ValidateProfileId(profileId);
        return Path.Combine(InstancesRoot, $".{profileId}.previous");
    }

    public string CreateStagingProfileRoot(string profileId)
    {
        ManifestValidator.ValidateProfileId(profileId);
        return Path.Combine(InstancesRoot, $".{profileId}.staging-{Guid.NewGuid():N}");
    }

    public void EnsureBaseDirectories()
    {
        Directory.CreateDirectory(InstancesRoot);
        Directory.CreateDirectory(ObjectCacheRoot);
        Directory.CreateDirectory(RuntimeRoot);
        Directory.CreateDirectory(LocksRoot);
    }

    public static string GetDefaultDataRoot()
    {
        var localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localApplicationData, "Hechao", "GameData");
    }

    public static string GetLegacyDefaultInstancesRoot()
    {
        var roamingApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData);
        return Path.Combine(roamingApplicationData, "Hechao", "instances");
    }

    public static string NormalizeRoot(string path) =>
        Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim())));

    public static bool PathsEqual(string left, string right) =>
        string.Equals(
            NormalizeRoot(left),
            NormalizeRoot(right),
            StringComparison.OrdinalIgnoreCase);
}

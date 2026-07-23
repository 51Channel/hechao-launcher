using System.Text.RegularExpressions;

namespace Hechao.Distribution;

public static partial class ManifestValidator
{
    public const int CurrentSchemaVersion = 1;
    public const int MaximumFileCount = 100_000;
    public const long MaximumFileSize = 32L * 1024 * 1024 * 1024;

    public static void Validate(ClientManifest manifest)
    {
        if (manifest.SchemaVersion != CurrentSchemaVersion)
        {
            throw new ManifestFormatException($"Unsupported manifest schema version: {manifest.SchemaVersion}.");
        }

        ValidateProfileId(manifest.ProfileId);
        ValidateText(manifest.Version, nameof(manifest.Version), 40);
        ValidateText(manifest.MinecraftVersion, nameof(manifest.MinecraftVersion), 40);
        ValidateText(manifest.JavaVersion, nameof(manifest.JavaVersion), 40);
        ValidateText(manifest.Loader, nameof(manifest.Loader), 40);
        ValidateText(manifest.LoaderVersion, nameof(manifest.LoaderVersion), 80);

        if (manifest.Files.Count > MaximumFileCount)
        {
            throw new ManifestFormatException($"The manifest contains more than {MaximumFileCount} files.");
        }

        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in manifest.Files)
        {
            ValidateManagedPath(file.Path);
            if (!paths.Add(file.Path))
            {
                throw new ManifestFormatException($"The manifest contains a duplicate path: {file.Path}.");
            }

            if (file.Size is < 0 or > MaximumFileSize)
            {
                throw new ManifestFormatException($"The file size is invalid for {file.Path}.");
            }

            if (!Sha256Regex().IsMatch(file.Sha256))
            {
                throw new ManifestFormatException($"The SHA-256 digest is invalid for {file.Path}.");
            }

            ValidateObjectUrl(file.Url, file.Path);
        }

        var deletePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var deletePath in manifest.DeletePaths)
        {
            ValidateManagedPath(deletePath);
            if (!deletePaths.Add(deletePath))
            {
                throw new ManifestFormatException($"The delete list contains a duplicate path: {deletePath}.");
            }
        }
    }

    public static void ValidateProfileId(string profileId)
    {
        if (!ProfileIdRegex().IsMatch(profileId))
        {
            throw new ManifestFormatException("The profile identifier is invalid.");
        }
    }

    public static string ResolveManagedPath(string rootDirectory, string relativePath)
    {
        ValidateManagedPath(relativePath);
        var root = Path.GetFullPath(rootDirectory);
        var candidate = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var rootPrefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        if (!candidate.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new ManifestFormatException($"The managed path escapes its root: {relativePath}.");
        }

        return candidate;
    }

    public static void ValidateManagedPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            path.Length > 512 ||
            path[0] == '/' ||
            path.Contains('\\') ||
            path.Contains('\0'))
        {
            throw new ManifestFormatException($"The managed path is invalid: {path}.");
        }

        var segments = path.Split('/');
        foreach (var segment in segments)
        {
            if (segment.Length == 0 ||
                segment is "." or ".." ||
                segment.EndsWith(' ') ||
                segment.EndsWith('.') ||
                segment.Any(character => character < 32 || "<>:\"|?*".Contains(character)) ||
                IsReservedWindowsName(segment))
            {
                throw new ManifestFormatException($"The managed path is invalid: {path}.");
            }
        }

        if (segments.Any(segment => string.Equals(segment, ".hechao", StringComparison.OrdinalIgnoreCase)) ||
            string.Equals(path, ".hechao-install.json", StringComparison.OrdinalIgnoreCase))
        {
            throw new ManifestFormatException($"The managed path is reserved: {path}.");
        }
    }

    private static void ValidateObjectUrl(string value, string path)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
             !(string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) && uri.IsLoopback)) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new ManifestFormatException($"The object URL is invalid for {path}.");
        }
    }

    private static void ValidateText(string value, string name, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength || value.Any(char.IsControl))
        {
            throw new ManifestFormatException($"{name} is invalid.");
        }
    }

    private static bool IsReservedWindowsName(string segment)
    {
        var name = segment.Split('.')[0];
        return name.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
               (name.Length == 4 &&
                (name.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                 name.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
                name[3] is >= '1' and <= '9');
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{1,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex ProfileIdRegex();

    [GeneratedRegex("^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Regex();
}

using System.Text.RegularExpressions;

namespace Hechao.Api.Distribution;

public sealed partial class LauncherUpdateOptions
{
    public const string SectionName = "LauncherUpdates";

    public bool Enabled { get; init; }
    public string LatestVersion { get; init; } = string.Empty;
    public string MinimumSupportedVersion { get; init; } = string.Empty;
    public long InstallerBytes { get; init; }
    public string InstallerSha256 { get; init; } = string.Empty;
    public DateTimeOffset PublishedAt { get; init; }
    public string ReleaseNotes { get; init; } = string.Empty;

    public bool IsValid()
    {
        if (!Enabled)
        {
            return true;
        }

        return TryParseVersion(LatestVersion, out var latest) &&
               TryParseVersion(MinimumSupportedVersion, out var minimum) &&
               latest >= minimum &&
               InstallerBytes is >= 1024 * 1024 and <= 512L * 1024 * 1024 &&
               Sha256Regex().IsMatch(InstallerSha256) &&
               PublishedAt != default &&
               ReleaseNotes.Length <= 2000;
    }

    public static bool TryParseVersion(string value, out Version version)
    {
        version = new Version();
        if (!VersionRegex().IsMatch(value ?? string.Empty) ||
            !Version.TryParse(value, out var parsed))
        {
            return false;
        }

        version = parsed;
        return true;
    }

    [GeneratedRegex(@"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex VersionRegex();

    [GeneratedRegex("^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Regex();
}

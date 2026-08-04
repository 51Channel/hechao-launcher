namespace Hechao.Contracts;

public sealed record LauncherUpdateRelease(
    string Version,
    string MinimumSupportedVersion,
    long InstallerBytes,
    string InstallerSha256,
    DateTimeOffset PublishedAt,
    string ReleaseNotes,
    string InstallerUrl);

public sealed record PublicLauncherRelease(
    string Version,
    long InstallerBytes,
    string InstallerSha256,
    DateTimeOffset PublishedAt,
    string ReleaseNotes);

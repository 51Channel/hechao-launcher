using Hechao.Api.Distribution;

namespace Hechao.Api.Tests;

public sealed class LauncherUpdateOptionsTests
{
    [Fact]
    public void IsValid_AllowsDisabledConfiguration()
    {
        var options = new LauncherUpdateOptions();

        Assert.True(options.IsValid());
    }

    [Fact]
    public void IsValid_AcceptsCompleteRelease()
    {
        var options = CreateValid();

        Assert.True(options.IsValid());
    }

    [Theory]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("01.2.3")]
    [InlineData("1.02.3")]
    [InlineData("v1.2.3")]
    public void IsValid_RejectsNonCanonicalVersions(string version)
    {
        var options = CreateValid().WithVersion(version);

        Assert.False(options.IsValid());
    }

    [Fact]
    public void IsValid_RejectsReleaseOlderThanMinimum()
    {
        var options = CreateValid(
            latestVersion: "1.2.2",
            minimumSupportedVersion: "1.2.3");

        Assert.False(options.IsValid());
    }

    [Theory]
    [InlineData(1048575)]
    [InlineData(536870913)]
    public void IsValid_RejectsInstallerOutsideAllowedSize(long installerBytes)
    {
        var options = CreateValid(installerBytes: installerBytes);

        Assert.False(options.IsValid());
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("gg00000000000000000000000000000000000000000000000000000000000000")]
    public void IsValid_RejectsInvalidSha256(string sha256)
    {
        var options = CreateValid(installerSha256: sha256);

        Assert.False(options.IsValid());
    }

    private static LauncherUpdateOptions CreateValid(
        string latestVersion = "1.2.3",
        string minimumSupportedVersion = "1.1.0",
        long installerBytes = 60 * 1024 * 1024,
        string? installerSha256 = null) =>
        new()
        {
            Enabled = true,
            LatestVersion = latestVersion,
            MinimumSupportedVersion = minimumSupportedVersion,
            InstallerBytes = installerBytes,
            InstallerSha256 = installerSha256 ?? new string('a', 64),
            PublishedAt = DateTimeOffset.Parse("2026-07-30T00:00:00Z"),
            ReleaseNotes = "测试更新"
        };
}

file static class LauncherUpdateOptionsTestExtensions
{
    public static LauncherUpdateOptions WithVersion(
        this LauncherUpdateOptions value,
        string version) =>
        new()
        {
            Enabled = value.Enabled,
            LatestVersion = version,
            MinimumSupportedVersion = value.MinimumSupportedVersion,
            InstallerBytes = value.InstallerBytes,
            InstallerSha256 = value.InstallerSha256,
            PublishedAt = value.PublishedAt,
            ReleaseNotes = value.ReleaseNotes
        };
}

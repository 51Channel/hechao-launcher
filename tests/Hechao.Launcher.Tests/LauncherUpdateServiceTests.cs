using Hechao.Contracts;
using Hechao.Launcher.Services;

namespace Hechao.Launcher.Tests;

public sealed class LauncherUpdateServiceTests
{
    private static readonly DateTimeOffset PublishedAt =
        DateTimeOffset.Parse("2026-07-30T00:00:00Z");

    [Fact]
    public void CreatePlan_ReturnsNullWhenLauncherIsCurrent()
    {
        var release = CreateRelease(version: "0.12.3");

        var plan = LauncherUpdateService.CreatePlan(release, "0.12.3");

        Assert.Null(plan);
    }

    [Fact]
    public void CreatePlan_CreatesOptionalUpdate()
    {
        var release = CreateRelease(
            version: "0.13.0",
            minimumSupportedVersion: "0.11.0");

        var plan = LauncherUpdateService.CreatePlan(release, "0.12.3");

        Assert.NotNull(plan);
        Assert.False(plan.IsRequired);
        Assert.Equal(new Version(0, 13, 0), plan.LatestVersion);
        Assert.Equal(new string('a', 64), plan.InstallerSha256);
    }

    [Fact]
    public void CreatePlan_MarksUpdateRequiredBelowMinimum()
    {
        var release = CreateRelease(
            version: "0.13.0",
            minimumSupportedVersion: "0.12.4");

        var plan = LauncherUpdateService.CreatePlan(release, "0.12.3");

        Assert.NotNull(plan);
        Assert.True(plan.IsRequired);
    }

    [Theory]
    [InlineData("ftp://download.hechao.world/launcher.exe")]
    [InlineData("http://download.hechao.world/launcher.exe")]
    [InlineData("https://user:password@download.hechao.world/launcher.exe")]
    [InlineData("https://download.hechao.world/launcher.exe#fragment")]
    public void CreatePlan_RejectsUnsafeInstallerUrl(string url)
    {
        var release = CreateRelease(installerUrl: url);

        Assert.Throws<InvalidDataException>(() =>
            LauncherUpdateService.CreatePlan(release, "0.12.3"));
    }

    [Theory]
    [InlineData("0.13")]
    [InlineData("0.13.0.1")]
    [InlineData("v0.13.0")]
    public void CreatePlan_RejectsNonCanonicalVersion(string version)
    {
        var release = CreateRelease(version: version);

        Assert.Throws<InvalidDataException>(() =>
            LauncherUpdateService.CreatePlan(release, "0.12.3"));
    }

    [Fact]
    public void BootstrapTryParse_AcceptsExactUpdaterArguments()
    {
        var arguments = new[]
        {
            "--apply-launcher-update",
            "1234",
            Path.Combine(
                Path.GetTempPath(),
                "Hechao-Launcher-Setup-0.13.0-win-x64.exe"),
            (60 * 1024 * 1024).ToString(),
            new string('b', 64),
            "0.13.0"
        };

        var result = LauncherUpdateBootstrap.TryParse(arguments, out var command);

        Assert.True(result);
        Assert.NotNull(command);
        Assert.Equal(1234, command.ParentProcessId);
        Assert.Equal("0.13.0", command.Version);
        Assert.Equal(new string('b', 64), command.InstallerSha256);
    }

    [Fact]
    public void BootstrapTryParse_RejectsMismatchedInstallerName()
    {
        var arguments = new[]
        {
            "--apply-launcher-update",
            "1234",
            Path.Combine(Path.GetTempPath(), "other.exe"),
            (60 * 1024 * 1024).ToString(),
            new string('b', 64),
            "0.13.0"
        };

        var result = LauncherUpdateBootstrap.TryParse(arguments, out var command);

        Assert.False(result);
        Assert.Null(command);
    }

    private static LauncherUpdateRelease CreateRelease(
        string version = "0.13.0",
        string minimumSupportedVersion = "0.11.0",
        string installerUrl =
            "https://download.hechao.world/releases/launcher/0.13.0/installer.exe") =>
        new(
            version,
            minimumSupportedVersion,
            60 * 1024 * 1024,
            new string('A', 64),
            PublishedAt,
            "稳定性更新",
            installerUrl);
}

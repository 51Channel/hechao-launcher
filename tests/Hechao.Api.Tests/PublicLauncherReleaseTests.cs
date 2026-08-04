using System.Text.Json;
using Hechao.Contracts;

namespace Hechao.Api.Tests;

public sealed class PublicLauncherReleaseTests
{
    [Theory]
    [InlineData(
        "app.MapGet(\"/v1/public/launcher/latest\", GetPublicLauncherRelease)",
        ".RequireRateLimiting(\"catalog\")")]
    [InlineData(
        "app.MapGet(\"/v1/public/launcher/download\", DownloadPublicLauncher)",
        ".RequireRateLimiting(\"downloads\")")]
    public void PublicLauncherRoutesAreAnonymousAndRateLimited(
        string route,
        string rateLimit)
    {
        var program = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Hechao.Api",
            "Program.cs"));
        var routeStart = program.IndexOf(route, StringComparison.Ordinal);

        Assert.True(routeStart >= 0, $"The public launcher route {route} must be mapped.");
        var routeEnd = program.IndexOf(';', routeStart);
        Assert.True(routeEnd > routeStart, "The public launcher route must terminate.");
        var routeContract = program[routeStart..(routeEnd + 1)];
        Assert.Contains(rateLimit, routeContract, StringComparison.Ordinal);
        Assert.DoesNotContain(
            ".RequireAuthorization()",
            routeContract,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PublicMetadataDoesNotExposePrivateInstallerUrl()
    {
        var release = new PublicLauncherRelease(
            "0.14.2",
            61_929_723,
            new string('d', 64),
            DateTimeOffset.Parse("2026-08-01T09:16:27Z"),
            "Stable launcher release.");

        var json = JsonSerializer.Serialize(
            release,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"version\":\"0.14.2\"", json, StringComparison.Ordinal);
        Assert.Contains("\"installerSha256\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("installerUrl", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("oss", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("signature", json, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Hechao.Launcher.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;
using Hechao.Api.Catalog;
using Hechao.Contracts;

namespace Hechao.Api.Tests;

public sealed class PublicActivityCatalogProjectorTests
{
    [Fact]
    public void PublicEndpointIsReadOnlyAnonymousAndCatalogRateLimited()
    {
        var program = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Hechao.Api",
            "Program.cs"));
        const string route =
            "app.MapGet(\"/v1/public/activities\", GetPublicActivitiesAsync)";
        var routeStart = program.IndexOf(route, StringComparison.Ordinal);

        Assert.True(routeStart >= 0, "The public activity route must be mapped.");
        var routeEnd = program.IndexOf(';', routeStart);
        Assert.True(routeEnd > routeStart, "The public activity route must terminate.");
        var routeContract = program[routeStart..(routeEnd + 1)];
        Assert.Contains(
            ".RequireRateLimiting(\"catalog\")",
            routeContract,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            ".RequireAuthorization()",
            routeContract,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CreateReturnsOnlySanitizedActivityServers()
    {
        var generatedAt = new DateTimeOffset(
            2026,
            8,
            4,
            4,
            0,
            0,
            TimeSpan.Zero);
        var opensAt = generatedAt.AddDays(2);
        var closesAt = opensAt.AddHours(3);
        var catalog = new LauncherCatalogSnapshot(
            generatedAt,
            [
                new ServerSummary(
                    "survival",
                    "常驻生存",
                    "生存",
                    "server",
                    ServerStatus.Online,
                    4,
                    30,
                    "1.21.11",
                    ModLoaderKind.Paper,
                    AccessTier.Member,
                    "base-profile",
                    CatalogSection: ServerCatalogSection.Permanent),
                new ServerSummary(
                    "summer-recording",
                    "夏日录制活动",
                    "夏日",
                    "activity",
                    ServerStatus.Closed,
                    0,
                    20,
                    "1.21.11",
                    ModLoaderKind.NeoForge,
                    AccessTier.Participant,
                    "private-client-profile",
                    "提前下载客户端，按开放时间进入。",
                    opensAt,
                    closesAt,
                    ServerCatalogSection.Activity),
            ],
            [
                new ClientProfileSummary(
                    "private-client-profile",
                    "活动客户端",
                    "1.0.0",
                    1234,
                    new string('a', 64),
                    generatedAt),
            ]);

        var result = PublicActivityCatalogProjector.Create(catalog);

        Assert.Equal(generatedAt, result.GeneratedAt);
        var activity = Assert.Single(result.Activities);
        Assert.Equal("summer-recording", activity.Id);
        Assert.Equal("夏日录制活动", activity.Name);
        Assert.Equal(ServerStatus.Closed, activity.Status);
        Assert.Equal(opensAt, activity.OpensAt);
        Assert.Equal(closesAt, activity.ClosesAt);
        Assert.Equal(20, activity.MaxPlayers);
        Assert.Equal(AccessTier.Participant, activity.MinimumTier);

        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        var json = JsonSerializer.Serialize(result, options);
        Assert.DoesNotContain(
            "clientProfile",
            json,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            new string('a', 64),
            json,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"status\":\"Closed\"",
            json,
            StringComparison.Ordinal);
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

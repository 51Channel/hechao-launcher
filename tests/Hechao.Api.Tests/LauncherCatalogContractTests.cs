using System.Text.Json;
using System.Text.Json.Serialization;
using Hechao.Contracts;

namespace Hechao.Api.Tests;

public sealed class LauncherCatalogContractTests
{
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    [Fact]
    public void LegacySnapshotWithoutCatalogSectionDeserializesWithNullSection()
    {
        const string json = """
            {
              "generatedAt": "2026-08-01T00:00:00Z",
              "servers": [
                {
                  "id": "legacy",
                  "name": "Legacy Server",
                  "shortName": "Legacy",
                  "iconGlyph": "L",
                  "status": "Online",
                  "onlinePlayers": 1,
                  "maxPlayers": 20,
                  "minecraftVersion": "1.21.11",
                  "loader": "Paper",
                  "minimumTier": "Member",
                  "clientProfileId": "legacy-profile"
                }
              ],
              "clientProfiles": [
                {
                  "id": "legacy-profile",
                  "displayName": "Legacy Profile",
                  "version": "1.0.0",
                  "downloadBytes": 1,
                  "sha256": "hash",
                  "publishedAt": "2026-08-01T00:00:00Z"
                }
              ]
            }
            """;

        var snapshot = JsonSerializer.Deserialize<LauncherCatalogSnapshot>(json, SerializerOptions);

        Assert.NotNull(snapshot);
        Assert.Null(Assert.Single(snapshot.Servers).CatalogSection);
        Assert.True(Assert.Single(snapshot.Servers).CanJoin);
    }

    [Fact]
    public void CatalogSectionSerializesAsStringAndRoundTrips()
    {
        var server = new ServerSummary(
            "activity",
            "Activity",
            "Activity",
            "A",
            ServerStatus.Online,
            1,
            20,
            "1.21.11",
            ModLoaderKind.NeoForge,
            AccessTier.Participant,
            "activity-profile",
            CatalogSection: ServerCatalogSection.Activity);

        var json = JsonSerializer.Serialize(server, SerializerOptions);
        var roundTrip = JsonSerializer.Deserialize<ServerSummary>(json, SerializerOptions);

        Assert.Contains("\"catalogSection\":\"Activity\"", json, StringComparison.Ordinal);
        Assert.Contains("\"canJoin\":true", json, StringComparison.Ordinal);
        Assert.NotNull(roundTrip);
        Assert.Equal(ServerCatalogSection.Activity, roundTrip.CatalogSection);
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}

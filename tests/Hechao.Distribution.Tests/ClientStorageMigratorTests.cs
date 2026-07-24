using System.Text.Json;

namespace Hechao.Distribution.Tests;

public sealed class ClientStorageMigratorTests
{
    [Fact]
    public async Task Migrate_MovesLegacyProfilesAndCacheIntoVersionTwoLayout()
    {
        using var temporary = new TemporaryDirectory();
        var legacyRoot = Path.Combine(temporary.Path, "legacy");
        var dataRoot = Path.Combine(temporary.Path, "data");
        var profileId = "activity-neoforge-1.21.11";
        var legacyProfile = Path.Combine(legacyRoot, profileId);
        var legacyPrevious = Path.Combine(legacyRoot, $".{profileId}.previous");
        Directory.CreateDirectory(Path.Combine(legacyProfile, "versions", "current"));
        Directory.CreateDirectory(Path.Combine(legacyPrevious, "versions", "previous"));
        await File.WriteAllTextAsync(
            Path.Combine(legacyProfile, "versions", "current", "current.json"),
            "{}");
        await File.WriteAllTextAsync(
            Path.Combine(legacyPrevious, "versions", "previous", "previous.json"),
            "{}");
        await WriteLegacyStateAsync(legacyProfile, profileId);

        var digest = new string('a', 64);
        var legacyObject = Path.Combine(
            legacyRoot,
            ".hechao",
            "cache",
            "objects",
            digest[..2],
            digest);
        Directory.CreateDirectory(Path.GetDirectoryName(legacyObject)!);
        await File.WriteAllTextAsync(legacyObject, "cached-object");

        var result = new ClientStorageMigrator().Migrate(legacyRoot, dataRoot);
        var layout = new ClientStorageLayout(dataRoot);

        Assert.Equal(1, result.MigratedProfiles);
        Assert.Equal(1, result.MigratedPreviousProfiles);
        Assert.Equal(1, result.MigratedCacheFiles);
        Assert.True(File.Exists(Path.Combine(
            layout.GetProfileGameDirectory(profileId),
            "versions",
            "current",
            "current.json")));
        Assert.True(File.Exists(Path.Combine(
            layout.GetPreviousProfileRoot(profileId),
            ClientStorageLayout.GameDirectoryName,
            "versions",
            "previous",
            "previous.json")));
        Assert.True(File.Exists(Path.Combine(
            layout.ObjectCacheRoot,
            digest[..2],
            digest)));

        var state = JsonSerializer.Deserialize<InstalledProfileState>(
            await File.ReadAllTextAsync(Path.Combine(
                layout.GetProfileRoot(profileId),
                ClientStorageLayout.InstallStateFileName)),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(state);
        Assert.Equal(ClientStorageLayout.CurrentStorageSchemaVersion, state.SchemaVersion);
    }

    [Fact]
    public async Task Migrate_IsIdempotentAndLeavesUnrelatedFoldersAlone()
    {
        using var temporary = new TemporaryDirectory();
        var root = Path.Combine(temporary.Path, "custom-root");
        var profileId = "base-1.21.11";
        var profile = Path.Combine(root, profileId);
        var unrelated = Path.Combine(root, "recording-assets");
        Directory.CreateDirectory(Path.Combine(profile, "versions"));
        Directory.CreateDirectory(unrelated);
        await File.WriteAllTextAsync(
            Path.Combine(profile, "versions", "version.json"),
            "{}");
        await File.WriteAllTextAsync(
            Path.Combine(unrelated, "notes.txt"),
            "keep-me");

        var migrator = new ClientStorageMigrator();
        var first = migrator.Migrate(root, root);
        var second = migrator.Migrate(root, root);
        var layout = new ClientStorageLayout(root);

        Assert.Equal(1, first.MigratedProfiles);
        Assert.Equal(0, second.MigratedProfiles);
        Assert.True(File.Exists(Path.Combine(
            layout.GetProfileGameDirectory(profileId),
            "versions",
            "version.json")));
        Assert.Equal(
            "keep-me",
            await File.ReadAllTextAsync(Path.Combine(unrelated, "notes.txt")));
    }

    private static async Task WriteLegacyStateAsync(
        string profileRoot,
        string profileId)
    {
        var state = new InstalledProfileState(
            ClientStorageLayout.LegacyStorageSchemaVersion,
            profileId,
            "1.0.0",
            new string('b', 64),
            "release-2026",
            DateTimeOffset.Parse("2026-07-22T00:00:00Z"));
        await File.WriteAllTextAsync(
            Path.Combine(profileRoot, ClientStorageLayout.InstallStateFileName),
            JsonSerializer.Serialize(
                state,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }
}

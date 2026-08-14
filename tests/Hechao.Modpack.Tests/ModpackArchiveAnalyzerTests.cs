using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Hechao.Modpack;

namespace Hechao.Modpack.Tests;

public sealed class ModpackArchiveAnalyzerTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "hechao-modpack-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task AnalyzeAndSplitAsync_CanonicalArchiveCreatesTwoSafeParts()
    {
        var source = CreateArchive(
            ("Pack/hechao-pack.json", JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                id = "summer-fabric-1.20.1",
                displayName = "夏日活动",
                version = "1.2.3",
                minecraftVersion = "1.20.1",
                javaMajorVersion = 17,
                loader = "Fabric",
                loaderVersion = "0.16.14",
                clientRoot = "client",
                serverRoot = "server",
                sharedRoot = "shared"
            })),
            ("Pack/client/.minecraft/versions/1.20.1/1.20.1.json", "{}"),
            ("Pack/shared/mods/common.jar", "shared-mod"),
            ("Pack/server/server.properties", "max-players=24\nserver-port=25568\n"),
            ("Pack/server/start.bat", "java -jar fabric-server-launch.jar nogui\n"));

        var result = await ModpackArchiveAnalyzer.AnalyzeAndSplitAsync(
            source,
            Path.Combine(root, "out"));

        Assert.Equal(ModpackLayoutKind.Canonical, result.Layout);
        Assert.False(result.HasBlockingIssues);
        Assert.Equal("summer-fabric-1.20.1", result.Metadata.SuggestedProfileId);
        Assert.Equal("1.2.3", result.Metadata.Version);
        Assert.Equal("Fabric", result.Metadata.Loader);
        Assert.Equal(24, result.Metadata.MaximumPlayers);
        Assert.NotNull(result.Client);
        Assert.NotNull(result.Server);
        Assert.Equal(2, result.Client!.FileCount);
        Assert.Equal(3, result.Server!.FileCount);
        Assert.Equal(
            ["mods/common.jar", "versions/1.20.1/1.20.1.json"],
            ReadPaths(result.Client.Path));
        Assert.Equal(
            ["mods/common.jar", "server.properties", "start.bat"],
            ReadPaths(result.Server.Path));
    }

    [Fact]
    public async Task AnalyzeAndSplitAsync_StandardImportTemplateCreatesCompleteSides()
    {
        const string versionId = "1.20.1-Fabric_0.15.11";
        var source = CreateArchive(
            ("hechao-pack.json", """
                {
                  "schemaVersion": 1,
                  "id": "activity-contract-fixture-fabric-1.20.1",
                  "displayName": "Contract Fixture",
                  "version": "1.0.0",
                  "minecraftVersion": "1.20.1",
                  "javaMajorVersion": 17,
                  "loader": "Fabric",
                  "loaderVersion": "0.15.11",
                  "clientRoot": "client",
                  "serverRoot": "server",
                  "sharedRoot": "shared"
                }
                """),
            ("client/hechao-profile.json", $$"""
                {
                  "schemaVersion": 1,
                  "versionId": "{{versionId}}",
                  "javaMajorVersion": 17
                }
                """),
            ("client/assets/indexes/1.20.json", "{}"),
            ("client/assets/objects/00/0000000000000000000000000000000000000000", "asset"),
            ("client/libraries/example/library.jar", "library"),
            ($"client/versions/{versionId}/{versionId}.json", $$"""
                {
                  "id": "{{versionId}}",
                  "javaVersion": { "majorVersion": 17 }
                }
                """),
            ($"client/versions/{versionId}/{versionId}.jar", "version"),
            ("client/mods/client-only.jar", "client-mod"),
            ("shared/mods/hechao-contract.jar", "common-mod"),
            ("server/server.properties", """
                server-ip=127.0.0.1
                server-port=25568
                online-mode=false
                max-players=20
                """),
            ("server/eula.txt", "eula=true"),
            ("server/user_jvm_args.txt", "-Xms1024M\n-Xmx2048M\n"),
            ("server/start.bat", """
                @echo off
                if not defined HECHAO_MANAGED_START pause
                java @user_jvm_args.txt -jar fabric-server-launch.jar nogui
                """),
            ("server/fabric-server-launch.jar", "fabric-server"),
            ("server/mods/server-only.jar", "server-mod"));

        var result = await ModpackArchiveAnalyzer.AnalyzeAndSplitAsync(
            source,
            Path.Combine(root, "standard-template-out"));

        Assert.Equal(ModpackLayoutKind.Canonical, result.Layout);
        Assert.False(
            result.HasBlockingIssues,
            string.Join("; ", result.Issues.Select(issue => $"{issue.Code}: {issue.Message}")));
        Assert.Equal("activity-contract-fixture-fabric-1.20.1", result.Metadata.SuggestedProfileId);
        Assert.Equal("1.0.0", result.Metadata.Version);
        Assert.Equal("1.20.1", result.Metadata.MinecraftVersion);
        Assert.Equal(17, result.Metadata.JavaMajorVersion);
        Assert.Equal("Fabric", result.Metadata.Loader);
        Assert.Equal("0.15.11", result.Metadata.LoaderVersion);
        Assert.Equal(20, result.Metadata.MaximumPlayers);
        Assert.Equal("server/start.bat", result.Metadata.ServerLaunchPath);
        Assert.NotNull(result.Client);
        Assert.NotNull(result.Server);
        Assert.Equal(8, result.Client!.FileCount);
        Assert.Equal(7, result.Server!.FileCount);
        Assert.Equal(
            [
                "assets/indexes/1.20.json",
                "assets/objects/00/0000000000000000000000000000000000000000",
                "hechao-profile.json",
                "libraries/example/library.jar",
                "mods/client-only.jar",
                "mods/hechao-contract.jar",
                $"versions/{versionId}/{versionId}.jar",
                $"versions/{versionId}/{versionId}.json"
            ],
            ReadPaths(result.Client.Path));
        Assert.Equal(
            [
                "eula.txt",
                "fabric-server-launch.jar",
                "mods/hechao-contract.jar",
                "mods/server-only.jar",
                "server.properties",
                "start.bat",
                "user_jvm_args.txt"
            ],
            ReadPaths(result.Server.Path));
    }

    [Fact]
    public async Task AnalyzeAndSplitAsync_RejectsTraversalAndCaseCollision()
    {
        var source = CreateArchive(
            ("client/mods/Test.jar", "a"),
            ("client/mods/test.jar", "b"),
            ("server/server.properties", "max-players=20"),
            ("../escape.txt", "bad"));

        var result = await ModpackArchiveAnalyzer.AnalyzeAndSplitAsync(
            source,
            Path.Combine(root, "unsafe-out"));

        Assert.True(result.HasBlockingIssues);
        Assert.Contains(result.Issues, issue => issue.Code == "PATH_COLLISION");
        Assert.Contains(result.Issues, issue => issue.Code == "UNSAFE_ARCHIVE_ENTRY");
    }

    [Fact]
    public async Task AnalyzeAndSplitAsync_CurseForgeReferencePackFailsClosed()
    {
        var source = CreateArchive(
            ("manifest.json", """
                {
                  "name":"Reference only",
                  "version":"2.0.0",
                  "minecraft":{
                    "version":"1.20.1",
                    "modLoaders":[{"id":"forge-47.4.0","primary":true}]
                  },
                  "files":[{"projectID":1,"fileID":2,"required":true}]
                }
                """),
            ("overrides/config/example.toml", "enabled=true"));

        var result = await ModpackArchiveAnalyzer.AnalyzeAndSplitAsync(
            source,
            Path.Combine(root, "curse-out"));

        Assert.Equal(ModpackLayoutKind.CurseForge, result.Layout);
        Assert.True(result.HasBlockingIssues);
        Assert.Equal("Forge", result.Metadata.Loader);
        Assert.Equal("47.4.0", result.Metadata.LoaderVersion);
        Assert.Contains(result.Issues, issue => issue.Code == "CURSEFORGE_REMOTE_FILES");
        Assert.Contains(result.Issues, issue => issue.Code == "SERVER_PART_MISSING");
    }

    [Fact]
    public async Task AnalyzeAndSplitAsync_DoesNotTreatClientRootAsWrapper()
    {
        var source = CreateArchive(
            ("client/versions/1.21.1/1.21.1.json", "{}"),
            ("client/mods/example.jar", "mod"));

        var result = await ModpackArchiveAnalyzer.AnalyzeAndSplitAsync(
            source,
            Path.Combine(root, "client-only-out"));

        Assert.NotNull(result.Client);
        Assert.Equal(
            ["mods/example.jar", "versions/1.21.1/1.21.1.json"],
            ReadPaths(result.Client!.Path));
        Assert.Contains(result.Issues, issue => issue.Code == "SERVER_PART_MISSING");
    }

    [Fact]
    public async Task AnalyzeAndSplitAsync_DetectsArbitrarilyNamedClientAndServerRoots()
    {
        var source = CreateArchive(
            ("商业街 客户端/.minecraft/versions/1.12.2/1.12.2.json", "{}"),
            ("商业街 客户端/.minecraft/mods/example.jar", "client-mod"),
            ("商业街 客户端/.minecraft/libraries/net/minecraftforge/forge/1.12.2-14.23.5.2859/forge.jar", "forge"),
            ("商业街 客户端/.minecraft/libraries/minecraft_server.1.12.2.jar", "client-library"),
            ("商业街 服务端/server.properties", "max-players=24\n"),
            ("商业街 服务端/minecraft_server.1.12.2.jar", "server"));

        var result = await ModpackArchiveAnalyzer.AnalyzeAndSplitAsync(
            source,
            Path.Combine(root, "named-roots-out"));

        Assert.Equal(ModpackLayoutKind.Canonical, result.Layout);
        Assert.False(
            result.HasBlockingIssues,
            string.Join("; ", result.Issues.Select(issue => $"{issue.Code}: {issue.Message}")));
        Assert.NotNull(result.Client);
        Assert.NotNull(result.Server);
        Assert.Equal(
            [
                "libraries/minecraft_server.1.12.2.jar",
                "libraries/net/minecraftforge/forge/1.12.2-14.23.5.2859/forge.jar",
                "mods/example.jar",
                "versions/1.12.2/1.12.2.json"
            ],
            ReadPaths(result.Client!.Path));
        Assert.Equal(
            ["minecraft_server.1.12.2.jar", "server.properties"],
            ReadPaths(result.Server!.Path));
        Assert.DoesNotContain(result.Issues, issue => issue.Code == "CLIENT_PART_MISSING");
        Assert.DoesNotContain(result.Issues, issue => issue.Code == "SERVER_PART_MISSING");
    }

    [Fact]
    public async Task SafeZipExtractor_DeletesPartialDestinationAfterFailure()
    {
        var source = CreateArchive(
            ("safe.txt", "safe"),
            ("CON/config.txt", "unsafe"));
        var destination = Path.Combine(root, "extract");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            SafeZipExtractor.ExtractAsync(source, destination));

        Assert.False(Directory.Exists(destination));
    }

    private string CreateArchive(params (string Path, string Content)[] files)
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, Guid.NewGuid().ToString("N") + ".zip");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var file in files)
        {
            var entry = archive.CreateEntry(file.Path, CompressionLevel.Fastest);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write(file.Content);
        }

        return path;
    }

    private static string[] ReadPaths(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        return archive.Entries
            .Where(entry => !string.IsNullOrEmpty(entry.Name))
            .Select(entry => entry.FullName)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

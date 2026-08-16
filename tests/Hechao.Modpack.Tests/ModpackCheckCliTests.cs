using System.IO.Compression;
using System.Text;
using Hechao.Modpack.Check;

namespace Hechao.Modpack.Tests;

[Collection("Console CLI")]
public sealed class ModpackCheckCliTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "hechao-modpack-cli-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Main_ReturnsExpectedExitCodesAndWritesJsonReport()
    {
        var compliant = CreateArchive(
            "Fabric",
            "java @user_jvm_args.txt -jar fabric-server-launch.jar nogui",
            ("server/fabric-server-launch.jar", "fabric"));
        var blocked = CreateArchive(
            "Arclight",
            "java @user_jvm_args.txt @libraries/net/neoforged/neoforge/21.1.228/win_args.txt nogui",
            ("server/arclight-neoforge-1.21.1.jar", "arclight"),
            ("server/libraries/net/neoforged/neoforge/21.1.228/win_args.txt", "args"));
        var compliantReport = Path.Combine(root, "compliant-report.json");
        var blockedReport = Path.Combine(root, "blocked-report.json");

        var compliantExitCode = await Program.Main(
            [compliant, "--quiet", "--json", compliantReport]);
        var blockedExitCode = await Program.Main(
            [blocked, "--quiet", "--json", blockedReport]);

        Assert.Equal(0, compliantExitCode);
        Assert.Equal(2, blockedExitCode);
        Assert.True(File.Exists(compliantReport));
        var json = await File.ReadAllTextAsync(blockedReport);
        Assert.Contains("\"readiness\": \"blocked\"", json);
        Assert.Contains("ARCLIGHT_BYPASSED", json);
    }

    [Fact]
    public async Task Main_ReturnsExecutionErrorForMissingArchive()
    {
        var exitCode = await Program.Main([Path.Combine(root, "missing.zip"), "--quiet"]);

        Assert.Equal(3, exitCode);
    }

    private string CreateArchive(
        string serverCore,
        string launchCommand,
        params (string Path, string Content)[] additionalServerFiles)
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, Guid.NewGuid().ToString("N") + ".zip");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        var loader = serverCore == "Fabric" ? "Fabric" : "NeoForge";
        Add(archive, "hechao-pack.json", $$"""
            {
              "schemaVersion":1,
              "id":"cli-test",
              "displayName":"CLI 测试",
              "version":"1.0.0",
              "minecraftVersion":"1.21.1",
              "javaMajorVersion":21,
              "loader":"{{loader}}",
              "loaderVersion":"21.1.228",
              "serverCore":"{{serverCore}}",
              "clientRoot":"client",
              "serverRoot":"server",
              "sharedRoot":"shared"
            }
            """);
        Add(archive, "client/versions/1.21.1/1.21.1.json", "{}");
        Add(archive, "server/server.properties", "server-ip=127.0.0.1\nonline-mode=false\n");
        Add(archive, "server/eula.txt", "eula=true\n");
        Add(archive, "server/user_jvm_args.txt", "-Xms1024M\n-Xmx4096M\n");
        Add(
            archive,
            "server/start.bat",
            $"@echo off\nif not defined HECHAO_MANAGED_START pause\n{launchCommand}\n");
        foreach (var file in additionalServerFiles)
        {
            Add(archive, file.Path, file.Content);
        }

        return path;
    }

    private static void Add(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Fastest);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

[CollectionDefinition("Console CLI", DisableParallelization = true)]
public sealed class ConsoleCliCollection;

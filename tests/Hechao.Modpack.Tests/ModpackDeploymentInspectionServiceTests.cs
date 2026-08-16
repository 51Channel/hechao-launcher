using System.IO.Compression;
using System.Text;

namespace Hechao.Modpack.Tests;

public sealed class ModpackDeploymentInspectionServiceTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "hechao-modpack-report-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task InspectAsync_ReturnsCompliantReportForManagedFabricPackage()
    {
        var archive = CreateArchive(
            "Fabric",
            "java @user_jvm_args.txt -jar fabric-server-launch.jar nogui",
            ("server/fabric-server-launch.jar", "fabric"));
        var service = new ModpackDeploymentInspectionService();

        var report = await service.InspectAsync(archive);

        Assert.Equal(DeploymentReadiness.Compliant, report.Readiness);
        Assert.Equal(0, report.BlockingCount);
        Assert.Equal(0, report.WarningCount);
        Assert.True(report.PassedCount > 0);
        Assert.Equal("Fabric", report.ServerDeployment?.DeclaredCore);
        Assert.Equal(ServerCoreKind.Fabric, report.ServerDeployment?.LaunchCore);
        Assert.Matches("^[0-9A-F]{64}$", report.ArchiveSha256);
    }

    [Fact]
    public async Task InspectAsync_BlocksArclightPackageThatLaunchesNeoForge()
    {
        var archive = CreateArchive(
            "Arclight",
            "java @user_jvm_args.txt @libraries/net/neoforged/neoforge/21.1.228/win_args.txt nogui",
            ("server/arclight-neoforge-1.21.1.jar", "arclight"),
            ("server/libraries/net/neoforged/neoforge/21.1.228/win_args.txt", "args"));
        var service = new ModpackDeploymentInspectionService();

        var report = await service.InspectAsync(archive);
        var json = ModpackDeploymentInspectionService.SerializeJson(report);

        Assert.Equal(DeploymentReadiness.Blocked, report.Readiness);
        Assert.Contains(report.Checks, check => check.Code == "ARCLIGHT_BYPASSED");
        Assert.Contains("\"readiness\": \"blocked\"", json);
        Assert.DoesNotContain(root, json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InspectAsync_RejectsUnsupportedArchiveExtension()
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "package.rar");
        await File.WriteAllTextAsync(path, "not a supported archive");
        var service = new ModpackDeploymentInspectionService();

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.InspectAsync(path));

        Assert.Contains("ZIP", exception.Message);
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
              "id":"inspection-test",
              "displayName":"检查器测试",
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

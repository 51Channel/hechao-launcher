using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Hechao.Contracts;

namespace Hechao.ServerControlAgent.Tests;

public sealed class ServerPackageDeployerTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "hechao-package-deployer-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task DeployAsync_DeploysFreshSlotFromHostManagedSnapshot()
    {
        var server = Path.Combine(root, "ActivityNeoForge");
        var backup = Path.Combine(root, "agent-backups");
        Directory.CreateDirectory(server);
        await File.WriteAllTextAsync(
            Path.Combine(server, "forwarding.secret"),
            "host-secret");
        var configuration = CreateConfiguration(server);
        new HostManagedSnapshotStore(configuration, backup).CaptureFromServer();
        Directory.Delete(server, recursive: true);
        var archive = CreateServerArchive();
        var metadata = await ReadArchiveMetadataAsync(archive);
        var deployment = new ServerPackageDeploymentRequest(
            Guid.NewGuid(),
            "summer-neoforge-1.21.11",
            "1.0.0",
            new FileInfo(archive).Length,
            await ComputeSha256Async(archive),
            metadata.ExpandedBytes,
            metadata.FileCount,
            PreserveWorldData: false,
            InitialMemoryMiB: 2048,
            MaximumMemoryMiB: 4096);
        var deployer = new ServerPackageDeployer(
            configuration,
            backup);

        var result = await deployer.DeployAsync(
            deployment,
            archive,
            _ => Task.FromResult<int?>(null),
            CancellationToken.None);

        Assert.Equal(ServerControlCommandOutcome.Succeeded, result.Outcome);
        Assert.Equal("PACKAGE_DEPLOYED_FRESH_STOPPED", result.ResultCode);
        Assert.Equal(
            "host-secret",
            await File.ReadAllTextAsync(
                Path.Combine(server, "forwarding.secret")));
        Assert.True(File.Exists(Path.Combine(server, "new-server.jar")));
        Assert.False(Directory.Exists(
            Path.Combine(root, ".ActivityNeoForge.hechao-rollback")));
    }

    [Fact]
    public async Task DeployAsync_ReplacesServerAtomicallyAndPreservesFixedData()
    {
        var server = Path.Combine(root, "ActivityNeoForge");
        var backup = Path.Combine(root, "agent-backups");
        Directory.CreateDirectory(Path.Combine(server, "world"));
        await File.WriteAllTextAsync(
            Path.Combine(server, "world", "level.dat"),
            "old-world");
        await File.WriteAllTextAsync(
            Path.Combine(server, "forwarding.secret"),
            "host-secret");
        var economyToken = Path.Combine(
            server,
            "plugins",
            "HechaoEconomy",
            "economy-token.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(economyToken)!);
        await File.WriteAllTextAsync(economyToken, "economy-token");
        await File.WriteAllTextAsync(
            Path.Combine(server, "old-server.jar"),
            "old");
        await File.WriteAllTextAsync(
            Path.Combine(server, "server.properties"),
            "server-port=25568\n");
        await File.WriteAllTextAsync(
            Path.Combine(server, "user_jvm_args.txt"),
            "-Xms1G -Xmx2G\n");
        var archive = CreateServerArchive();
        var metadata = await ReadArchiveMetadataAsync(archive);
        var deployment = new ServerPackageDeploymentRequest(
            Guid.NewGuid(),
            "summer-neoforge-1.21.11",
            "1.0.0",
            new FileInfo(archive).Length,
            await ComputeSha256Async(archive),
            metadata.ExpandedBytes,
            metadata.FileCount,
            PreserveWorldData: true,
            InitialMemoryMiB: 2048,
            MaximumMemoryMiB: 4096);
        var deployer = new ServerPackageDeployer(
            CreateConfiguration(
                server,
                ["forwarding.secret", @"plugins\HechaoEconomy\economy-token.txt"]),
            backup);

        var result = await deployer.DeployAsync(
            deployment,
            archive,
            _ => Task.FromResult<int?>(null),
            CancellationToken.None);

        Assert.True(
            result.Outcome == ServerControlCommandOutcome.Succeeded,
            result.ResultMessage);
        Assert.Equal("PACKAGE_DEPLOYED_STOPPED", result.ResultCode);
        Assert.True(File.Exists(Path.Combine(server, "new-server.jar")));
        Assert.False(File.Exists(Path.Combine(server, "old-server.jar")));
        Assert.Equal(
            "old-world",
            await File.ReadAllTextAsync(
                Path.Combine(server, "world", "level.dat")));
        Assert.Equal(
            "host-secret",
            await File.ReadAllTextAsync(
                Path.Combine(server, "forwarding.secret")));
        Assert.Equal(
            "economy-token",
            await File.ReadAllTextAsync(economyToken));
        Assert.Contains(
            "-Xms2048M -Xmx4096M",
            await File.ReadAllTextAsync(
                Path.Combine(server, "user_jvm_args.txt")));
        var properties = await File.ReadAllTextAsync(
            Path.Combine(server, "server.properties"));
        Assert.Contains("server-ip=127.0.0.1", properties);
        Assert.Contains("server-port=25568", properties);
        Assert.Contains("online-mode=false", properties);
        Assert.True(File.Exists(Path.Combine(server, ".hechao-deployment.json")));
        Assert.Equal(
            new ServerPackageDeploymentIdentity(
                deployment.ImportId,
                deployment.ProfileId,
                deployment.Version),
            ServerPackageDeployer.ReadDeploymentIdentity(server));
        Assert.True(Directory.Exists(
            Path.Combine(root, ".ActivityNeoForge.hechao-rollback")));

        var repeated = await deployer.DeployAsync(
            deployment,
            archive,
            _ => Task.FromResult<int?>(null),
            CancellationToken.None);
        Assert.Equal("PACKAGE_ALREADY_DEPLOYED", repeated.ResultCode);
    }

    [Fact]
    public async Task DeployAsync_RejectsBadDigestWithoutChangingCurrentServer()
    {
        var server = Path.Combine(root, "ActivityNeoForge");
        Directory.CreateDirectory(server);
        await File.WriteAllTextAsync(
            Path.Combine(server, "keep.txt"),
            "unchanged");
        var archive = CreateServerArchive();
        var metadata = await ReadArchiveMetadataAsync(archive);
        var deployment = new ServerPackageDeploymentRequest(
            Guid.NewGuid(),
            "summer-neoforge-1.21.11",
            "1.0.0",
            new FileInfo(archive).Length,
            new string('a', 64),
            metadata.ExpandedBytes,
            metadata.FileCount,
            PreserveWorldData: false,
            InitialMemoryMiB: 2048,
            MaximumMemoryMiB: 4096);
        var deployer = new ServerPackageDeployer(
            CreateConfiguration(server),
            Path.Combine(root, "backups"));

        var result = await deployer.DeployAsync(
            deployment,
            archive,
            _ => Task.FromResult<int?>(null),
            CancellationToken.None);

        Assert.Equal(ServerControlCommandOutcome.Failed, result.Outcome);
        Assert.Equal("PACKAGE_DEPLOY_FAILED", result.ResultCode);
        Assert.Equal(
            "unchanged",
            await File.ReadAllTextAsync(Path.Combine(server, "keep.txt")));
    }

    [Fact]
    public async Task DeployAsync_RejectsMissingHostManagedFileWithoutSwitching()
    {
        var server = Path.Combine(root, "ActivityNeoForge");
        Directory.CreateDirectory(server);
        await File.WriteAllTextAsync(
            Path.Combine(server, "keep.txt"),
            "unchanged");
        var archive = CreateServerArchive();
        var metadata = await ReadArchiveMetadataAsync(archive);
        var deployment = new ServerPackageDeploymentRequest(
            Guid.NewGuid(),
            "summer-neoforge-1.21.11",
            "1.0.0",
            new FileInfo(archive).Length,
            await ComputeSha256Async(archive),
            metadata.ExpandedBytes,
            metadata.FileCount,
            PreserveWorldData: false,
            InitialMemoryMiB: 2048,
            MaximumMemoryMiB: 4096);
        var deployer = new ServerPackageDeployer(
            CreateConfiguration(server),
            Path.Combine(root, "backups"));

        var result = await deployer.DeployAsync(
            deployment,
            archive,
            _ => Task.FromResult<int?>(null),
            CancellationToken.None);

        Assert.Equal(ServerControlCommandOutcome.Failed, result.Outcome);
        Assert.Equal("PACKAGE_DEPLOY_FAILED", result.ResultCode);
        Assert.Equal(
            "unchanged",
            await File.ReadAllTextAsync(Path.Combine(server, "keep.txt")));
        Assert.False(File.Exists(Path.Combine(server, "new-server.jar")));
    }

    [Fact]
    public async Task DeployAsync_RejectsPackageWithoutManagedStartScript()
    {
        var server = Path.Combine(root, "ActivityNeoForge");
        Directory.CreateDirectory(server);
        await File.WriteAllTextAsync(
            Path.Combine(server, "keep.txt"),
            "unchanged");
        await File.WriteAllTextAsync(
            Path.Combine(server, "forwarding.secret"),
            "host-secret");
        var archive = CreateServerArchive();
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Update))
        {
            zip.GetEntry("start.bat")!.Delete();
        }

        var metadata = await ReadArchiveMetadataAsync(archive);
        var deployment = new ServerPackageDeploymentRequest(
            Guid.NewGuid(),
            "summer-neoforge-1.21.11",
            "1.0.0",
            new FileInfo(archive).Length,
            await ComputeSha256Async(archive),
            metadata.ExpandedBytes,
            metadata.FileCount,
            PreserveWorldData: false,
            InitialMemoryMiB: 2048,
            MaximumMemoryMiB: 4096);
        var deployer = new ServerPackageDeployer(
            CreateConfiguration(server),
            Path.Combine(root, "backups"));

        var result = await deployer.DeployAsync(
            deployment,
            archive,
            _ => Task.FromResult<int?>(null),
            CancellationToken.None);

        Assert.Equal(ServerControlCommandOutcome.Failed, result.Outcome);
        Assert.Equal("PACKAGE_DEPLOY_FAILED", result.ResultCode);
        Assert.Contains("managed start script", result.ResultMessage);
        Assert.Equal(
            "unchanged",
            await File.ReadAllTextAsync(Path.Combine(server, "keep.txt")));
    }

    [Fact]
    public async Task DeployAsync_RecoversPartialPreservationWithoutReplacingOldWorld()
    {
        var importId = Guid.NewGuid();
        var server = Path.Combine(root, "ActivityNeoForge");
        var staging = Path.Combine(
            root,
            $".ActivityNeoForge.hechao-staging-{importId:N}");
        var rollback = Path.Combine(root, ".ActivityNeoForge.hechao-rollback");
        Directory.CreateDirectory(Path.Combine(staging, "world"));
        Directory.CreateDirectory(Path.Combine(rollback, "world"));
        await File.WriteAllTextAsync(
            Path.Combine(staging, "world", "level.dat"),
            "package-world");
        await File.WriteAllTextAsync(
            Path.Combine(staging, "forwarding.secret"),
            "old-secret");
        await File.WriteAllTextAsync(
            Path.Combine(rollback, "world", "level.dat"),
            "old-world");
        await File.WriteAllTextAsync(
            Path.Combine(rollback, "server.properties"),
            "server-port=25568\n");
        await File.WriteAllTextAsync(
            Path.Combine(rollback, "user_jvm_args.txt"),
            "-Xms1G -Xmx2G\n");

        var archive = CreateServerArchive();
        var metadata = await ReadArchiveMetadataAsync(archive);
        var digest = await ComputeSha256Async(archive);
        var owner = $$"""
            {
              "schemaVersion": 1,
              "importId": "{{importId:D}}",
              "archiveSha256": "{{digest}}",
              "preserveWorldData": true
            }
            """;
        await File.WriteAllTextAsync(staging + ".owner.json", owner);
        await File.WriteAllTextAsync(rollback + ".owner.json", owner);
        var deployment = new ServerPackageDeploymentRequest(
            importId,
            "summer-neoforge-1.21.11",
            "1.0.0",
            new FileInfo(archive).Length,
            digest,
            metadata.ExpandedBytes,
            metadata.FileCount,
            PreserveWorldData: true,
            InitialMemoryMiB: 2048,
            MaximumMemoryMiB: 4096);
        var deployer = new ServerPackageDeployer(
            CreateConfiguration(server),
            Path.Combine(root, "backups"));

        var result = await deployer.DeployAsync(
            deployment,
            archive,
            _ => Task.FromResult<int?>(null),
            CancellationToken.None);

        Assert.Equal(ServerControlCommandOutcome.Succeeded, result.Outcome);
        Assert.Equal(
            "old-world",
            await File.ReadAllTextAsync(
                Path.Combine(server, "world", "level.dat")));
        Assert.Equal(
            "old-secret",
            await File.ReadAllTextAsync(
                Path.Combine(server, "forwarding.secret")));
    }

    private ServerControlTargetConfiguration CreateConfiguration(
        string server,
        IReadOnlyList<string>? hostManagedRelativePaths = null) =>
        new()
        {
            ServerId = "activity",
            ServerDirectory = server,
            StartTaskName = "Hechao-Server-ActivityNeoForge",
            Port = 25568,
            ConflictGroup = "owl5-activity-slot",
            PropertiesRelativePath = "server.properties",
            MemorySettingsRelativePath = "user_jvm_args.txt",
            StartScriptRelativePath = "start.bat",
            MaximumAllowedMemoryMiB = 8192,
            PackageDeploymentEnabled = true,
            HostManagedRelativePaths =
                hostManagedRelativePaths ?? ["forwarding.secret"],
            WorldDataRelativePaths = ["world", "world_nether", "world_the_end"],
            AllowedCommandPrefixes = ["list"]
        };

    private string CreateServerArchive()
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "server.zip");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        Add(archive, "new-server.jar", "new");
        Add(
            archive,
            "server.properties",
            "server-ip=0.0.0.0\nserver-port=25565\nonline-mode=true\n");
        Add(archive, "user_jvm_args.txt", "-Xms1G -Xmx2G\n");
        Add(
            archive,
            "start.bat",
            "@echo off\r\nif not defined HECHAO_MANAGED_START pause\r\njava @user_jvm_args.txt -jar new-server.jar nogui\r\n");
        Add(archive, "world/level.dat", "package-world");
        Add(archive, "forwarding.secret", "package-secret");
        return path;
    }

    private static void Add(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Fastest);
        using var writer = new StreamWriter(
            entry.Open(),
            new UTF8Encoding(false));
        writer.Write(content);
    }

    private static async Task<(long ExpandedBytes, int FileCount)>
        ReadArchiveMetadataAsync(string path)
    {
        await Task.Yield();
        using var archive = ZipFile.OpenRead(path);
        return (
            archive.Entries.Where(entry => !string.IsNullOrEmpty(entry.Name))
                .Sum(entry => entry.Length),
            archive.Entries.Count(entry => !string.IsNullOrEmpty(entry.Name)));
    }

    private static async Task<string> ComputeSha256Async(string path)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(
            await SHA256.HashDataAsync(stream)).ToLowerInvariant();
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

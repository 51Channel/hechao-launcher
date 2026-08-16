using System.IO.Compression;
using System.Text;
using Hechao.Api.PackageImports;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Hechao.Api.Tests;

public sealed class PackageImportStorageTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "hechao-package-storage-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task AppendCompleteAndAnalyze_ResumesByExactOffset()
    {
        var storage = CreateStorage();
        var importId = Guid.NewGuid();
        var archive = CreateCanonicalArchive();
        var bytes = await File.ReadAllBytesAsync(archive);
        storage.Initialize(importId);

        var midpoint = bytes.Length / 2;
        var first = await storage.AppendAsync(
            importId,
            0,
            bytes.Length,
            new MemoryStream(bytes[..midpoint]),
            CancellationToken.None);
        Assert.Equal(midpoint, first);
        var mismatch = await Assert.ThrowsAsync<PackageUploadOffsetException>(() =>
            storage.AppendAsync(
                importId,
                0,
                bytes.Length,
                new MemoryStream(bytes[midpoint..]),
                CancellationToken.None));
        Assert.Equal(midpoint, mismatch.ActualOffset);

        var completedBytes = await storage.AppendAsync(
            importId,
            midpoint,
            bytes.Length,
            new MemoryStream(bytes[midpoint..]),
            CancellationToken.None);
        Assert.Equal(bytes.Length, completedBytes);
        var completed = await storage.CompleteUploadAsync(
            importId,
            bytes.Length,
            CancellationToken.None);
        Assert.Equal(bytes.Length, completed.Bytes);
        Assert.Equal(64, completed.Sha256.Length);

        var analysis = await storage.AnalyzeAsync(importId, CancellationToken.None);
        Assert.False(analysis.HasBlockingIssues);
        Assert.NotNull(analysis.Client);
        Assert.NotNull(analysis.Server);
        Assert.Equal("Fabric", analysis.Metadata.Loader);
        Assert.Equal(1, analysis.SharedFileCount);
        await using var client = storage.OpenClientArchive(importId);
        await using var server = storage.OpenServerArchive(importId);
        Assert.True(client.Length > 0);
        Assert.True(server.Length > 0);
    }

    [Fact]
    public async Task AppendAsync_RejectsChunkLargerThanConfiguredLimitWithoutChangingUpload()
    {
        var storage = CreateStorage(chunkBytes: 1024 * 1024);
        var importId = Guid.NewGuid();
        storage.Initialize(importId);
        var oversized = new byte[1024 * 1024 + 1];

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            storage.AppendAsync(
                importId,
                0,
                oversized.Length,
                new MemoryStream(oversized),
                CancellationToken.None));

        Assert.Equal(0, storage.GetUploadedBytes(importId));
    }

    [Fact]
    public async Task AnalyzeAsync_BlocksArclightArchiveThatLaunchesNeoForge()
    {
        var storage = CreateStorage();
        var importId = Guid.NewGuid();
        var archive = CreateArclightBypassArchive();
        var bytes = await File.ReadAllBytesAsync(archive);
        storage.Initialize(importId);

        await storage.AppendAsync(
            importId,
            0,
            bytes.Length,
            new MemoryStream(bytes),
            CancellationToken.None);
        await storage.CompleteUploadAsync(
            importId,
            bytes.Length,
            CancellationToken.None);

        var analysis = await storage.AnalyzeAsync(importId, CancellationToken.None);

        Assert.True(analysis.HasBlockingIssues);
        Assert.Contains(analysis.Issues, issue => issue.Code == "ARCLIGHT_BYPASSED");
    }

    private PackageImportStorage CreateStorage(int chunkBytes = 1024 * 1024) =>
        new(
            Options.Create(new PackageImportOptions
            {
                Enabled = true,
                StorageRoot = root,
                MaximumUploadBytes = 64 * 1024 * 1024,
                UploadChunkBytes = chunkBytes,
                MaximumEntries = 10_000,
                MaximumExpandedBytes = 64 * 1024 * 1024,
                MaximumEntryBytes = 64 * 1024 * 1024,
                PublisherTokenSha256 = new string('a', 64)
            }),
            NullLogger<PackageImportStorage>.Instance);

    private string CreateCanonicalArchive()
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "source.zip");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        Add(archive, "hechao-pack.json", """
            {
              "schemaVersion":1,
              "id":"summer-fabric-1.20.1",
              "displayName":"夏日活动",
              "version":"1.0.0",
              "minecraftVersion":"1.20.1",
              "javaMajorVersion":17,
              "loader":"Fabric",
              "loaderVersion":"0.16.14",
              "serverCore":"Fabric",
              "clientRoot":"client",
              "serverRoot":"server",
              "sharedRoot":"shared"
            }
            """);
        Add(archive, "client/versions/1.20.1/1.20.1.json", "{}");
        Add(archive, "shared/mods/common.jar", "mod");
        Add(
            archive,
            "server/server.properties",
            "server-ip=127.0.0.1\nonline-mode=false\nmax-players=20\n");
        Add(archive, "server/eula.txt", "eula=true\n");
        Add(archive, "server/user_jvm_args.txt", "-Xms1024M\n-Xmx4096M\n");
        Add(
            archive,
            "server/start.bat",
            "@echo off\nif not defined HECHAO_MANAGED_START pause\njava @user_jvm_args.txt -jar fabric-server-launch.jar nogui\n");
        Add(archive, "server/fabric-server-launch.jar", "fabric");
        return path;
    }

    private string CreateArclightBypassArchive()
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "arclight-bypass.zip");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        Add(archive, "hechao-pack.json", """
            {
              "schemaVersion":1,
              "id":"arclight-bypass",
              "displayName":"Arclight 绕过测试",
              "version":"1.0.0",
              "minecraftVersion":"1.21.1",
              "javaMajorVersion":21,
              "loader":"NeoForge",
              "loaderVersion":"21.1.228",
              "serverCore":"Arclight",
              "clientRoot":"client",
              "serverRoot":"server",
              "sharedRoot":"shared"
            }
            """);
        Add(archive, "client/versions/1.21.1/1.21.1.json", "{}");
        Add(
            archive,
            "server/server.properties",
            "server-ip=127.0.0.1\nonline-mode=false\n");
        Add(archive, "server/eula.txt", "eula=true\n");
        Add(archive, "server/user_jvm_args.txt", "-Xms1024M\n-Xmx4096M\n");
        Add(
            archive,
            "server/start.bat",
            "@echo off\nif not defined HECHAO_MANAGED_START pause\n" +
            "java @user_jvm_args.txt @libraries/net/neoforged/neoforge/21.1.228/win_args.txt nogui\n");
        Add(archive, "server/arclight-neoforge-1.21.1.jar", "arclight");
        Add(
            archive,
            "server/libraries/net/neoforged/neoforge/21.1.228/win_args.txt",
            "args");
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

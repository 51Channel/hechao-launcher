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
              "clientRoot":"client",
              "serverRoot":"server",
              "sharedRoot":"shared"
            }
            """);
        Add(archive, "client/versions/1.20.1/1.20.1.json", "{}");
        Add(archive, "shared/mods/common.jar", "mod");
        Add(archive, "server/server.properties", "max-players=20");
        Add(archive, "server/start.bat", "java -jar fabric-server-launch.jar nogui");
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

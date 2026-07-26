using System.IO.Compression;
using System.Text.Json;
using Hechao.Api.Diagnostics;
using Hechao.Contracts;
using Microsoft.Extensions.Options;

namespace Hechao.Api.Tests;

public sealed class DiagnosticUploadRulesTests
{
    [Fact]
    public void ValidateCreateRequest_AcceptsBoundedSignedMetadata()
    {
        var options = new DiagnosticUploadOptions();
        var request = new DiagnosticUploadCreateRequest(
            "activity-neoforge-1.21.11",
            1234,
            new string('a', 64),
            "0.11.12");

        var errors = DiagnosticUploadRules.ValidateCreateRequest(request, options);

        Assert.Empty(errors);
    }

    [Fact]
    public void UploadToken_IsStrongUrlSafeAndHashable()
    {
        var first = DiagnosticUploadRules.CreateUploadToken();
        var second = DiagnosticUploadRules.CreateUploadToken();

        Assert.True(DiagnosticUploadRules.IsValidUploadToken(first));
        Assert.True(DiagnosticUploadRules.IsValidUploadToken(second));
        Assert.NotEqual(first, second);
        Assert.Matches("^[0-9a-f]{64}$", DiagnosticUploadRules.HashUploadToken(first));
    }

    [Fact]
    public async Task ValidateArchiveAsync_AcceptsLauncherDiagnosticShape()
    {
        using var temporary = new TemporaryDirectory();
        var path = Path.Combine(temporary.Path, "diagnostic.zip");
        CreateArchive(
            path,
            ("diagnostic.json", JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                profileId = "base-1.21.11"
            })),
            ("README.txt", "safe"),
            ("logs/latest.log", "redacted"),
            ("crash-reports/crash-2026-07-27_12.00.00-client.txt", "stack"));

        await DiagnosticUploadRules.ValidateArchiveAsync(
            path,
            "base-1.21.11",
            CancellationToken.None);
    }

    [Theory]
    [InlineData("../credential.txt")]
    [InlineData("saves/world/level.dat")]
    [InlineData("logs/debug.log")]
    public async Task ValidateArchiveAsync_RejectsUnexpectedEntries(string entryName)
    {
        using var temporary = new TemporaryDirectory();
        var path = Path.Combine(temporary.Path, "diagnostic.zip");
        CreateArchive(
            path,
            ("diagnostic.json", JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                profileId = "base-1.21.11"
            })),
            ("README.txt", "safe"),
            (entryName, "private"));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            DiagnosticUploadRules.ValidateArchiveAsync(
                path,
                "base-1.21.11",
                CancellationToken.None));
    }

    [Fact]
    public async Task ValidateArchiveAsync_RejectsProfileMismatch()
    {
        using var temporary = new TemporaryDirectory();
        var path = Path.Combine(temporary.Path, "diagnostic.zip");
        CreateArchive(
            path,
            ("diagnostic.json", JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                profileId = "base-1.21.11"
            })),
            ("README.txt", "safe"));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            DiagnosticUploadRules.ValidateArchiveAsync(
                path,
                "activity-neoforge-1.21.11",
                CancellationToken.None));
    }

    [Fact]
    public void Storage_CommitsReadsAndDeletesOnlyItsUploadId()
    {
        using var temporary = new TemporaryDirectory();
        var options = Options.Create(new DiagnosticUploadOptions
        {
            StorageRoot = temporary.Path
        });
        var storage = new DiagnosticUploadStorage(options);
        var uploadId = Guid.NewGuid();

        using (var stream = storage.CreateTemporaryFile(uploadId))
        {
            stream.Write([1, 2, 3, 4]);
        }

        storage.Commit(uploadId);
        Assert.True(storage.ArchiveExists(uploadId));
        using (var stream = storage.OpenRead(uploadId))
        {
            Assert.Equal(4, stream.Length);
        }

        storage.Delete(uploadId);
        Assert.False(storage.ArchiveExists(uploadId));
    }

    private static void CreateArchive(
        string path,
        params (string Name, string Content)[] entries)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            var entry = archive.CreateEntry(name);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(content);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "hechao-diagnostics-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (DirectoryNotFoundException)
            {
            }
        }
    }
}

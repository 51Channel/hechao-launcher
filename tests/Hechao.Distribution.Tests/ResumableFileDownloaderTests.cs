using System.Security.Cryptography;

namespace Hechao.Distribution.Tests;

public sealed class ResumableFileDownloaderTests
{
    [Fact]
    public async Task DownloadAsync_ResumesExistingPartialFileWithRangeRequest()
    {
        var content = "resume-this-download"u8.ToArray();
        var digest = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        var handler = new RangeResponseHandler(content);
        using var httpClient = new HttpClient(handler);
        var downloader = new ResumableFileDownloader(httpClient);
        using var temporary = new TemporaryDirectory();
        var destination = Path.Combine(temporary.Path, "object");
        await File.WriteAllBytesAsync(destination + ".part", content[..7]);
        var manifestFile = new ClientManifestFile(
            "mods/example.jar",
            content.Length,
            digest,
            "https://download.hechao.world/object");

        await downloader.DownloadAsync(manifestFile, destination);

        Assert.Equal([7L], handler.RequestedOffsets);
        Assert.Equal(content, await File.ReadAllBytesAsync(destination));
        Assert.False(File.Exists(destination + ".part"));
    }

    [Fact]
    public async Task DownloadAsync_RejectsContentWithWrongDigest()
    {
        var content = "corrupted-content"u8.ToArray();
        var handler = new RangeResponseHandler(content);
        using var httpClient = new HttpClient(handler);
        var downloader = new ResumableFileDownloader(httpClient);
        using var temporary = new TemporaryDirectory();
        var destination = Path.Combine(temporary.Path, "object");
        var manifestFile = new ClientManifestFile(
            "mods/example.jar",
            content.Length,
            new string('0', 64),
            "https://download.hechao.world/object");

        await Assert.ThrowsAsync<ManifestIntegrityException>(() =>
            downloader.DownloadAsync(manifestFile, destination));

        Assert.False(File.Exists(destination));
        Assert.False(File.Exists(destination + ".part"));
        Assert.Equal(3, handler.RequestedOffsets.Count);
    }

    [Fact]
    public async Task DownloadAsync_PreservesRangeButDoesNotLeakBearerTokenAcrossRedirect()
    {
        var content = "private-oss-download"u8.ToArray();
        var digest = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        var redirectHandler = new AuthenticatedRedirectHandler(content);
        using var authorizationHandler = new OriginBoundBearerTokenHandler(
            new Uri("https://launcher-api.hechao.world/"),
            _ => ValueTask.FromResult("launcher-access-token"))
        {
            InnerHandler = redirectHandler
        };
        using var httpClient = new HttpClient(authorizationHandler);
        var downloader = new ResumableFileDownloader(httpClient);
        using var temporary = new TemporaryDirectory();
        var destination = Path.Combine(temporary.Path, "object");
        await File.WriteAllBytesAsync(destination + ".part", content[..8]);
        var manifestFile = new ClientManifestFile(
            "mods/private.jar",
            content.Length,
            digest,
            "https://launcher-api.hechao.world/v1/profiles/activity/objects/aa/" + digest);

        await downloader.DownloadAsync(manifestFile, destination);

        Assert.Equal(2, redirectHandler.Requests.Count);
        Assert.Equal("launcher-api.hechao.world", redirectHandler.Requests[0].Host);
        Assert.Equal("Bearer launcher-access-token", redirectHandler.Requests[0].Authorization);
        Assert.Equal("download.hechao.world", redirectHandler.Requests[1].Host);
        Assert.Null(redirectHandler.Requests[1].Authorization);
        Assert.All(redirectHandler.Requests, request => Assert.Equal(8, request.RangeOffset));
        Assert.Equal(content, await File.ReadAllBytesAsync(destination));
    }
}

using System.Net;
using System.Security.Cryptography;
using Hechao.Contracts;

namespace Hechao.Publisher.Tests;

public sealed class PackagePublisherApiClientTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "hechao-publisher-api-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task DownloadClientArchiveAsync_ResumesAndVerifiesImmutableDigest()
    {
        Directory.CreateDirectory(root);
        var content = Enumerable.Range(0, 4096)
            .Select(index => (byte)(index % 251))
            .ToArray();
        var destination = Path.Combine(root, "client.zip");
        await File.WriteAllBytesAsync(destination, content[..1000]);
        var handler = new RecordingHandler(request =>
        {
            Assert.Equal("bytes=1000-", request.Headers.Range?.ToString());
            Assert.True(request.Headers.Contains("X-Hechao-Package-Publisher-Token"));
            return new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent(content[1000..])
            };
        });
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://launcher-api.example/")
        };
        var client = new PackagePublisherApiClient(
            httpClient,
            "publisher-main",
            new string('A', 48));
        var job = new PackagePublisherJobDelivery(
            Guid.NewGuid(),
            1,
            "profile-id",
            "1.0.0",
            "1.20.1",
            17,
            "Fabric",
            "0.16.14",
            content.Length,
            Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant());

        await client.DownloadClientArchiveAsync(
            job,
            destination,
            CancellationToken.None);

        Assert.Equal(content, await File.ReadAllBytesAsync(destination));
    }

    [Fact]
    public async Task DownloadClientArchiveAsync_DoesNotFollowRedirects()
    {
        Directory.CreateDirectory(root);
        var handler = new RecordingHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Redirect);
            response.Headers.Location = new Uri("https://untrusted.example/file");
            return response;
        });
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://launcher-api.example/")
        };
        var client = new PackagePublisherApiClient(
            httpClient,
            "publisher-main",
            new string('A', 48),
            _ => TimeSpan.Zero);
        var job = new PackagePublisherJobDelivery(
            Guid.NewGuid(), 1, "profile-id", "1.0.0", "1.20.1", 17,
            "Fabric", "0.16.14", 1, new string('a', 64));

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.DownloadClientArchiveAsync(
                job,
                Path.Combine(root, "redirect.zip"),
                CancellationToken.None));
    }

    [Fact]
    public async Task DownloadClientArchiveAsync_RestartsAfterBadFullCache()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(
            "verified client archive");
        var digest = Convert.ToHexString(SHA256.HashData(bytes))
            .ToLowerInvariant();
        var destination = Path.Combine(root, "client.zip");
        Directory.CreateDirectory(root);
        await File.WriteAllBytesAsync(destination, new byte[bytes.Length]);
        var requests = 0;
        using var handler = new RecordingHandler(request =>
        {
            requests += 1;
            if (requests == 1)
            {
                Assert.Equal(bytes.Length, request.Headers.Range?.Ranges.Single().From);
                return new HttpResponseMessage(
                    HttpStatusCode.RequestedRangeNotSatisfiable);
            }

            Assert.Null(request.Headers.Range);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bytes)
            };
        });
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://launcher-api.example/")
        };
        var client = new PackagePublisherApiClient(
            httpClient,
            "publisher-main",
            new string('A', 48),
            _ => TimeSpan.Zero);
        var job = new PackagePublisherJobDelivery(
            Guid.NewGuid(),
            1,
            "profile-id",
            "1.0.0",
            "1.20.1",
            17,
            "Fabric",
            "0.16.14",
            bytes.Length,
            digest);

        await client.DownloadClientArchiveAsync(
            job,
            destination,
            CancellationToken.None);

        Assert.Equal(2, requests);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(destination));
    }

    [Fact]
    public async Task ReportProgressAsync_UsesAuthenticatedJobEndpoint()
    {
        var importId = Guid.NewGuid();
        using var handler = new RecordingHandler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(
                $"/v1/internal/package-imports/publisher/jobs/{importId:D}/progress",
                request.RequestUri?.AbsolutePath);
            Assert.Equal(
                "publisher-main",
                request.Headers.GetValues("X-Hechao-Package-Publisher-Agent").Single());
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://launcher-api.example/")
        };
        var client = new PackagePublisherApiClient(
            httpClient,
            "publisher-main",
            new string('A', 48));

        await client.ReportProgressAsync(
            importId,
            new PackagePublisherProgressRequest(
                "publisher-main",
                2,
                PackagePublisherProgressPhase.PublishingObjects,
                25,
                100,
                1024,
                4096),
            CancellationToken.None);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, HttpResponseMessage> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(handler(request));
    }
}

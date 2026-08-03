using System.Net;
using System.Security.Cryptography;
using System.Text;
using Hechao.Contracts;

namespace Hechao.ServerControlAgent.Tests;

public sealed class AgentApiClientTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "hechao-agent-api-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task DownloadPackageArchiveAsync_RestartsAfterBadFullCache()
    {
        var bytes = Encoding.UTF8.GetBytes("verified server archive");
        var digest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var destination = Path.Combine(root, "cache", digest + ".zip");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await File.WriteAllBytesAsync(destination, new byte[bytes.Length]);
        var requests = 0;
        using var handler = new CallbackHandler(request =>
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
            BaseAddress = new Uri("https://launcher-api.example.test/")
        };
        var api = new AgentApiClient(
            httpClient,
            "owl5",
            "test-token",
            _ => TimeSpan.Zero);
        var deployment = new ServerPackageDeploymentRequest(
            Guid.NewGuid(),
            "summer-neoforge-1.21.11",
            "1.0.0",
            bytes.Length,
            digest,
            bytes.Length,
            1,
            PreserveWorldData: false,
            InitialMemoryMiB: 2048,
            MaximumMemoryMiB: 4096);
        var command = new ServerControlCommandDelivery(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "activity",
            ServerControlCommandKind.DeployPackage,
            1,
            null,
            null,
            deployment);

        await api.DownloadPackageArchiveAsync(
            command,
            destination,
            CancellationToken.None);

        Assert.Equal(2, requests);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(destination));
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class CallbackHandler(
        Func<HttpRequestMessage, HttpResponseMessage> callback)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(callback(request));
    }
}

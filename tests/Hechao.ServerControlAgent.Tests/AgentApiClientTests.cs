using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hechao.Contracts;

namespace Hechao.ServerControlAgent.Tests;

public sealed class AgentApiClientTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "hechao-agent-api-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void PackageDeployment_DeserializesLegacyCommandWithoutJavaOverride()
    {
        var importId = Guid.NewGuid();
        var payload = $$"""
            {
              "importId": "{{importId}}",
              "profileId": "legacy-package",
              "version": "1.0.0",
              "archiveBytes": 128,
              "archiveSha256": "{{new string('a', 64)}}",
              "expandedBytes": 256,
              "fileCount": 2,
              "preserveWorldData": false,
              "initialMemoryMiB": 2048,
              "maximumMemoryMiB": 4096
            }
            """;

        var deployment = JsonSerializer.Deserialize<ServerPackageDeploymentRequest>(
            payload,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(deployment);
        Assert.Equal(importId, deployment.ImportId);
        Assert.Null(deployment.JavaMajorVersion);
    }

    [Fact]
    public void PackageDeployment_RoundTripsExplicitJavaVersion()
    {
        var deployment = new ServerPackageDeploymentRequest(
            Guid.NewGuid(),
            "legacy-forge-1.12.2",
            "1.0.0",
            128,
            new string('b', 64),
            256,
            2,
            PreserveWorldData: false,
            InitialMemoryMiB: 2048,
            MaximumMemoryMiB: 4096,
            JavaMajorVersion: 8);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var payload = JsonSerializer.Serialize(deployment, options);
        var restored = JsonSerializer.Deserialize<ServerPackageDeploymentRequest>(
            payload,
            options);

        Assert.NotNull(restored);
        Assert.Equal(8, restored.JavaMajorVersion);
        Assert.Contains("\"javaMajorVersion\":8", payload, StringComparison.Ordinal);
    }

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

using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace Hechao.Distribution.Tests;

internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hechao-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

internal sealed class RangeResponseHandler(byte[] content) : HttpMessageHandler
{
    public List<long?> RequestedOffsets { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var offset = request.Headers.Range?.Ranges.Single().From;
        RequestedOffsets.Add(offset);
        var start = checked((int)(offset ?? 0));
        if (start > content.Length)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable));
        }

        var body = new ByteArrayContent(content[start..]);
        var response = new HttpResponseMessage(
            offset.HasValue ? HttpStatusCode.PartialContent : HttpStatusCode.OK)
        {
            Content = body
        };
        if (offset.HasValue && start < content.Length)
        {
            body.Headers.ContentRange = new ContentRangeHeaderValue(start, content.Length - 1, content.Length);
        }

        return Task.FromResult(response);
    }
}

internal sealed class AuthenticatedRedirectHandler(byte[] content) : HttpMessageHandler
{
    public List<CapturedDownloadRequest> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var offset = request.Headers.Range?.Ranges.Single().From;
        Requests.Add(new CapturedDownloadRequest(
            request.RequestUri!.Host,
            request.Headers.Authorization?.ToString(),
            offset));

        if (request.RequestUri.Host == "launcher-api.hechao.world")
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect)
            {
                Headers = { Location = new Uri("https://download.hechao.world/objects/private") }
            });
        }

        var start = checked((int)(offset ?? 0));
        var body = new ByteArrayContent(content[start..]);
        if (offset.HasValue)
        {
            body.Headers.ContentRange = new ContentRangeHeaderValue(start, content.Length - 1, content.Length);
        }

        return Task.FromResult(new HttpResponseMessage(
            offset.HasValue ? HttpStatusCode.PartialContent : HttpStatusCode.OK)
        {
            Content = body
        });
    }
}

internal sealed record CapturedDownloadRequest(string Host, string? Authorization, long? RangeOffset);

internal static class ManifestTestData
{
    public static ClientManifest CreateManifest(byte[] content, string path = "mods/example.jar")
    {
        var digest = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        return new ClientManifest(
            ManifestValidator.CurrentSchemaVersion,
            "activity-neoforge-1.21.11",
            "1.0.0",
            "1.21.11",
            "21",
            "NeoForge",
            "21.11.42",
            DateTimeOffset.Parse("2026-07-22T00:00:00Z"),
            [new ClientManifestFile(path, content.Length, digest, "https://download.hechao.world/objects/example")],
            []);
    }
}

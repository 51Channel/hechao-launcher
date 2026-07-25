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

internal sealed class ExpiringAuthenticatedRedirectHandler(byte[] content) : HttpMessageHandler
{
    private int _apiRequestCount;

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
            var attempt = Interlocked.Increment(ref _apiRequestCount);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect)
            {
                Headers =
                {
                    Location = new Uri(
                        $"https://download.hechao.world/objects/private?ticket={attempt}")
                }
            });
        }

        if (request.RequestUri.Query == "?ticket=1")
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
        }

        var start = checked((int)(offset ?? 0));
        var body = new ByteArrayContent(content[start..]);
        if (offset.HasValue)
        {
            body.Headers.ContentRange = new ContentRangeHeaderValue(
                start,
                content.Length - 1,
                content.Length);
        }

        return Task.FromResult(new HttpResponseMessage(
            offset.HasValue ? HttpStatusCode.PartialContent : HttpStatusCode.OK)
        {
            Content = body
        });
    }
}

internal sealed class ServiceUnavailableHandler : HttpMessageHandler
{
    public int RequestCount { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        RequestCount++;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    }
}

internal sealed class DelayedObjectResponseHandler(
    IReadOnlyDictionary<string, byte[]> objects,
    TimeSpan delay) : HttpMessageHandler
{
    private int _activeRequests;
    private int _maximumConcurrentRequests;
    private int _requestCount;

    public int MaximumConcurrentRequests => Volatile.Read(ref _maximumConcurrentRequests);
    public int RequestCount => Volatile.Read(ref _requestCount);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _requestCount);
        var activeRequests = Interlocked.Increment(ref _activeRequests);
        UpdateMaximum(activeRequests);
        try
        {
            await Task.Delay(delay, cancellationToken);
            if (request.RequestUri is null ||
                !objects.TryGetValue(request.RequestUri.AbsoluteUri, out var content))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content)
            };
        }
        finally
        {
            Interlocked.Decrement(ref _activeRequests);
        }
    }

    private void UpdateMaximum(int candidate)
    {
        while (true)
        {
            var current = Volatile.Read(ref _maximumConcurrentRequests);
            if (candidate <= current ||
                Interlocked.CompareExchange(
                    ref _maximumConcurrentRequests,
                    candidate,
                    current) == current)
            {
                return;
            }
        }
    }
}

internal sealed class SynchronousProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
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

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hechao.Contracts;

internal sealed class PackagePublisherApiClient(
    HttpClient httpClient,
    string agentId,
    string token,
    Func<int, TimeSpan>? packageRetryDelay = null)
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    internal async Task SendHeartbeatAsync(
        string agentVersion,
        Guid? activeImportId,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(
            HttpMethod.Post,
            "v1/internal/package-imports/publisher/heartbeat");
        request.Content = JsonContent.Create(
            new PackagePublisherHeartbeatRequest(
                agentId,
                agentVersion,
                DateTimeOffset.UtcNow,
                activeImportId),
            options: JsonOptions);
        using var requestCancellation = CreateRequestCancellation(cancellationToken);
        using var response = await httpClient.SendAsync(
            request,
            requestCancellation.Token);
        await EnsureSuccessAsync(response, requestCancellation.Token);
    }

    internal async Task<PackagePublisherClaimResponse> ClaimAsync(
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(
            HttpMethod.Post,
            "v1/internal/package-imports/publisher/jobs/claim");
        request.Content = JsonContent.Create(
            new PackagePublisherClaimRequest(agentId),
            options: JsonOptions);
        using var requestCancellation = CreateRequestCancellation(cancellationToken);
        using var response = await httpClient.SendAsync(
            request,
            requestCancellation.Token);
        await EnsureSuccessAsync(response, requestCancellation.Token);
        return await response.Content.ReadFromJsonAsync<PackagePublisherClaimResponse>(
                   JsonOptions,
                   requestCancellation.Token)
               ?? throw new InvalidDataException(
                   "The package publisher claim response is empty.");
    }

    internal async Task DownloadClientArchiveAsync(
        PackagePublisherJobDelivery job,
        string destinationPath,
        CancellationToken cancellationToken)
        => await DownloadClientArchiveAsync(
            job,
            destinationPath,
            progress: null,
            cancellationToken);

    internal async Task DownloadClientArchiveAsync(
        PackagePublisherJobDelivery job,
        string destinationPath,
        Func<long, long, CancellationToken, Task>? progress,
        CancellationToken cancellationToken)
    {
        Exception? lastFailure = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                await DownloadClientArchiveAttemptAsync(
                    job,
                    destinationPath,
                    progress,
                    cancellationToken);
                return;
            }
            catch (OperationCanceledException) when (
                !cancellationToken.IsCancellationRequested)
            {
                lastFailure = new TimeoutException(
                    "The client archive download timed out.");
            }
            catch (Exception exception) when (
                exception is HttpRequestException or InvalidDataException)
            {
                lastFailure = exception;
            }

            if (attempt < 3)
            {
                await Task.Delay(
                    packageRetryDelay?.Invoke(attempt) ??
                    TimeSpan.FromSeconds(attempt * 2),
                    cancellationToken);
            }
        }

        throw lastFailure ?? new InvalidOperationException(
            "The client archive download failed without an error.");
    }

    private async Task DownloadClientArchiveAttemptAsync(
        PackagePublisherJobDelivery job,
        string destinationPath,
        Func<long, long, CancellationToken, Task>? progress,
        CancellationToken cancellationToken)
    {
        var path = Path.GetFullPath(destinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path) &&
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                "The client archive cache file cannot be a reparse point.");
        }

        var existingBytes = File.Exists(path) ? new FileInfo(path).Length : 0;
        if (existingBytes > job.ClientArchiveBytes)
        {
            File.Delete(path);
            existingBytes = 0;
        }

        using var request = CreateRequest(
            HttpMethod.Get,
            $"v1/internal/package-imports/publisher/jobs/{job.ImportId:D}/client-archive");
        if (existingBytes > 0)
        {
            request.Headers.Range = new RangeHeaderValue(existingBytes, null);
        }

        using var requestCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        requestCancellation.CancelAfter(TimeSpan.FromHours(2));
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            requestCancellation.Token);
        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable &&
            existingBytes == job.ClientArchiveBytes)
        {
            if (progress is not null)
            {
                await progress(existingBytes, job.ClientArchiveBytes, requestCancellation.Token);
            }
            await ValidateDownloadedArchiveAsync(
                path,
                job,
                requestCancellation.Token);
            return;
        }

        await EnsureSuccessAsync(response, requestCancellation.Token);
        var append = existingBytes > 0 &&
                     response.StatusCode == HttpStatusCode.PartialContent;
        var mode = append ? FileMode.Append : FileMode.Create;
        await using var input = await response.Content.ReadAsStreamAsync(
            requestCancellation.Token);
        await using (var output = new FileStream(
                         path,
                         mode,
                         FileAccess.Write,
                         FileShare.None,
                         256 * 1024,
                         FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            var buffer = new byte[256 * 1024];
            long total = append ? existingBytes : 0;
            if (progress is not null)
            {
                await progress(total, job.ClientArchiveBytes, requestCancellation.Token);
            }
            while (true)
            {
                var read = await input.ReadAsync(
                    buffer,
                    requestCancellation.Token);
                if (read == 0)
                {
                    break;
                }

                total = checked(total + read);
                if (total > job.ClientArchiveBytes)
                {
                    throw new InvalidDataException(
                        "The client archive exceeded its declared size.");
                }

                await output.WriteAsync(
                    buffer.AsMemory(0, read),
                    requestCancellation.Token);
                if (progress is not null)
                {
                    await progress(total, job.ClientArchiveBytes, requestCancellation.Token);
                }
            }

            await output.FlushAsync(requestCancellation.Token);
        }

        await ValidateDownloadedArchiveAsync(
            path,
            job,
            requestCancellation.Token);
    }

    internal async Task CompleteAsync(
        Guid importId,
        PackagePublisherCompletionRequest completion,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(
            HttpMethod.Post,
            $"v1/internal/package-imports/publisher/jobs/{importId:D}/complete");
        request.Content = JsonContent.Create(completion, options: JsonOptions);
        using var requestCancellation = CreateRequestCancellation(cancellationToken);
        using var response = await httpClient.SendAsync(
            request,
            requestCancellation.Token);
        await EnsureSuccessAsync(response, requestCancellation.Token);
    }

    internal async Task ReportProgressAsync(
        Guid importId,
        PackagePublisherProgressRequest progress,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(
            HttpMethod.Post,
            $"v1/internal/package-imports/publisher/jobs/{importId:D}/progress");
        request.Content = JsonContent.Create(progress, options: JsonOptions);
        using var requestCancellation = CreateRequestCancellation(cancellationToken);
        using var response = await httpClient.SendAsync(
            request,
            requestCancellation.Token);
        await EnsureSuccessAsync(response, requestCancellation.Token);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Hechao-Package-Publisher-Token", token);
        request.Headers.Add("X-Hechao-Package-Publisher-Agent", agentId);
        return request;
    }

    private static async Task ValidateDownloadedArchiveAsync(
        string path,
        PackagePublisherJobDelivery job,
        CancellationToken cancellationToken)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length != job.ClientArchiveBytes)
        {
            throw new InvalidDataException(
                "The client archive download is incomplete.");
        }

        string digest;
        await using (var stream = new FileStream(
                         path,
                         FileMode.Open,
                         FileAccess.Read,
                         FileShare.Read,
                         256 * 1024,
                         FileOptions.Asynchronous |
                         FileOptions.SequentialScan))
        {
            digest = Convert.ToHexString(
                    await SHA256.HashDataAsync(stream, cancellationToken))
                .ToLowerInvariant();
        }

        if (!CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.ASCII.GetBytes(digest),
                System.Text.Encoding.ASCII.GetBytes(job.ClientArchiveSha256)))
        {
            File.Delete(path);
            throw new InvalidDataException(
                "The client archive SHA-256 does not match the immutable job.");
        }
    }

    private static CancellationTokenSource CreateRequestCancellation(
        CancellationToken cancellationToken)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        source.CancelAfter(TimeSpan.FromSeconds(30));
        return source;
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (body.Length > 500)
        {
            body = body[..500];
        }

        throw new HttpRequestException(
            $"Package publisher API returned {(int)response.StatusCode}: {body}",
            inner: null,
            response.StatusCode);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}

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
    string token)
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    internal async Task SendHeartbeatAsync(
        string agentVersion,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(
            HttpMethod.Post,
            "v1/internal/package-imports/publisher/heartbeat");
        request.Content = JsonContent.Create(
            new PackagePublisherHeartbeatRequest(
                agentId,
                agentVersion,
                DateTimeOffset.UtcNow),
            options: JsonOptions);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
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
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<PackagePublisherClaimResponse>(
                   JsonOptions,
                   cancellationToken)
               ?? throw new InvalidDataException(
                   "The package publisher claim response is empty.");
    }

    internal async Task DownloadClientArchiveAsync(
        PackagePublisherJobDelivery job,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        var path = Path.GetFullPath(destinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
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

        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable &&
            existingBytes == job.ClientArchiveBytes)
        {
            await ValidateDownloadedArchiveAsync(path, job, cancellationToken);
            return;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var append = existingBytes > 0 &&
                     response.StatusCode == HttpStatusCode.PartialContent;
        var mode = append ? FileMode.Append : FileMode.Create;
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
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
            while (true)
            {
                var read = await input.ReadAsync(buffer, cancellationToken);
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

                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            await output.FlushAsync(cancellationToken);
        }

        await ValidateDownloadedArchiveAsync(path, job, cancellationToken);
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
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
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

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            256 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var digest = Convert.ToHexString(
            await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.ASCII.GetBytes(digest),
                System.Text.Encoding.ASCII.GetBytes(job.ClientArchiveSha256)))
        {
            File.Delete(path);
            throw new InvalidDataException(
                "The client archive SHA-256 does not match the immutable job.");
        }
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

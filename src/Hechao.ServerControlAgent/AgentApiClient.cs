using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hechao.Contracts;

namespace Hechao.ServerControlAgent;

internal sealed class AgentApiClient(
    HttpClient httpClient,
    string agentId,
    string token,
    Func<int, TimeSpan>? packageRetryDelay = null)
{
    private static readonly JsonSerializerOptions JsonOptions =
        CreateJsonOptions();

    internal async Task SendHeartbeatAsync(
        ServerControlAgentHeartbeatRequest request,
        CancellationToken cancellationToken)
    {
        using var message = CreateRequest(
            HttpMethod.Post,
            "v1/internal/server-control/heartbeat");
        message.Content = JsonContent.Create(request, options: JsonOptions);
        using var requestCancellation = CreateRequestCancellation(cancellationToken);
        using var response = await httpClient.SendAsync(
            message,
            requestCancellation.Token);
        await EnsureSuccessAsync(response, requestCancellation.Token);
    }

    internal async Task<ServerControlCommandClaimResponse> ClaimAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        using var message = CreateRequest(
            HttpMethod.Post,
            "v1/internal/server-control/commands/claim");
        message.Content = JsonContent.Create(
            new ServerControlCommandClaimRequest(agentId, limit),
            options: JsonOptions);
        using var requestCancellation = CreateRequestCancellation(cancellationToken);
        using var response = await httpClient.SendAsync(
            message,
            requestCancellation.Token);
        await EnsureSuccessAsync(response, requestCancellation.Token);
        return await response.Content.ReadFromJsonAsync<
                   ServerControlCommandClaimResponse>(
                   JsonOptions,
                   requestCancellation.Token)
               ?? throw new InvalidDataException(
                   "The server control claim response is empty.");
    }

    internal async Task CompleteAsync(
        Guid commandId,
        ServerControlCommandCompletionRequest request,
        CancellationToken cancellationToken)
    {
        using var message = CreateRequest(
            HttpMethod.Post,
            $"v1/internal/server-control/commands/{commandId:D}/complete");
        message.Content = JsonContent.Create(request, options: JsonOptions);
        using var requestCancellation = CreateRequestCancellation(cancellationToken);
        using var response = await httpClient.SendAsync(
            message,
            requestCancellation.Token);
        await EnsureSuccessAsync(response, requestCancellation.Token);
    }

    internal async Task DownloadPackageArchiveAsync(
        ServerControlCommandDelivery command,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        Exception? lastFailure = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                await DownloadPackageArchiveAttemptAsync(
                    command,
                    destinationPath,
                    cancellationToken);
                return;
            }
            catch (OperationCanceledException) when (
                !cancellationToken.IsCancellationRequested)
            {
                lastFailure = new TimeoutException(
                    "The server package download timed out.");
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
            "The server package download failed without an error.");
    }

    private async Task DownloadPackageArchiveAttemptAsync(
        ServerControlCommandDelivery command,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        var deployment = command.PackageDeployment
            ?? throw new InvalidDataException(
                "The package deployment command has no archive metadata.");
        var path = Path.GetFullPath(destinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path) &&
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                "The package cache file cannot be a reparse point.");
        }

        var existingBytes = File.Exists(path) ? new FileInfo(path).Length : 0;
        if (existingBytes > deployment.ArchiveBytes)
        {
            File.Delete(path);
            existingBytes = 0;
        }

        using var request = CreateRequest(
            HttpMethod.Get,
            $"v1/internal/server-control/commands/{command.CommandId:D}/package-archive");
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
            existingBytes == deployment.ArchiveBytes)
        {
            await ValidatePackageArchiveAsync(
                path,
                deployment,
                requestCancellation.Token);
            return;
        }

        await EnsureSuccessAsync(response, requestCancellation.Token);
        var append = existingBytes > 0 &&
                     response.StatusCode == HttpStatusCode.PartialContent;
        await using var input = await response.Content.ReadAsStreamAsync(
            requestCancellation.Token);
        await using (var output = new FileStream(
                         path,
                         append ? FileMode.Append : FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         256 * 1024,
                         FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            var buffer = new byte[256 * 1024];
            long total = append ? existingBytes : 0;
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
                if (total > deployment.ArchiveBytes)
                {
                    throw new InvalidDataException(
                        "The server package exceeded its declared size.");
                }

                await output.WriteAsync(
                    buffer.AsMemory(0, read),
                    requestCancellation.Token);
            }

            await output.FlushAsync(requestCancellation.Token);
        }

        await ValidatePackageArchiveAsync(
            path,
            deployment,
            requestCancellation.Token);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Hechao-Server-Control-Token", token);
        request.Headers.Add("X-Hechao-Server-Control-Agent", agentId);
        return request;
    }

    private static async Task ValidatePackageArchiveAsync(
        string path,
        ServerPackageDeploymentRequest deployment,
        CancellationToken cancellationToken)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length != deployment.ArchiveBytes ||
            (file.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                "The server package download is incomplete.");
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
                System.Text.Encoding.ASCII.GetBytes(
                    deployment.ArchiveSha256)))
        {
            File.Delete(path);
            throw new InvalidDataException(
                "The server package SHA-256 does not match the command.");
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
        throw new HttpRequestException(
            $"Server control API returned {(int)response.StatusCode}: " +
            AgentLog.Sanitize(body, 500),
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

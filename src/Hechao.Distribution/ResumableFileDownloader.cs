using System.Net;
using System.Net.Http.Headers;
using System.Xml;
using System.Xml.Linq;

namespace Hechao.Distribution;

public sealed record FileDownloadProgress(long BytesDownloaded, long TotalBytes);

public sealed class ResumableFileDownloader
{
    private const int DefaultMaximumAttempts = 5;
    private const int MaximumRedirects = 5;
    private const int BufferSize = 128 * 1024;
    private const int MaximumErrorBodyBytes = 16 * 1024;
    private const string RetryAfterDataKey = "Hechao.RetryAfter";
    private readonly HttpClient _httpClient;
    private readonly int _maximumAttempts;
    private readonly Func<TimeSpan, CancellationToken, Task> _retryDelay;

    public ResumableFileDownloader(
        HttpClient httpClient,
        int maximumAttempts = DefaultMaximumAttempts,
        Func<TimeSpan, CancellationToken, Task>? retryDelay = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        if (maximumAttempts is < 1 or > 10)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumAttempts),
                maximumAttempts,
                "Download attempts must be between 1 and 10.");
        }

        _httpClient = httpClient;
        _maximumAttempts = maximumAttempts;
        _retryDelay = retryDelay ?? Task.Delay;
    }

    public async Task DownloadAsync(
        ClientManifestFile manifestFile,
        string destinationPath,
        IProgress<FileDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifestFile);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        if (await FileHashing.MatchesAsync(
                destinationPath,
                manifestFile.Size,
                manifestFile.Sha256,
                cancellationToken))
        {
            progress?.Report(new FileDownloadProgress(manifestFile.Size, manifestFile.Size));
            return;
        }

        TryDelete(destinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destinationPath))!);
        var partialPath = destinationPath + ".part";

        Exception? lastFailure = null;
        for (var attempt = 1; attempt <= _maximumAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await DownloadAttemptAsync(manifestFile, partialPath, progress, cancellationToken);
                if (!await FileHashing.MatchesAsync(
                        partialPath,
                        manifestFile.Size,
                        manifestFile.Sha256,
                        cancellationToken))
                {
                    TryDelete(partialPath);
                    throw new ManifestIntegrityException($"SHA-256 verification failed for {manifestFile.Path}.");
                }

                File.Move(partialPath, destinationPath, true);
                return;
            }
            catch (Exception exception) when (
                attempt < _maximumAttempts &&
                !cancellationToken.IsCancellationRequested &&
                exception is HttpRequestException or OperationCanceledException or IOException or ManifestIntegrityException)
            {
                lastFailure = exception;
                await _retryDelay(
                    CalculateRetryDelay(attempt, exception),
                    cancellationToken);
            }
        }

        throw lastFailure ?? new IOException($"The download failed for {manifestFile.Path}.");
    }

    private async Task DownloadAttemptAsync(
        ClientManifestFile manifestFile,
        string partialPath,
        IProgress<FileDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var existingBytes = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0;
        if (existingBytes > manifestFile.Size)
        {
            TryDelete(partialPath);
            existingBytes = 0;
        }

        using var response = await SendFollowingRedirectsAsync(
            new Uri(manifestFile.Url, UriKind.Absolute),
            existingBytes,
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            if (existingBytes == manifestFile.Size)
            {
                progress?.Report(new FileDownloadProgress(existingBytes, manifestFile.Size));
                return;
            }

            TryDelete(partialPath);
            throw new HttpRequestException(
                "The download server rejected the resume position.",
                null,
                response.StatusCode);
        }

        if (!response.IsSuccessStatusCode)
        {
            var remoteErrorCode = await TryReadRemoteErrorCodeAsync(
                response,
                cancellationToken);
            var errorCodeSuffix = remoteErrorCode is null
                ? string.Empty
                : $", code {remoteErrorCode}";
            var exception = new HttpRequestException(
                $"The download server returned {(int)response.StatusCode} " +
                $"({response.ReasonPhrase}){errorCodeSuffix}.",
                null,
                response.StatusCode);
            if (GetRetryAfter(response.Headers.RetryAfter) is { } retryAfter)
            {
                exception.Data[RetryAfterDataKey] = retryAfter;
            }

            throw exception;
        }

        var append = existingBytes > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        if (append)
        {
            var contentRange = response.Content.Headers.ContentRange;
            if (contentRange?.From != existingBytes)
            {
                throw new HttpRequestException("The download server returned an invalid Content-Range header.");
            }
        }
        else
        {
            existingBytes = 0;
        }

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = new FileStream(
            partialPath,
            append ? FileMode.Append : FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var buffer = new byte[BufferSize];
        var downloadedBytes = existingBytes;
        progress?.Report(new FileDownloadProgress(downloadedBytes, manifestFile.Size));
        while (true)
        {
            var bytesRead = await source.ReadAsync(buffer, cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }

            downloadedBytes = checked(downloadedBytes + bytesRead);
            if (downloadedBytes > manifestFile.Size)
            {
                throw new ManifestIntegrityException($"The server returned too much data for {manifestFile.Path}.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            progress?.Report(new FileDownloadProgress(downloadedBytes, manifestFile.Size));
        }

        await destination.FlushAsync(cancellationToken);
        if (downloadedBytes != manifestFile.Size)
        {
            throw new HttpRequestException(
                $"The download ended early for {manifestFile.Path}: {downloadedBytes}/{manifestFile.Size} bytes.");
        }
    }

    private static async Task<string?> TryReadRemoteErrorCodeAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (TryGetSafeHeaderValue(response, "x-oss-ec") is { } headerCode)
        {
            return headerCode;
        }

        if (response.Content.Headers.ContentLength is > MaximumErrorBodyBytes)
        {
            return null;
        }

        try
        {
            await using var source = await response.Content.ReadAsStreamAsync(
                cancellationToken);
            using var buffer = new MemoryStream();
            var chunk = new byte[4096];
            while (buffer.Length <= MaximumErrorBodyBytes)
            {
                var remaining = MaximumErrorBodyBytes + 1 - (int)buffer.Length;
                var bytesRead = await source.ReadAsync(
                    chunk.AsMemory(0, Math.Min(chunk.Length, remaining)),
                    cancellationToken);
                if (bytesRead == 0)
                {
                    break;
                }

                buffer.Write(chunk, 0, bytesRead);
            }

            if (buffer.Length == 0 || buffer.Length > MaximumErrorBodyBytes)
            {
                return null;
            }

            buffer.Position = 0;
            using var reader = XmlReader.Create(
                buffer,
                new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    IgnoreComments = true,
                    IgnoreWhitespace = true,
                    MaxCharactersInDocument = MaximumErrorBodyBytes
                });
            var document = XDocument.Load(reader, LoadOptions.None);
            var bodyCode = document
                .Descendants()
                .FirstOrDefault(element =>
                    string.Equals(
                        element.Name.LocalName,
                        "Code",
                        StringComparison.Ordinal))
                ?.Value;
            return IsSafeErrorCode(bodyCode) ? bodyCode : null;
        }
        catch (Exception exception) when (
            exception is IOException or XmlException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string? TryGetSafeHeaderValue(
        HttpResponseMessage response,
        string name)
    {
        if (!response.Headers.TryGetValues(name, out var values))
        {
            return null;
        }

        var value = values.FirstOrDefault();
        return IsSafeErrorCode(value) ? value : null;
    }

    private static bool IsSafeErrorCode(string? value) =>
        value is { Length: > 0 and <= 64 } &&
        value.All(character =>
            char.IsAsciiLetterOrDigit(character) ||
            character is '-' or '_' or '.');

    private async Task<HttpResponseMessage> SendFollowingRedirectsAsync(
        Uri initialUri,
        long existingBytes,
        CancellationToken cancellationToken)
    {
        var currentUri = initialUri;
        for (var redirectCount = 0; redirectCount <= MaximumRedirects; redirectCount++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, currentUri);
            if (existingBytes > 0)
            {
                request.Headers.Range = new RangeHeaderValue(existingBytes, null);
            }

            var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (!IsRedirect(response.StatusCode))
            {
                return response;
            }

            var location = response.Headers.Location;
            response.Dispose();
            if (location is null || redirectCount == MaximumRedirects)
            {
                throw new HttpRequestException("The download server returned an invalid redirect.");
            }

            currentUri = location.IsAbsoluteUri ? location : new Uri(currentUri, location);
            if ((currentUri.Scheme != Uri.UriSchemeHttps &&
                 !(currentUri.Scheme == Uri.UriSchemeHttp && currentUri.IsLoopback)) ||
                !string.IsNullOrEmpty(currentUri.UserInfo) ||
                !string.IsNullOrEmpty(currentUri.Fragment))
            {
                throw new HttpRequestException("The download server redirected to an unsafe URL.");
            }
        }

        throw new HttpRequestException("The download server returned too many redirects.");
    }

    private static bool IsRedirect(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.MovedPermanently or
            HttpStatusCode.Redirect or
            HttpStatusCode.RedirectMethod or
            HttpStatusCode.TemporaryRedirect or
            HttpStatusCode.PermanentRedirect;

    private static TimeSpan CalculateRetryDelay(
        int failedAttempt,
        Exception exception)
    {
        var serverDelay = exception.Data[RetryAfterDataKey] as TimeSpan?;
        var baseDelayMilliseconds = serverDelay.HasValue
            ? Math.Clamp(serverDelay.Value.TotalMilliseconds, 250, 30_000)
            : Math.Min(4000, 250 * (1 << Math.Min(failedAttempt - 1, 4)));
        var jitterMilliseconds = Random.Shared.Next(0, 151);
        return TimeSpan.FromMilliseconds(baseDelayMilliseconds + jitterMilliseconds);
    }

    private static TimeSpan? GetRetryAfter(RetryConditionHeaderValue? retryAfter)
    {
        if (retryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
        {
            return delta;
        }

        if (retryAfter?.Date is { } retryDate)
        {
            var delay = retryDate - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
            {
                return delay;
            }
        }

        return null;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

}

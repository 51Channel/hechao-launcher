using System.Net;
using System.Net.Http.Headers;

namespace Hechao.Distribution;

public sealed record FileDownloadProgress(long BytesDownloaded, long TotalBytes);

public sealed class ResumableFileDownloader(HttpClient httpClient)
{
    private const int MaximumAttempts = 3;
    private const int MaximumRedirects = 5;
    private const int BufferSize = 128 * 1024;

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
        for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
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
                attempt < MaximumAttempts &&
                !cancellationToken.IsCancellationRequested &&
                exception is HttpRequestException or TaskCanceledException or IOException or ManifestIntegrityException)
            {
                lastFailure = exception;
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt * attempt), cancellationToken);
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

        response.EnsureSuccessStatusCode();

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

            var response = await httpClient.SendAsync(
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

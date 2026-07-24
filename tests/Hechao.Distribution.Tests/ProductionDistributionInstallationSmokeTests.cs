using System.Net;
using System.Net.Http.Headers;

namespace Hechao.Distribution.Tests;

public sealed class ProductionDistributionInstallationSmokeTests
{
    [Fact]
    [Trait("Category", "Smoke")]
    public async Task InstallAsync_InstallsAndRevalidatesProductionDistribution()
    {
        var manifestPath = Environment.GetEnvironmentVariable("HECHAO_SMOKE_MANIFEST_PATH");
        var trustBundlePath = Environment.GetEnvironmentVariable("HECHAO_SMOKE_TRUST_BUNDLE_PATH");
        var objectRoot = Environment.GetEnvironmentVariable("HECHAO_SMOKE_OBJECT_ROOT");
        var dataRoot = Environment.GetEnvironmentVariable("HECHAO_SMOKE_INSTALL_ROOT");
        if (string.IsNullOrWhiteSpace(manifestPath) ||
            string.IsNullOrWhiteSpace(trustBundlePath) ||
            string.IsNullOrWhiteSpace(objectRoot) ||
            string.IsNullOrWhiteSpace(dataRoot))
        {
            return;
        }

        var envelopeBytes = await File.ReadAllBytesAsync(manifestPath);
        var trustBundle = ManifestJson.DeserializeTrustBundle(
            await File.ReadAllBytesAsync(trustBundlePath));
        var verified = SignedManifestCodec.Verify(envelopeBytes, trustBundle);

        using var httpClient = new HttpClient(new DistributionObjectHandler(objectRoot))
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        var installer = new ClientProfileInstaller(new ResumableFileDownloader(httpClient));
        await installer.InstallAsync(
            verified,
            new ClientInstallationOptions(dataRoot, KeepObjectCache: false));

        Assert.Equal(
            LocalProfileState.Ready,
            await installer.GetLocalStateAsync(
                dataRoot,
                verified.Manifest.ProfileId,
                verified.Manifest.Version));

        var activeDirectory = new ClientStorageLayout(dataRoot)
            .GetProfileGameDirectory(verified.Manifest.ProfileId);
        foreach (var file in verified.Manifest.Files)
        {
            var installedPath = ManifestValidator.ResolveManagedPath(activeDirectory, file.Path);
            Assert.True(
                await FileHashing.MatchesAsync(installedPath, file.Size, file.Sha256),
                $"Installed file failed validation: {file.Path}");
        }
    }

    private sealed class DistributionObjectHandler(string objectRoot) : HttpMessageHandler
    {
        private readonly string _objectRoot = Path.GetFullPath(objectRoot);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Method != HttpMethod.Get || request.RequestUri is null)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.MethodNotAllowed));
            }

            var segments = request.RequestUri.AbsolutePath.Split(
                '/',
                StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 2)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            var prefix = segments[^2];
            var digest = segments[^1];
            if (prefix.Length != 2 ||
                digest.Length != 64 ||
                !digest.All(Uri.IsHexDigit) ||
                !string.Equals(prefix, digest[..2], StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            var objectPath = ManifestValidator.ResolveManagedPath(
                _objectRoot,
                $"{prefix}/{digest}");
            var objectFile = new FileInfo(objectPath);
            if (!objectFile.Exists)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            var start = ReadRangeStart(request.Headers.Range);
            if (start >= objectFile.Length)
            {
                var invalidRange = new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable);
                invalidRange.Content = new ByteArrayContent([]);
                invalidRange.Content.Headers.ContentRange =
                    new ContentRangeHeaderValue(objectFile.Length);
                return Task.FromResult(invalidRange);
            }

            var stream = new FileStream(
                objectPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            stream.Position = start;
            var response = new HttpResponseMessage(
                start == 0 ? HttpStatusCode.OK : HttpStatusCode.PartialContent)
            {
                Content = new StreamContent(stream)
            };
            response.Content.Headers.ContentLength = objectFile.Length - start;
            if (start > 0)
            {
                response.Content.Headers.ContentRange =
                    new ContentRangeHeaderValue(start, objectFile.Length - 1, objectFile.Length);
            }

            return Task.FromResult(response);
        }

        private static long ReadRangeStart(RangeHeaderValue? range)
        {
            if (range is null)
            {
                return 0;
            }

            var ranges = range.Ranges.ToArray();
            if (ranges.Length != 1 ||
                ranges[0].From is null ||
                ranges[0].To is not null)
            {
                throw new HttpRequestException("The smoke server only supports open-ended ranges.");
            }

            return ranges[0].From.GetValueOrDefault();
        }
    }
}

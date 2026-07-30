using Hechao.Api.Distribution;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Hechao.Api.Tests;

public sealed class OssPresignedUrlFactoryTests
{
    [Fact]
    public void TryCreateGetUrl_UsesPrivateCnameObjectPathAndV4Signature()
    {
        var previousAccessKeyId = Environment.GetEnvironmentVariable("OSS_ACCESS_KEY_ID");
        var previousAccessKeySecret = Environment.GetEnvironmentVariable("OSS_ACCESS_KEY_SECRET");
        try
        {
            Environment.SetEnvironmentVariable("OSS_ACCESS_KEY_ID", "test-access-key-id");
            Environment.SetEnvironmentVariable("OSS_ACCESS_KEY_SECRET", "test-access-key-secret");
            var options = Options.Create(new DistributionOptions
            {
                OssRegion = "cn-shanghai",
                OssBucket = "hechaoworld",
                OssEndpoint = "https://download.hechao.world",
                OssObjectPrefix = "objects",
                PresignedUrlSeconds = 300
            });
            using var factory = new OssPresignedUrlFactory(
                options,
                NullLogger<OssPresignedUrlFactory>.Instance);
            var digest = "ab" + new string('0', 62);

            var result = factory.TryCreateGetUrl(digest);

            Assert.NotNull(result);
            var uri = new Uri(result);
            Assert.Equal(Uri.UriSchemeHttps, uri.Scheme);
            Assert.Equal("download.hechao.world", uri.Host);
            Assert.Equal($"/objects/ab/{digest}", uri.AbsolutePath);
            Assert.Contains("x-oss-signature-version=OSS4-HMAC-SHA256", uri.Query, StringComparison.OrdinalIgnoreCase);
            var expires = uri.Query.TrimStart('?')
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Single(part => part.StartsWith("x-oss-expires=", StringComparison.OrdinalIgnoreCase))
                .Split('=', 2)[1];
            Assert.InRange(int.Parse(expires, System.Globalization.CultureInfo.InvariantCulture), 299, 300);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OSS_ACCESS_KEY_ID", previousAccessKeyId);
            Environment.SetEnvironmentVariable("OSS_ACCESS_KEY_SECRET", previousAccessKeySecret);
        }
    }

    [Fact]
    public void TryCreateLauncherInstallerUrl_UsesExactPrivateReleasePath()
    {
        var previousAccessKeyId = Environment.GetEnvironmentVariable("OSS_ACCESS_KEY_ID");
        var previousAccessKeySecret = Environment.GetEnvironmentVariable("OSS_ACCESS_KEY_SECRET");
        try
        {
            Environment.SetEnvironmentVariable("OSS_ACCESS_KEY_ID", "test-access-key-id");
            Environment.SetEnvironmentVariable("OSS_ACCESS_KEY_SECRET", "test-access-key-secret");
            var options = Options.Create(new DistributionOptions
            {
                OssRegion = "cn-shanghai",
                OssBucket = "hechaoworld",
                OssEndpoint = "https://download.hechao.world",
                OssObjectPrefix = "objects",
                PresignedUrlSeconds = 300
            });
            using var factory = new OssPresignedUrlFactory(
                options,
                NullLogger<OssPresignedUrlFactory>.Instance);

            var result = factory.TryCreateLauncherInstallerUrl("0.13.0");

            Assert.NotNull(result);
            var uri = new Uri(result);
            Assert.Equal(
                "/releases/launcher/0.13.0/Hechao-Launcher-Setup-0.13.0-win-x64.exe",
                uri.AbsolutePath);
            Assert.Contains(
                "x-oss-signature-version=OSS4-HMAC-SHA256",
                uri.Query,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OSS_ACCESS_KEY_ID", previousAccessKeyId);
            Environment.SetEnvironmentVariable("OSS_ACCESS_KEY_SECRET", previousAccessKeySecret);
        }
    }
}

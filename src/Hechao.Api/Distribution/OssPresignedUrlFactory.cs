using Microsoft.Extensions.Options;
using OSS = AlibabaCloud.OSS.V2;

namespace Hechao.Api.Distribution;

public sealed class OssPresignedUrlFactory : IDisposable
{
    private readonly DistributionOptions _options;
    private readonly ILogger<OssPresignedUrlFactory> _logger;
    private readonly OSS.Client? _client;
    private readonly string _objectPrefix;

    public OssPresignedUrlFactory(
        IOptions<DistributionOptions> options,
        ILogger<OssPresignedUrlFactory> logger)
    {
        _options = options.Value;
        _logger = logger;
        _objectPrefix = _options.OssObjectPrefix.Trim('/');
        if (!_options.HasCompleteOssConfiguration)
        {
            return;
        }

        var configuration = OSS.Configuration.LoadDefault();
        configuration.CredentialsProvider = new OSS.Credentials.EnvironmentVariableCredentialsProvider();
        configuration.Region = _options.OssRegion;
        configuration.Endpoint = _options.OssEndpoint;
        configuration.UseCName = true;
        _client = new OSS.Client(configuration);
    }

    public string? TryCreateGetUrl(string objectSha256)
    {
        var objectKey = string.IsNullOrEmpty(_objectPrefix)
            ? $"{objectSha256[..2]}/{objectSha256}"
            : $"{_objectPrefix}/{objectSha256[..2]}/{objectSha256}";
        return TryCreateGetUrlForKey(objectKey);
    }

    public string? TryCreateLauncherInstallerUrl(string version)
    {
        if (!LauncherUpdateOptions.TryParseVersion(version, out _))
        {
            return null;
        }

        var fileName = $"Hechao-Launcher-Setup-{version}-win-x64.exe";
        return TryCreateGetUrlForKey($"releases/launcher/{version}/{fileName}");
    }

    private string? TryCreateGetUrlForKey(string objectKey)
    {
        if (_client is null ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OSS_ACCESS_KEY_ID")) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OSS_ACCESS_KEY_SECRET")))
        {
            return null;
        }

        try
        {
            var result = _client.Presign(
                new OSS.Models.GetObjectRequest
                {
                    Bucket = _options.OssBucket,
                    Key = objectKey
                },
                DateTime.UtcNow.AddSeconds(_options.PresignedUrlSeconds));
            return result.Url;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Unable to create a presigned OSS download URL.");
            return null;
        }
    }

    public void Dispose()
    {
        _client?.Dispose();
    }
}

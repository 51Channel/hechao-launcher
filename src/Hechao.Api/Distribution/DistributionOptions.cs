namespace Hechao.Api.Distribution;

public sealed class DistributionOptions
{
    public const string SectionName = "Distribution";

    public string ManifestDirectory { get; init; } = string.Empty;
    public int MaximumManifestBytes { get; init; } = 8 * 1024 * 1024;
    public string OssRegion { get; init; } = string.Empty;
    public string OssBucket { get; init; } = string.Empty;
    public string OssEndpoint { get; init; } = string.Empty;
    public string OssObjectPrefix { get; init; } = "objects";
    public int PresignedUrlSeconds { get; init; } = 300;

    public bool HasAnyOssConfiguration =>
        !string.IsNullOrWhiteSpace(OssRegion) ||
        !string.IsNullOrWhiteSpace(OssBucket) ||
        !string.IsNullOrWhiteSpace(OssEndpoint);

    public bool HasCompleteOssConfiguration =>
        !string.IsNullOrWhiteSpace(OssRegion) &&
        !string.IsNullOrWhiteSpace(OssBucket) &&
        !string.IsNullOrWhiteSpace(OssEndpoint);
}

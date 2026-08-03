using System.Text.RegularExpressions;

namespace Hechao.Api.PackageImports;

public sealed partial class PackageImportOptions
{
    public const string SectionName = "PackageImports";

    public bool Enabled { get; init; }
    public string StorageRoot { get; init; } = string.Empty;
    public long MaximumUploadBytes { get; init; } = 4L * 1024 * 1024 * 1024;
    public int UploadChunkBytes { get; init; } = 16 * 1024 * 1024;
    public int MaximumEntries { get; init; } = 50_000;
    public long MaximumExpandedBytes { get; init; } = 20L * 1024 * 1024 * 1024;
    public long MaximumEntryBytes { get; init; } = 4L * 1024 * 1024 * 1024;
    public int MaximumCompressionRatio { get; init; } = 250;
    public int RetentionDays { get; init; } = 14;
    public int PublisherLeaseMinutes { get; init; } = 30;
    public int PublisherAgentFreshnessSeconds { get; init; } = 30;
    public string PublisherTokenSha256 { get; init; } = string.Empty;

    public bool IsValid() =>
        !Enabled ||
        Path.IsPathFullyQualified(StorageRoot) &&
        MaximumUploadBytes is >= 64 * 1024 * 1024 and <= 16L * 1024 * 1024 * 1024 &&
        UploadChunkBytes is >= 1024 * 1024 and <= 32 * 1024 * 1024 &&
        MaximumEntries is >= 1000 and <= 200_000 &&
        MaximumExpandedBytes >= MaximumUploadBytes &&
        MaximumExpandedBytes <= 100L * 1024 * 1024 * 1024 &&
        MaximumEntryBytes is >= 64 * 1024 * 1024 &&
        MaximumEntryBytes <= MaximumExpandedBytes &&
        MaximumCompressionRatio is >= 10 and <= 10_000 &&
        RetentionDays is >= 1 and <= 90 &&
        PublisherLeaseMinutes is >= 5 and <= 120 &&
        PublisherAgentFreshnessSeconds is >= 10 and <= 300 &&
        Sha256().IsMatch(PublisherTokenSha256);

    [GeneratedRegex("^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256();
}

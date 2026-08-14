using System.Text.RegularExpressions;

namespace Hechao.Api.Economy;

public sealed partial class EconomyServiceOptions
{
    public const string SectionName = "EconomyService";

    public string InternalTokenSha256 { get; set; } = string.Empty;

    public string[] AllowedServerIds { get; set; } = [];

    public int QuoteLifetimeSeconds { get; set; } = 30;

    public decimal MaximumTransferAmount { get; set; } = 1_000_000m;

    public bool IsConfigured =>
        InternalTokenSha256.Length == 64 && AllowedServerIds.Length > 0;

    public bool IsValid()
    {
        if (QuoteLifetimeSeconds is < 10 or > 120 ||
            MaximumTransferAmount is < 1m or > 100_000_000m)
        {
            return false;
        }

        if (string.IsNullOrEmpty(InternalTokenSha256))
        {
            return AllowedServerIds.Length == 0;
        }

        return Sha256Regex().IsMatch(InternalTokenSha256) &&
               AllowedServerIds is { Length: > 0 and <= 32 } &&
               AllowedServerIds.All(serverId =>
                   !string.IsNullOrWhiteSpace(serverId) &&
                   ServerIdRegex().IsMatch(serverId));
    }

    [GeneratedRegex("^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Regex();

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{1,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex ServerIdRegex();
}

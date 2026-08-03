using System.Security.Cryptography;
using System.Text;

namespace Hechao.Api.ServerControl;

public sealed class ServerControlOptions
{
    public const string SectionName = "ServerControl";

    public bool Enabled { get; init; }
    public int AgentFreshnessSeconds { get; init; } = 30;
    public int ClaimLeaseSeconds { get; init; } = 300;
    public int PackageDeploymentClaimLeaseMinutes { get; init; } = 180;
    public Dictionary<string, string> AgentTokenSha256 { get; init; } =
        new(StringComparer.Ordinal);

    public bool IsValid()
    {
        if (!Enabled)
        {
            return true;
        }

        return AgentFreshnessSeconds is >= 10 and <= 300 &&
               ClaimLeaseSeconds is >= 30 and <= 600 &&
               PackageDeploymentClaimLeaseMinutes is >= 15 and <= 480 &&
               AgentTokenSha256.Count is >= 1 and <= 32 &&
               AgentTokenSha256.All(pair =>
                   ServerControlRules.IsValidAgentId(pair.Key) &&
                   TryDecodeSha256(pair.Value, out _));
    }

    internal static bool TryDecodeSha256(string value, out byte[] hash)
    {
        hash = [];
        if (value.Length != 64)
        {
            return false;
        }

        try
        {
            hash = Convert.FromHexString(value);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

public sealed class ServerControlTokenValidator(
    Microsoft.Extensions.Options.IOptions<ServerControlOptions> options)
{
    private readonly IReadOnlyDictionary<string, byte[]> _agentTokenHashes =
        options.Value.AgentTokenSha256
            .Where(pair => ServerControlOptions.TryDecodeSha256(pair.Value, out _))
            .ToDictionary(
                pair => pair.Key,
                pair =>
                {
                    ServerControlOptions.TryDecodeSha256(pair.Value, out var hash);
                    return hash;
                },
                StringComparer.Ordinal);

    public bool IsConfigured =>
        options.Value.Enabled && _agentTokenHashes.Count > 0;

    public bool IsValid(string agentId, string? token)
    {
        if (!_agentTokenHashes.TryGetValue(agentId, out var expectedHash) ||
            string.IsNullOrWhiteSpace(token) ||
            token.Length > 256)
        {
            return false;
        }

        var actualHash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }
}

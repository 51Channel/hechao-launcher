using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Hechao.Api.Economy;

public enum EconomyAuthenticationStatus
{
    Allowed,
    NotConfigured,
    MissingCredentials,
    InvalidCredentials,
    ServerNotAllowed
}

public sealed class EconomyServiceTokenValidator(
    IOptions<EconomyServiceOptions> options)
{
    private readonly EconomyServiceOptions _options = options.Value;

    public EconomyAuthenticationStatus Validate(string? authorization, string? serverId)
    {
        if (!_options.IsConfigured)
        {
            return EconomyAuthenticationStatus.NotConfigured;
        }

        if (string.IsNullOrWhiteSpace(authorization) ||
            !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(serverId))
        {
            return EconomyAuthenticationStatus.MissingCredentials;
        }

        var token = authorization["Bearer ".Length..].Trim();
        if (token.Length is < 32 or > 256)
        {
            return EconomyAuthenticationStatus.InvalidCredentials;
        }

        var actual = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        var expected = Convert.FromHexString(_options.InternalTokenSha256);
        if (!CryptographicOperations.FixedTimeEquals(actual, expected))
        {
            return EconomyAuthenticationStatus.InvalidCredentials;
        }

        return _options.AllowedServerIds.Contains(
            serverId.Trim(),
            StringComparer.Ordinal)
            ? EconomyAuthenticationStatus.Allowed
            : EconomyAuthenticationStatus.ServerNotAllowed;
    }
}

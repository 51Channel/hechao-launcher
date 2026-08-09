using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Hechao.Api.ActivityPlans;

public sealed class WebsiteActivityBridgeTokenValidator(
    IOptions<WebsiteActivityBridgeOptions> options)
{
    private readonly byte[]? expectedHash = DecodeHash(
        options.Value.InternalTokenSha256);

    public bool IsConfigured =>
        expectedHash is not null && options.Value.ActorUserId != Guid.Empty;

    public bool IsValid(string? token)
    {
        if (expectedHash is null ||
            string.IsNullOrWhiteSpace(token) ||
            token.Length > 256)
        {
            return false;
        }

        var actualHash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }

    private static byte[]? DecodeHash(string value)
    {
        if (value.Length != 64)
        {
            return null;
        }

        try
        {
            return Convert.FromHexString(value);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}

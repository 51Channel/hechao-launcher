using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Hechao.Api.Velocity;

public sealed class VelocityAuthorizationTokenValidator(IOptions<VelocityAuthorizationOptions> options)
{
    private readonly byte[]? _expectedHash = DecodeHash(options.Value.InternalTokenSha256);

    public bool IsConfigured => _expectedHash is not null;

    public bool IsValid(string? token)
    {
        if (_expectedHash is null || string.IsNullOrWhiteSpace(token) || token.Length > 256)
        {
            return false;
        }

        var actualHash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return CryptographicOperations.FixedTimeEquals(actualHash, _expectedHash);
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

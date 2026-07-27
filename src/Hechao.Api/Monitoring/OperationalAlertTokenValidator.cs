using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Hechao.Api.Monitoring;

public sealed class OperationalAlertTokenValidator(
    IOptions<OperationalAlertOptions> options)
{
    private readonly byte[]? _expectedDigest =
        ParseDigest(options.Value.InternalTokenSha256);

    public bool IsConfigured => _expectedDigest is not null;

    public bool IsValid(string suppliedToken)
    {
        if (_expectedDigest is null ||
            string.IsNullOrWhiteSpace(suppliedToken) ||
            suppliedToken.Length > 512)
        {
            return false;
        }

        var actualDigest = SHA256.HashData(
            Encoding.UTF8.GetBytes(suppliedToken));
        return CryptographicOperations.FixedTimeEquals(
            actualDigest,
            _expectedDigest);
    }

    private static byte[]? ParseDigest(string value)
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

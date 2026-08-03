using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Hechao.Api.PackageImports;

public sealed class PackagePublisherTokenValidator(
    IOptions<PackageImportOptions> options)
{
    private readonly bool enabled = options.Value.Enabled;
    private readonly byte[]? expectedDigest = DecodeDigest(
        options.Value.PublisherTokenSha256);

    public bool IsConfigured => enabled && expectedDigest is not null;

    public bool IsValid(string? suppliedToken)
    {
        if (!enabled || expectedDigest is null ||
            string.IsNullOrWhiteSpace(suppliedToken) ||
            suppliedToken.Length > 256)
        {
            return false;
        }

        var actualDigest = SHA256.HashData(
            Encoding.UTF8.GetBytes(suppliedToken));
        return CryptographicOperations.FixedTimeEquals(
            actualDigest,
            expectedDigest);
    }

    private static byte[]? DecodeDigest(string value)
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

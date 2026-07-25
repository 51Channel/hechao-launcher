using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Norgerman.Cryptography.Scrypt;

namespace Hechao.Api.Authentication;

public sealed class HechaoAccountPasswordService(
    IPasswordHasher<HechaoAccountPasswordSubject> passwordHasher)
{
    private const int LegacySaltHexCharacters = 32;
    private const int LegacyHashBytes = 64;
    private const int LegacyCost = 16_384;
    private const int LegacyBlockSize = 8;
    private const int LegacyParallelism = 1;

    public string HashPassword(HechaoAccountPasswordSubject subject, string password) =>
        passwordHasher.HashPassword(subject, password);

    public string CreateDummyHash() =>
        HashPassword(
            new HechaoAccountPasswordSubject(Guid.Empty, "missing"),
            Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));

    public AccountPasswordVerificationResult Verify(
        HechaoAccountPasswordSubject subject,
        string storedHash,
        string password)
    {
        if (storedHash.StartsWith("scrypt$", StringComparison.Ordinal))
        {
            return VerifyLegacyScrypt(storedHash, password)
                ? AccountPasswordVerificationResult.SuccessRehashNeeded
                : AccountPasswordVerificationResult.Failed;
        }

        return passwordHasher.VerifyHashedPassword(subject, storedHash, password) switch
        {
            PasswordVerificationResult.Success =>
                AccountPasswordVerificationResult.Success,
            PasswordVerificationResult.SuccessRehashNeeded =>
                AccountPasswordVerificationResult.SuccessRehashNeeded,
            _ => AccountPasswordVerificationResult.Failed
        };
    }

    public bool IsSupportedLegacyHash(string storedHash)
    {
        if (!TryDecodeLegacyHash(storedHash, out _, out _))
        {
            return false;
        }

        return true;
    }

    private static bool VerifyLegacyScrypt(string storedHash, string password)
    {
        if (!TryDecodeLegacyHash(storedHash, out var salt, out var expectedHash))
        {
            return false;
        }

        var derivedHash = ScryptUtil.Scrypt(
            Encoding.UTF8.GetBytes(password),
            salt,
            LegacyCost,
            LegacyBlockSize,
            LegacyParallelism,
            LegacyHashBytes);
        return CryptographicOperations.FixedTimeEquals(derivedHash, expectedHash);
    }

    private static bool TryDecodeLegacyHash(
        string storedHash,
        out byte[] salt,
        out byte[] expectedHash)
    {
        salt = [];
        expectedHash = [];

        var parts = storedHash.Split('$');
        if (parts.Length != 3 ||
            !string.Equals(parts[0], "scrypt", StringComparison.Ordinal) ||
            parts[1].Length != LegacySaltHexCharacters ||
            parts[2].Length != LegacyHashBytes * 2)
        {
            return false;
        }

        try
        {
            _ = Convert.FromHexString(parts[1]);
            salt = Encoding.UTF8.GetBytes(parts[1]);
            expectedHash = Convert.FromHexString(parts[2]);
            return salt.Length == LegacySaltHexCharacters &&
                   expectedHash.Length == LegacyHashBytes;
        }
        catch (FormatException)
        {
            salt = [];
            expectedHash = [];
            return false;
        }
    }
}

public enum AccountPasswordVerificationResult
{
    Failed,
    Success,
    SuccessRehashNeeded
}

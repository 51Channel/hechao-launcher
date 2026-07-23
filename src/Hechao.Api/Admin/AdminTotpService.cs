using System.Security.Cryptography;
using OtpNet;
using QRCoder;

namespace Hechao.Api.Admin;

public sealed class AdminTotpService(TimeProvider timeProvider)
{
    private const int SecretBytes = 20;
    private const int RecoveryCodeBytes = 10;
    private const int RecoveryCodeCount = 8;

    public AdminTotpSetup CreateSetup(string issuer, string accountName)
    {
        var secret = RandomNumberGenerator.GetBytes(SecretBytes);
        var secretKey = Base32Encoding.ToString(secret);
        var label = Uri.EscapeDataString($"{issuer}:{accountName}");
        var otpAuthUri =
            $"otpauth://totp/{label}?secret={secretKey}&issuer={Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits=6&period=30";
        var qrCode = PngByteQRCodeHelper.GetQRCode(
            otpAuthUri,
            QRCodeGenerator.ECCLevel.Q,
            6);

        return new AdminTotpSetup(
            secret,
            secretKey,
            otpAuthUri,
            $"data:image/png;base64,{Convert.ToBase64String(qrCode)}");
    }

    public AdminTotpVerification Verify(byte[] secret, string? suppliedCode)
    {
        var code = NormalizeTotpCode(suppliedCode);
        if (code is null)
        {
            return AdminTotpVerification.Invalid;
        }

        var totp = new Totp(secret, step: 30, mode: OtpHashMode.Sha1, totpSize: 6);
        var verified = totp.VerifyTotp(
            timeProvider.GetUtcNow().UtcDateTime,
            code,
            out var timeWindowUsed,
            new VerificationWindow(previous: 1, future: 1));
        return verified
            ? new AdminTotpVerification(true, timeWindowUsed)
            : AdminTotpVerification.Invalid;
    }

    public AdminRecoveryCodeSet CreateRecoveryCodes()
    {
        var codes = new List<string>(RecoveryCodeCount);
        var hashes = new List<string>(RecoveryCodeCount);
        for (var index = 0; index < RecoveryCodeCount; index++)
        {
            var compact = Base32Encoding.ToString(
                RandomNumberGenerator.GetBytes(RecoveryCodeBytes));
            var code = string.Join(
                '-',
                Enumerable.Range(0, compact.Length / 4)
                    .Select(offset => compact.Substring(offset * 4, 4)));
            codes.Add(code);
            hashes.Add(HashRecoveryCode(code));
        }

        return new AdminRecoveryCodeSet(codes, hashes);
    }

    public static string? NormalizeRecoveryCode(string? suppliedCode)
    {
        if (string.IsNullOrWhiteSpace(suppliedCode))
        {
            return null;
        }

        var normalized = new string(suppliedCode
            .Where(char.IsAsciiLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());
        return normalized.Length == 16 ? normalized : null;
    }

    public static string HashRecoveryCode(string code)
    {
        var normalized = NormalizeRecoveryCode(code)
            ?? throw new ArgumentException("Recovery code format is invalid.", nameof(code));
        return Convert.ToHexString(
            SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(normalized)));
    }

    public static bool FixedTimeEqualsRecoveryHash(string expectedHex, string candidateHex)
    {
        byte[] expected;
        byte[] candidate;
        try
        {
            expected = Convert.FromHexString(expectedHex);
            candidate = Convert.FromHexString(candidateHex);
        }
        catch (FormatException)
        {
            return false;
        }

        return expected.Length == 32 &&
               candidate.Length == 32 &&
               CryptographicOperations.FixedTimeEquals(expected, candidate);
    }

    private static string? NormalizeTotpCode(string? suppliedCode)
    {
        if (string.IsNullOrWhiteSpace(suppliedCode))
        {
            return null;
        }

        var normalized = new string(suppliedCode.Where(char.IsAsciiDigit).ToArray());
        return normalized.Length == 6 ? normalized : null;
    }
}

public sealed record AdminTotpSetup(
    byte[] Secret,
    string SecretKey,
    string OtpAuthUri,
    string QrCodeDataUri);

public sealed record AdminTotpVerification(bool IsValid, long TimeWindowUsed)
{
    public static AdminTotpVerification Invalid { get; } = new(false, 0);
}

public sealed record AdminRecoveryCodeSet(
    IReadOnlyList<string> Codes,
    IReadOnlyList<string> Hashes);

using System.Buffers.Binary;
using System.Security.Cryptography;
using Hechao.Api.Admin;

namespace Hechao.Api.Tests;

public sealed class AdminTotpServiceTests
{
    private static readonly DateTimeOffset FrozenNow =
        DateTimeOffset.Parse("2026-07-24T06:30:00Z");

    [Fact]
    public void CreateSetup_ProducesAuthenticatorCompatiblePayload()
    {
        var service = new AdminTotpService(new FrozenTimeProvider(FrozenNow));

        var setup = service.CreateSetup("赫朝服务器", "HechaoPlayer");

        Assert.Equal(20, setup.Secret.Length);
        Assert.NotEmpty(setup.SecretKey);
        Assert.StartsWith("otpauth://totp/", setup.OtpAuthUri, StringComparison.Ordinal);
        Assert.Contains("digits=6", setup.OtpAuthUri, StringComparison.Ordinal);
        Assert.Contains("period=30", setup.OtpAuthUri, StringComparison.Ordinal);
        Assert.StartsWith("data:image/png;base64,", setup.QrCodeDataUri, StringComparison.Ordinal);
        Assert.NotEmpty(Convert.FromBase64String(
            setup.QrCodeDataUri["data:image/png;base64,".Length..]));
    }

    [Fact]
    public void Verify_AcceptsCurrentTotpAndRejectsMalformedCode()
    {
        var service = new AdminTotpService(new FrozenTimeProvider(FrozenNow));
        var setup = service.CreateSetup("赫朝服务器", "HechaoPlayer");
        var code = ComputeTotp(setup.Secret, FrozenNow);

        var verified = service.Verify(setup.Secret, code);
        var malformed = service.Verify(setup.Secret, "12-ab");

        Assert.True(verified.IsValid);
        Assert.False(malformed.IsValid);
    }

    [Fact]
    public void RecoveryCodes_AreUniqueNormalizableAndHashed()
    {
        var service = new AdminTotpService(new FrozenTimeProvider(FrozenNow));

        var result = service.CreateRecoveryCodes();

        Assert.Equal(8, result.Codes.Count);
        Assert.Equal(8, result.Codes.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(8, result.Hashes.Count);
        Assert.All(result.Codes, code =>
        {
            Assert.Equal(19, code.Length);
            Assert.Equal(16, AdminTotpService.NormalizeRecoveryCode(code)!.Length);
        });
        Assert.All(result.Hashes, hash => Assert.Equal(64, hash.Length));
        Assert.True(AdminTotpService.FixedTimeEqualsRecoveryHash(
            result.Hashes[0],
            AdminTotpService.HashRecoveryCode(result.Codes[0].ToLowerInvariant())));
        Assert.False(AdminTotpService.FixedTimeEqualsRecoveryHash(
            result.Hashes[0],
            result.Hashes[1]));
        Assert.False(AdminTotpService.FixedTimeEqualsRecoveryHash("not-hex", result.Hashes[0]));
    }

    private static string ComputeTotp(byte[] secret, DateTimeOffset timestamp)
    {
        Span<byte> counterBytes = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64BigEndian(
            counterBytes,
            timestamp.ToUnixTimeSeconds() / 30);
        var hash = HMACSHA1.HashData(secret, counterBytes);
        var offset = hash[^1] & 0x0f;
        var binaryCode =
            ((hash[offset] & 0x7f) << 24) |
            (hash[offset + 1] << 16) |
            (hash[offset + 2] << 8) |
            hash[offset + 3];
        return (binaryCode % 1_000_000).ToString("D6");
    }

    private sealed class FrozenTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

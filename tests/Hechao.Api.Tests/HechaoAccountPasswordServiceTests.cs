using Hechao.Api.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Hechao.Api.Tests;

public sealed class HechaoAccountPasswordServiceTests
{
    private const string LegacyHash =
        "scrypt$00112233445566778899aabbccddeeff$" +
        "159474590650a5c233eb90717fea58b709f096f4a9fceca2fcf8309abe597265af" +
        "1e99102b38a50a9b0adefbdc32d57f07abb3c190ba771f922db13b10765b9c";

    private static readonly HechaoAccountPasswordSubject Subject =
        new(Guid.Parse("4ef968ff-f9c6-4cc8-9398-d1219636bc3e"), "unified_user");

    [Fact]
    public void Verify_AcceptsNodeScryptHashAndRequestsRehash()
    {
        var service = CreateService();

        var result = service.Verify(Subject, LegacyHash, "UnifiedPass123");

        Assert.Equal(AccountPasswordVerificationResult.SuccessRehashNeeded, result);
        Assert.True(service.IsSupportedLegacyHash(LegacyHash));
    }

    [Fact]
    public void Verify_RejectsWrongOrMalformedLegacyPassword()
    {
        var service = CreateService();

        Assert.Equal(
            AccountPasswordVerificationResult.Failed,
            service.Verify(Subject, LegacyHash, "WrongPass123"));
        Assert.False(service.IsSupportedLegacyHash("scrypt$invalid$hash"));
    }

    [Fact]
    public void HashPassword_UsesCurrentAspNetIdentityFormat()
    {
        var service = CreateService();
        var hash = service.HashPassword(Subject, "UnifiedPass123");

        Assert.Equal(
            AccountPasswordVerificationResult.Success,
            service.Verify(Subject, hash, "UnifiedPass123"));
    }

    private static HechaoAccountPasswordService CreateService()
    {
        var hasher = new PasswordHasher<HechaoAccountPasswordSubject>(
            Options.Create(new PasswordHasherOptions
            {
                IterationCount = 100_000
            }));
        return new HechaoAccountPasswordService(hasher);
    }
}

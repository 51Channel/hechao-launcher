using Hechao.Api.Admin;
using Hechao.Contracts;

namespace Hechao.Api.Tests;

public sealed class AdminAccountSecurityRulesTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("reason\u0001")]
    public void Validate_RejectsMissingShortOrControlCharacterReasons(string reason)
    {
        var errors = AdminAccountSecurityRules.Validate(
            new AdminSecurityReasonRequest(reason));

        Assert.Contains("reason", errors);
    }

    [Fact]
    public void Validate_AcceptsTrimmedReason()
    {
        var errors = AdminAccountSecurityRules.Validate(
            new AdminSecurityReasonRequest("  玩家本人申请撤销设备  "));

        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateBan_RejectsExpiredDateAndInvalidRevision()
    {
        var errors = AdminAccountSecurityRules.Validate(
            new AdminMinecraftIdentityBanRequest(
                "违反活动规则",
                Now,
                ExpectedRevision: 0),
            Now);

        Assert.Contains("expiresAt", errors);
        Assert.Contains("expectedRevision", errors);
    }

    [Fact]
    public void ValidateBanDelete_RequiresPositiveRevision()
    {
        var errors = AdminAccountSecurityRules.Validate(
            new AdminMinecraftIdentityBanDeleteRequest(
                "管理员确认解除封禁",
                ExpectedRevision: 0));

        Assert.Contains("expectedRevision", errors);
    }
}

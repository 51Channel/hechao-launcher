using Hechao.Api.Admin;
using Hechao.Contracts;

namespace Hechao.Api.Tests;

public sealed class AdminAccessRulesTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Evaluate_ActiveDenyOverridesTier()
    {
        var access = AdminAccessRules.Evaluate(
            CreateUser(AccessTier.Administrator),
            serverVisible: true,
            ServerStatus.Online,
            AccessTier.Member,
            CreateRule(AdminServerAccessDecision.Deny, Now.AddHours(1)),
            Now,
            TimeSpan.FromHours(1));

        Assert.False(access.Allowed);
        Assert.Equal(AdminEffectiveAccessReason.DeniedByRule, access.Reason);
    }

    [Fact]
    public void Evaluate_ActiveAllowOverridesInsufficientTier()
    {
        var access = AdminAccessRules.Evaluate(
            CreateUser(AccessTier.Member),
            serverVisible: true,
            ServerStatus.Online,
            AccessTier.Administrator,
            CreateRule(AdminServerAccessDecision.Allow, Now.AddHours(1)),
            Now,
            TimeSpan.FromHours(1));

        Assert.True(access.Allowed);
        Assert.Equal(AdminEffectiveAccessReason.AllowedByRule, access.Reason);
    }

    [Fact]
    public void Evaluate_ExpiredRuleFallsBackToTier()
    {
        var access = AdminAccessRules.Evaluate(
            CreateUser(AccessTier.Member),
            serverVisible: true,
            ServerStatus.Online,
            AccessTier.Participant,
            CreateRule(AdminServerAccessDecision.Allow, Now.AddMinutes(-1)),
            Now,
            TimeSpan.FromHours(1));

        Assert.False(access.Allowed);
        Assert.Equal(AdminEffectiveAccessReason.InsufficientTier, access.Reason);
    }

    [Fact]
    public void Evaluate_ScheduleClosureTakesPriorityOverAllowRule()
    {
        var access = AdminAccessRules.Evaluate(
            CreateUser(AccessTier.Member),
            serverVisible: true,
            ServerStatus.Closed,
            AccessTier.Member,
            CreateRule(AdminServerAccessDecision.Allow, null),
            Now,
            TimeSpan.FromHours(1));

        Assert.False(access.Allowed);
        Assert.Equal(AdminEffectiveAccessReason.ServerUnavailable, access.Reason);
    }

    [Fact]
    public void Evaluate_MinecraftIdentityBanPreventsAllServerAccess()
    {
        var access = AdminAccessRules.Evaluate(
            CreateUser(AccessTier.Administrator, isMinecraftIdentityBanned: true),
            serverVisible: true,
            ServerStatus.Online,
            AccessTier.Member,
            CreateRule(AdminServerAccessDecision.Allow, null),
            Now,
            TimeSpan.FromHours(1));

        Assert.False(access.Allowed);
        Assert.Equal(AdminEffectiveAccessReason.MinecraftIdentityBanned, access.Reason);
    }

    [Fact]
    public void Validate_RejectsExpiredRuleAndInvalidRevision()
    {
        var errors = AdminAccessRules.Validate(
            new AdminServerAccessRuleUpsertRequest(
                AdminServerAccessDecision.Allow,
                "",
                Now,
                ExpectedRevision: 0),
            Now);

        Assert.Contains("expiresAt", errors);
        Assert.Contains("expectedRevision", errors);
    }

    private static AdminUserSummary CreateUser(
        AccessTier tier,
        bool isMinecraftIdentityBanned = false)
    {
        return new AdminUserSummary(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "player",
            "Player",
            null,
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            "Player",
            "default",
            tier,
            Now,
            IsDisabled: false,
            IsMinecraftIdentityBanned: isMinecraftIdentityBanned,
            ActiveRuleCount: 0,
            Now);
    }

    private static AdminServerAccessRuleRecord CreateRule(
        AdminServerAccessDecision decision,
        DateTimeOffset? expiresAt)
    {
        return new AdminServerAccessRuleRecord(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "activity",
            decision,
            "",
            expiresAt,
            Revision: 1,
            Now,
            Now);
    }
}

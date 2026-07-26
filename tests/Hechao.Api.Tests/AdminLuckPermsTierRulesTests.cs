using Hechao.Api.Admin;
using Hechao.Contracts;

namespace Hechao.Api.Tests;

public sealed class AdminLuckPermsTierRulesTests
{
    [Fact]
    public void ValidateAdminRequest_AcceptsControlledTierChange()
    {
        var errors = AdminLuckPermsTierRules.Validate(
            new AdminLuckPermsTierChangeRequest(
                AccessTier.Participant,
                "default",
                "活动成员资格已审核通过"));

        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateAdminRequest_RejectsUnsafeGroupAndReason()
    {
        var errors = AdminLuckPermsTierRules.Validate(
            new AdminLuckPermsTierChangeRequest(
                AccessTier.Participant,
                "default'; drop table",
                "bad\nreason"));

        Assert.Contains("expectedPrimaryGroup", errors);
        Assert.Contains("reason", errors);
    }

    [Theory]
    [InlineData("owl5-lobby", 10, true)]
    [InlineData("x", 10, false)]
    [InlineData("owl5-lobby", 0, false)]
    [InlineData("owl5-lobby", 21, false)]
    public void ValidateClaim_RestrictsAgentAndBatch(
        string agentId,
        int limit,
        bool expectedValid)
    {
        var errors = AdminLuckPermsTierRules.Validate(
            new LuckPermsTierCommandClaimRequest(agentId, limit));

        Assert.Equal(expectedValid, errors.Count == 0);
    }

    [Fact]
    public void ValidateCompletion_RequiresFailureCodeOnlyForFailure()
    {
        Assert.Empty(AdminLuckPermsTierRules.Validate(
            new LuckPermsTierCommandCompletionRequest(
                "owl5-lobby",
                1,
                LuckPermsTierCommandOutcome.Failed,
                "default",
                "luckperms-save-failed")));

        Assert.Contains(
            "failureCode",
            AdminLuckPermsTierRules.Validate(
                new LuckPermsTierCommandCompletionRequest(
                    "owl5-lobby",
                    1,
                    LuckPermsTierCommandOutcome.Failed,
                    "default",
                    null)));
        Assert.Contains(
            "failureCode",
            AdminLuckPermsTierRules.Validate(
                new LuckPermsTierCommandCompletionRequest(
                    "owl5-lobby",
                    1,
                    LuckPermsTierCommandOutcome.Applied,
                    "vip",
                    "unexpected")));

        Assert.Contains(
            "attemptCount",
            AdminLuckPermsTierRules.Validate(
                new LuckPermsTierCommandCompletionRequest(
                    "owl5-lobby",
                    0,
                    LuckPermsTierCommandOutcome.Applied,
                    "vip",
                    null)));
    }
}

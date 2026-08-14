using System.Text.Json;
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
    [InlineData("owl5-lobby", "0.1.3", 2, 10, true)]
    [InlineData("x", "0.1.3", 2, 10, false)]
    [InlineData("owl5-lobby", "legacy", 2, 10, false)]
    [InlineData("owl5-lobby", "0.1.2", 0, 10, false)]
    [InlineData("owl5-lobby", "0.1.3", 1, 10, false)]
    [InlineData("owl5-lobby", "0.1.3", 2, 0, false)]
    [InlineData("owl5-lobby", "0.1.3", 2, 21, false)]
    public void ValidateClaim_RestrictsAgentProtocolAndBatch(
        string agentId,
        string agentVersion,
        int protocolVersion,
        int limit,
        bool expectedValid)
    {
        var errors = AdminLuckPermsTierRules.Validate(
            new LuckPermsTierCommandClaimRequest(
                agentId,
                agentVersion,
                protocolVersion,
                limit));

        Assert.Equal(expectedValid, errors.Count == 0);
    }

    [Fact]
    public void ValidateClaim_RejectsExactLegacyJsonPayload()
    {
        var request = JsonSerializer.Deserialize<LuckPermsTierCommandClaimRequest>(
            """
            {"agentId":"owl5-lobby","limit":10}
            """,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(request);
        var errors = AdminLuckPermsTierRules.Validate(request);
        Assert.Contains("agentVersion", errors);
        Assert.Contains("protocolVersion", errors);
    }

    [Fact]
    public void AgentClaimIdentity_IncludesSoftwareAndProtocolVersion()
    {
        Assert.Equal(
            "owl5-lobby@0.1.3/p2",
            LuckPermsTierCommandRepository.FormatAgentClaimIdentity(
                " owl5-lobby ",
                " 0.1.3 ",
                2));
    }

    [Fact]
    public void ValidateCompletion_RequiresFailureCodeOnlyForFailure()
    {
        Assert.Empty(AdminLuckPermsTierRules.Validate(
            new LuckPermsTierCommandCompletionRequest(
                "owl5-lobby",
                "0.1.3",
                2,
                1,
                LuckPermsTierCommandOutcome.Failed,
                "default",
                "luckperms-save-failed")));

        Assert.Contains(
            "failureCode",
            AdminLuckPermsTierRules.Validate(
                new LuckPermsTierCommandCompletionRequest(
                    "owl5-lobby",
                    "0.1.3",
                    2,
                    1,
                    LuckPermsTierCommandOutcome.Failed,
                    "default",
                    null)));
        Assert.Contains(
            "failureCode",
            AdminLuckPermsTierRules.Validate(
                new LuckPermsTierCommandCompletionRequest(
                    "owl5-lobby",
                    "0.1.3",
                    2,
                    1,
                    LuckPermsTierCommandOutcome.Applied,
                    "vip",
                    "unexpected")));

        Assert.Contains(
            "attemptCount",
            AdminLuckPermsTierRules.Validate(
                new LuckPermsTierCommandCompletionRequest(
                    "owl5-lobby",
                    "0.1.3",
                    2,
                    0,
                    LuckPermsTierCommandOutcome.Applied,
                    "vip",
                    null)));
    }

    [Fact]
    public void ValidateCompletion_RejectsLegacyProtocol()
    {
        var errors = AdminLuckPermsTierRules.Validate(
            new LuckPermsTierCommandCompletionRequest(
                "owl5-lobby",
                "0.1.2",
                0,
                1,
                LuckPermsTierCommandOutcome.Applied,
                "vip",
                null));

        Assert.Contains("protocolVersion", errors);
    }
}

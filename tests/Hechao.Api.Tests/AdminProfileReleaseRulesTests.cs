using Hechao.Api.Admin;
using Hechao.Contracts;

namespace Hechao.Api.Tests;

public sealed class AdminProfileReleaseRulesTests
{
    [Fact]
    public void ValidateCreate_AcceptsMachineIdAndDisplayName()
    {
        var errors = AdminProfileReleaseRules.Validate(
            new AdminClientProfileCreateRequest(
                "activity-neoforge-1.21.11",
                "活动服 NeoForge"));

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("A")]
    [InlineData("Activity")]
    [InlineData("-activity")]
    [InlineData("activity server")]
    public void ValidateCreate_RejectsInvalidProfileId(string profileId)
    {
        var errors = AdminProfileReleaseRules.Validate(
            new AdminClientProfileCreateRequest(profileId, "活动服"));

        Assert.Contains("id", errors);
    }

    [Fact]
    public void ValidateChannel_RequiresProductionAtOneHundredPercent()
    {
        var errors = AdminProfileReleaseRules.Validate(
            ClientProfileReleaseChannel.Production,
            new AdminClientProfileChannelUpdateRequest(
                new string('a', 64),
                RolloutPercentage: 50,
                ExpectedRevision: 1));

        Assert.Contains("rolloutPercentage", errors);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(100)]
    public void ValidateChannel_AcceptsGrayPercentages(int percentage)
    {
        var errors = AdminProfileReleaseRules.Validate(
            ClientProfileReleaseChannel.Gray,
            new AdminClientProfileChannelUpdateRequest(
                new string('a', 64),
                percentage,
                ExpectedRevision: 1));

        Assert.Empty(errors);
    }

    [Fact]
    public void ValidatePause_RequiresReasonOnlyWhenPausing()
    {
        Assert.Contains(
            "reason",
            AdminProfileReleaseRules.Validate(
                new AdminClientProfileReleasePauseRequest(
                    IsPaused: true,
                    Reason: "",
                    ExpectedRevision: 1)));
        Assert.Empty(
            AdminProfileReleaseRules.Validate(
                new AdminClientProfileReleasePauseRequest(
                    IsPaused: false,
                    Reason: "",
                    ExpectedRevision: 1)));
    }
}

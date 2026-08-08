using Hechao.Api.Admin;
using Hechao.Contracts;

namespace Hechao.Api.Tests;

public sealed class AdminProfileReleaseRulesTests
{
    [Fact]
    public void DeleteEndpoint_ExplicitlyBindsConfirmationRequestFromBody()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Hechao.Api",
            "Program.cs"));

        Assert.Matches(
            @"DeleteAdminClientProfileAsync\(\s*string profileId,\s*\[FromBody\]\s+AdminClientProfileDeleteRequest request,",
            source);
    }

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

    [Fact]
    public void ValidateArchive_RequiresAuditableReasonAndRevision()
    {
        var errors = AdminProfileReleaseRules.Validate(
            new AdminClientProfileArchiveRequest(
                Reason: "x",
                ExpectedRevision: 0));

        Assert.Contains("reason", errors);
        Assert.Contains("expectedRevision", errors);
        Assert.Empty(AdminProfileReleaseRules.Validate(
            new AdminClientProfileArchiveRequest(
                Reason: "测试档案已经停止使用",
                ExpectedRevision: 3)));
    }

    [Fact]
    public void ValidateDelete_RequiresExactProfileConfirmation()
    {
        const string profileId = "unused-draft-1.21.11";
        var invalid = AdminProfileReleaseRules.Validate(
            profileId,
            new AdminClientProfileDeleteRequest(
                Reason: "清理误建的空档案",
                Confirmation: profileId,
                ExpectedRevision: 2));

        Assert.Contains("confirmation", invalid);
        Assert.Empty(AdminProfileReleaseRules.Validate(
            profileId,
            new AdminClientProfileDeleteRequest(
                Reason: "清理误建的空档案",
                Confirmation: $"DELETE {profileId}",
                ExpectedRevision: 2)));
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Hechao.Launcher.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Repository not found.");
    }
}

using Hechao.Api.Admin;
using Hechao.Contracts;

namespace Hechao.Api.Tests;

public sealed class AdminServerRulesTests
{
    [Fact]
    public void ValidateCreate_AcceptsCompleteServerDefinition()
    {
        var errors = AdminServerRules.Validate(CreateValidRequest());

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("A")]
    [InlineData("UpperCase")]
    [InlineData("-activity")]
    [InlineData("activity server")]
    [InlineData("activity\n")]
    public void ValidateCreate_RejectsInvalidServerId(string serverId)
    {
        var errors = AdminServerRules.Validate(CreateValidRequest() with { Id = serverId });

        Assert.Contains("id", errors);
    }

    [Fact]
    public void ValidateCreate_RejectsControlCharactersInDisplayText()
    {
        var errors = AdminServerRules.Validate(
            CreateValidRequest() with { DisplayName = "活动服\r\n伪造字段" });

        Assert.Contains("displayName", errors);
    }

    [Fact]
    public void ValidateCreate_RejectsTrailingNewlineInMachineIdentifiers()
    {
        var request = CreateValidRequest() with
        {
            MinecraftVersion = "1.21.11\n",
            ClientProfileId = "activity-neoforge-1.21.11\n",
            VelocityTarget = "activity\n"
        };

        var errors = AdminServerRules.Validate(request);

        Assert.Contains("minecraftVersion", errors);
        Assert.Contains("clientProfileId", errors);
        Assert.Contains("velocityTarget", errors);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10001)]
    public void ValidateCreate_RejectsPlayerLimitOutsideRange(int maxPlayers)
    {
        var errors = AdminServerRules.Validate(
            CreateValidRequest() with { MaxPlayers = maxPlayers });

        Assert.Contains("maxPlayers", errors);
    }

    [Fact]
    public void ValidateCreate_RejectsUnknownEnumValues()
    {
        var request = CreateValidRequest() with
        {
            Status = (ServerStatus)999,
            Loader = (ModLoaderKind)999,
            MinimumTier = (AccessTier)999
        };

        var errors = AdminServerRules.Validate(request);

        Assert.Contains("status", errors);
        Assert.Contains("loader", errors);
        Assert.Contains("minimumTier", errors);
    }

    [Fact]
    public void ValidateUpdate_RequiresOptimisticConcurrencyRevision()
    {
        var request = new AdminServerUpdateRequest(
            "活动服",
            "活",
            "活",
            ServerStatus.Maintenance,
            30,
            "1.21.11",
            ModLoaderKind.NeoForge,
            AccessTier.Participant,
            "activity-neoforge-1.21.11",
            "activity",
            30,
            ExpectedRevision: 0);

        var errors = AdminServerRules.Validate(request);

        Assert.Contains("expectedRevision", errors);
    }

    [Fact]
    public void ValidateVisibility_RequiresOptimisticConcurrencyRevision()
    {
        var errors = AdminServerRules.Validate(
            new AdminServerVisibilityRequest(false, ExpectedRevision: -1));

        Assert.Contains("expectedRevision", errors);
    }

    [Fact]
    public void IsValidServerId_MatchesRouteValidation()
    {
        Assert.True(AdminServerRules.IsValidServerId("dollnight"));
        Assert.True(AdminServerRules.IsValidServerId("activity.neoforge"));
        Assert.False(AdminServerRules.IsValidServerId("DollNight"));
        Assert.False(AdminServerRules.IsValidServerId("x"));
    }

    private static AdminServerCreateRequest CreateValidRequest()
    {
        return new AdminServerCreateRequest(
            "activity",
            "海绵小镇躲猫猫",
            "海",
            "海",
            ServerStatus.Online,
            30,
            "1.21.11",
            ModLoaderKind.NeoForge,
            AccessTier.Participant,
            "activity-neoforge-1.21.11",
            "activity",
            30,
            IsVisible: true);
    }
}

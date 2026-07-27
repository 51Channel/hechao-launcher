using Hechao.Api.Velocity;
using Hechao.Contracts;

namespace Hechao.Api.Tests;

public sealed class VelocityAuthorizationRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 23, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan MaximumSnapshotAge = TimeSpan.FromMinutes(20);

    [Fact]
    public void Evaluate_RequiresLinkedPlayer()
    {
        var result = VelocityAuthorizationRules.Evaluate(
            player: null,
            Server(AccessTier.Member),
            Now,
            MaximumSnapshotAge);

        Assert.Equal(VelocityAuthorizationReason.PlayerNotLinked, result);
    }

    [Fact]
    public void Evaluate_DisabledPlayerCannotUseExplicitAllow()
    {
        var result = VelocityAuthorizationRules.Evaluate(
            Player(AccessTier.Administrator, disabled: true),
            Server(AccessTier.Member, ServerAccessOverride.Allow),
            Now,
            MaximumSnapshotAge);

        Assert.Equal(VelocityAuthorizationReason.PlayerDisabled, result);
    }

    [Fact]
    public void Evaluate_MaintenanceServerCannotUseExplicitAllow()
    {
        var result = VelocityAuthorizationRules.Evaluate(
            Player(AccessTier.Administrator),
            Server(
                AccessTier.Member,
                ServerAccessOverride.Allow,
                ServerStatus.Maintenance),
            Now,
            MaximumSnapshotAge);

        Assert.Equal(VelocityAuthorizationReason.ServerUnavailable, result);
    }

    [Fact]
    public void Evaluate_BannedMinecraftIdentityCannotUseExplicitAllow()
    {
        var result = VelocityAuthorizationRules.Evaluate(
            Player(AccessTier.Administrator, minecraftBanned: true),
            Server(AccessTier.Member, ServerAccessOverride.Allow),
            Now,
            MaximumSnapshotAge);

        Assert.Equal(VelocityAuthorizationReason.MinecraftIdentityBanned, result);
    }

    [Fact]
    public void Evaluate_ExplicitDenyWins()
    {
        var result = VelocityAuthorizationRules.Evaluate(
            Player(AccessTier.Administrator),
            Server(AccessTier.Member, ServerAccessOverride.Deny),
            Now,
            MaximumSnapshotAge);

        Assert.Equal(VelocityAuthorizationReason.AccessDenied, result);
    }

    [Fact]
    public void Evaluate_ExplicitAllowBypassesTierAndSnapshotAge()
    {
        var result = VelocityAuthorizationRules.Evaluate(
            Player(AccessTier.Member, snapshotAvailable: false),
            Server(AccessTier.Administrator, ServerAccessOverride.Allow),
            Now,
            MaximumSnapshotAge);

        Assert.Equal(VelocityAuthorizationReason.Allowed, result);
    }

    [Fact]
    public void Evaluate_RequiresFreshSnapshotForElevatedServer()
    {
        var result = VelocityAuthorizationRules.Evaluate(
            Player(AccessTier.Participant, syncedAt: Now.AddMinutes(-21)),
            Server(AccessTier.Participant),
            Now,
            MaximumSnapshotAge);

        Assert.Equal(VelocityAuthorizationReason.PermissionDataStale, result);
    }

    [Fact]
    public void Evaluate_RejectsInsufficientTier()
    {
        var result = VelocityAuthorizationRules.Evaluate(
            Player(AccessTier.Member),
            Server(AccessTier.Participant),
            Now,
            MaximumSnapshotAge);

        Assert.Equal(VelocityAuthorizationReason.InsufficientTier, result);
    }

    [Fact]
    public void Evaluate_AllowsMemberWithoutLuckPermsSnapshotOnMemberServer()
    {
        var result = VelocityAuthorizationRules.Evaluate(
            Player(AccessTier.Member, snapshotAvailable: false),
            Server(AccessTier.Member),
            Now,
            MaximumSnapshotAge);

        Assert.Equal(VelocityAuthorizationReason.Allowed, result);
    }

    [Fact]
    public void EvaluateClientCompatibility_RequiresLaunchSession()
    {
        var result = VelocityAuthorizationRules.EvaluateClientCompatibility(
            sessionServer: null,
            Server(AccessTier.Member));

        Assert.Equal(VelocityAuthorizationReason.LaunchGrantRequired, result);
    }

    [Fact]
    public void EvaluateClientCompatibility_AllowsSameVersionPaperTransfer()
    {
        var result = VelocityAuthorizationRules.EvaluateClientCompatibility(
            Server(
                AccessTier.Member,
                serverId: "lobby",
                clientProfileId: "base-1.21.11"),
            Server(
                AccessTier.Member,
                serverId: "survival1",
                clientProfileId: "vanilla-1.21.11"));

        Assert.Equal(VelocityAuthorizationReason.Allowed, result);
    }

    [Fact]
    public void EvaluateClientCompatibility_AllowsModdedClientToSameVersionPaperServer()
    {
        var result = VelocityAuthorizationRules.EvaluateClientCompatibility(
            Server(
                AccessTier.Member,
                serverId: "activity",
                loader: "NeoForge",
                clientProfileId: "activity-neoforge-1.21.11"),
            Server(
                AccessTier.Member,
                serverId: "lobby",
                loader: "Paper",
                clientProfileId: "base-1.21.11"));

        Assert.Equal(VelocityAuthorizationReason.Allowed, result);
    }

    [Fact]
    public void EvaluateClientCompatibility_RejectsWrongProfileForModdedTarget()
    {
        var result = VelocityAuthorizationRules.EvaluateClientCompatibility(
            Server(
                AccessTier.Member,
                serverId: "lobby",
                clientProfileId: "base-1.21.11"),
            Server(
                AccessTier.Member,
                serverId: "activity",
                loader: "NeoForge",
                clientProfileId: "activity-neoforge-1.21.11"));

        Assert.Equal(VelocityAuthorizationReason.ClientProfileMismatch, result);
    }

    [Fact]
    public void EvaluateClientCompatibility_RejectsDifferentMinecraftVersion()
    {
        var result = VelocityAuthorizationRules.EvaluateClientCompatibility(
            Server(
                AccessTier.Member,
                serverId: "lobby",
                minecraftVersion: "1.21.11"),
            Server(
                AccessTier.Member,
                serverId: "pvp",
                minecraftVersion: "1.20.1",
                loader: "Fabric",
                clientProfileId: "pvp-fabric-1.20.1"));

        Assert.Equal(VelocityAuthorizationReason.MinecraftVersionMismatch, result);
    }

    private static VelocityPlayerAccess Player(
        AccessTier tier,
        bool disabled = false,
        bool minecraftBanned = false,
        DateTimeOffset? syncedAt = default,
        bool snapshotAvailable = true)
    {
        return new VelocityPlayerAccess(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            disabled,
            minecraftBanned,
            tier,
            tier == AccessTier.Member ? "default" : "vip",
            snapshotAvailable ? syncedAt ?? Now : null);
    }

    private static VelocityServerAccess Server(
        AccessTier minimumTier,
        ServerAccessOverride accessOverride = ServerAccessOverride.None,
        ServerStatus status = ServerStatus.Online,
        string serverId = "activity",
        string minecraftVersion = "1.21.11",
        string loader = "Paper",
        string clientProfileId = "base-1.21.11")
    {
        return new VelocityServerAccess(
            serverId,
            serverId,
            status,
            minimumTier,
            accessOverride,
            minecraftVersion,
            loader,
            clientProfileId);
    }
}

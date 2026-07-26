using System.Globalization;
using System.Security.Claims;
using Hechao.Api.Admin;
using Hechao.Api.Authentication;
using Hechao.Contracts;

namespace Hechao.Api.Tests;

public sealed class LauncherPrincipalExtensionsTests
{
    [Fact]
    public void GetPlayer_ReadsAdminWebSessionClaims()
    {
        var userId = Guid.NewGuid();
        var minecraftUuid = Guid.NewGuid();
        var syncedAt = DateTimeOffset.UtcNow.AddMinutes(-2);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString("D")),
            new Claim(ClaimTypes.Name, "SmokeAdmin"),
            new Claim(ClaimTypes.Role, nameof(AccessTier.Administrator)),
            new Claim(LauncherClaimTypes.MinecraftUuid, minecraftUuid.ToString("D")),
            new Claim(LauncherClaimTypes.MinecraftName, "SmokeAdmin"),
            new Claim(LauncherClaimTypes.LuckPermsPrimaryGroup, "owner"),
            new Claim(LauncherClaimTypes.AccessTier, nameof(AccessTier.Administrator)),
            new Claim(
                LauncherClaimTypes.LuckPermsSyncedAt,
                syncedAt.ToString("O", CultureInfo.InvariantCulture)),
            new Claim(AdminWebClaimTypes.AuthenticationMethod, "mfa")
        ], AdminWebSessionAuthenticationHandler.SchemeName));

        var player = principal.GetPlayer();

        Assert.NotNull(player);
        Assert.Equal(userId, player.UserId);
        Assert.Equal(minecraftUuid, player.MinecraftUuid);
        Assert.Equal("SmokeAdmin", player.MinecraftName);
        Assert.Equal("owner", player.LuckPermsPrimaryGroup);
        Assert.Equal(AccessTier.Administrator, player.AccessTier);
        Assert.Equal(syncedAt, player.LuckPermsSyncedAt);
    }

    [Fact]
    public void GetPlayer_RejectsPrincipalWithoutMinecraftNameClaim()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("D")),
            new Claim(LauncherClaimTypes.MinecraftUuid, Guid.NewGuid().ToString("D")),
            new Claim(LauncherClaimTypes.LuckPermsPrimaryGroup, "owner"),
            new Claim(LauncherClaimTypes.AccessTier, nameof(AccessTier.Administrator))
        ], AdminWebSessionAuthenticationHandler.SchemeName));

        Assert.Null(principal.GetPlayer());
    }
}

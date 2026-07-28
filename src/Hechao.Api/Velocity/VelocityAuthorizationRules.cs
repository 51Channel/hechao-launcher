using Hechao.Contracts;

namespace Hechao.Api.Velocity;

internal static class VelocityAuthorizationRules
{
    public static VelocityAuthorizationReason Evaluate(
        VelocityPlayerAccess? player,
        VelocityServerAccess? server,
        DateTimeOffset now,
        TimeSpan maximumLuckPermsAge)
    {
        if (player is null)
        {
            return VelocityAuthorizationReason.PlayerNotLinked;
        }

        if (player.IsDisabled)
        {
            return VelocityAuthorizationReason.PlayerDisabled;
        }

        if (player.IsMinecraftIdentityBanned)
        {
            return VelocityAuthorizationReason.MinecraftIdentityBanned;
        }

        if (server is null)
        {
            return VelocityAuthorizationReason.ServerUnknown;
        }

        if (server.Status != ServerStatus.Online)
        {
            return VelocityAuthorizationReason.ServerUnavailable;
        }

        if (server.OverrideDecision == ServerAccessOverride.Deny)
        {
            return VelocityAuthorizationReason.AccessDenied;
        }

        if (server.OverrideDecision == ServerAccessOverride.Allow)
        {
            return VelocityAuthorizationReason.Allowed;
        }

        if (server.MinimumTier > AccessTier.Member &&
            (player.LuckPermsSyncedAt is null ||
             player.LuckPermsSyncedAt < now.Subtract(maximumLuckPermsAge)))
        {
            return VelocityAuthorizationReason.PermissionDataStale;
        }

        return player.AccessTier >= server.MinimumTier
            ? VelocityAuthorizationReason.Allowed
            : VelocityAuthorizationReason.InsufficientTier;
    }

    public static string GetMessage(VelocityAuthorizationReason reason)
    {
        return reason switch
        {
            VelocityAuthorizationReason.Allowed => "允许进入服务器。",
            VelocityAuthorizationReason.PlayerNotLinked => "请先通过赫朝启动器完成 Microsoft 正版登录。",
            VelocityAuthorizationReason.PlayerDisabled => "该赫朝账号已被停用。",
            VelocityAuthorizationReason.MinecraftIdentityBanned => "该 Minecraft 正版身份已被管理员封禁。",
            VelocityAuthorizationReason.ServerUnknown => "目标服务器尚未登记到赫朝平台。",
            VelocityAuthorizationReason.ServerUnavailable => "目标服务器当前未开放。",
            VelocityAuthorizationReason.AccessDenied => "你没有该服务器的进入权限。",
            VelocityAuthorizationReason.InsufficientTier => "你的当前称号等级不足以进入该服务器。",
            VelocityAuthorizationReason.PermissionDataStale => "称号权限数据暂未同步，请稍后再试。",
            VelocityAuthorizationReason.LaunchGrantRequired => "请从赫朝启动器重新进入服务器。",
            VelocityAuthorizationReason.LaunchGrantIpMismatch => "启动器授权与当前网络不一致，请重新启动游戏。",
            VelocityAuthorizationReason.MinecraftVersionMismatch => "当前客户端版本与目标服务器不一致，请从赫朝启动器选择该服务器后重新进入。",
            VelocityAuthorizationReason.ClientProfileMismatch => "当前客户端档案与目标模组服不兼容，请从赫朝启动器安装并选择对应客户端。",
            _ => "暂时无法验证服务器权限。"
        };
    }

    public static VelocityAuthorizationReason EvaluateClientCompatibility(
        VelocityServerAccess? sessionServer,
        VelocityServerAccess targetServer)
    {
        if (sessionServer is null)
        {
            return VelocityAuthorizationReason.LaunchGrantRequired;
        }

        if (!string.Equals(
                sessionServer.MinecraftVersion,
                targetServer.MinecraftVersion,
                StringComparison.OrdinalIgnoreCase) &&
            !targetServer.AllowsProtocolTranslation)
        {
            return VelocityAuthorizationReason.MinecraftVersionMismatch;
        }

        if (RequiresMatchingClientProfile(targetServer.Loader) &&
            !string.Equals(
                sessionServer.ClientProfileId,
                targetServer.ClientProfileId,
                StringComparison.Ordinal))
        {
            return VelocityAuthorizationReason.ClientProfileMismatch;
        }

        return VelocityAuthorizationReason.Allowed;
    }

    private static bool RequiresMatchingClientProfile(string loader) =>
        loader.Equals("Fabric", StringComparison.OrdinalIgnoreCase) ||
        loader.Equals("Forge", StringComparison.OrdinalIgnoreCase) ||
        loader.Equals("NeoForge", StringComparison.OrdinalIgnoreCase);
}

internal sealed record VelocityPlayerAccess(
    Guid UserId,
    Guid MinecraftUuid,
    bool IsDisabled,
    bool IsMinecraftIdentityBanned,
    AccessTier AccessTier,
    string LuckPermsPrimaryGroup,
    DateTimeOffset? LuckPermsSyncedAt);

internal sealed record VelocityServerAccess(
    string ServerId,
    string VelocityTarget,
    ServerStatus Status,
    AccessTier MinimumTier,
    ServerAccessOverride OverrideDecision,
    string MinecraftVersion,
    string Loader,
    string ClientProfileId,
    bool AllowsProtocolTranslation);

internal enum ServerAccessOverride
{
    None,
    Allow,
    Deny
}

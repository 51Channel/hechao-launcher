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
            VelocityAuthorizationReason.ServerUnknown => "目标服务器尚未登记到赫朝平台。",
            VelocityAuthorizationReason.ServerUnavailable => "目标服务器当前未开放。",
            VelocityAuthorizationReason.AccessDenied => "你没有该服务器的进入权限。",
            VelocityAuthorizationReason.InsufficientTier => "你的当前称号等级不足以进入该服务器。",
            VelocityAuthorizationReason.PermissionDataStale => "称号权限数据暂未同步，请稍后再试。",
            VelocityAuthorizationReason.LaunchGrantRequired => "请从赫朝启动器重新进入服务器。",
            VelocityAuthorizationReason.LaunchGrantIpMismatch => "启动器授权与当前网络不一致，请重新启动游戏。",
            _ => "暂时无法验证服务器权限。"
        };
    }
}

internal sealed record VelocityPlayerAccess(
    Guid UserId,
    Guid MinecraftUuid,
    bool IsDisabled,
    AccessTier AccessTier,
    string LuckPermsPrimaryGroup,
    DateTimeOffset? LuckPermsSyncedAt);

internal sealed record VelocityServerAccess(
    string ServerId,
    string VelocityTarget,
    ServerStatus Status,
    AccessTier MinimumTier,
    ServerAccessOverride OverrideDecision);

internal enum ServerAccessOverride
{
    None,
    Allow,
    Deny
}

using Hechao.Contracts;

namespace Hechao.Api.Admin;

public static class AdminAccessRules
{
    public static Dictionary<string, string[]> ValidateSearch(string query, int limit)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (query.Length > 80 || query.Any(char.IsControl))
        {
            errors["query"] = ["搜索内容最多 80 个字符，且不能包含控制字符。"];
        }

        if (limit is < 1 or > 100)
        {
            errors["limit"] = ["limit 必须在 1 到 100 之间。"];
        }

        return errors;
    }

    public static Dictionary<string, string[]> Validate(
        AdminServerAccessRuleUpsertRequest request,
        DateTimeOffset now)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (!Enum.IsDefined(request.Decision))
        {
            errors["decision"] = ["单服权限决定无效。"];
        }

        if (request.Reason is null ||
            request.Reason.Length > 240 ||
            request.Reason.Any(char.IsControl))
        {
            errors["reason"] = ["原因最多 240 个字符，且不能包含控制字符。"];
        }

        if (request.ExpiresAt is not null && request.ExpiresAt <= now)
        {
            errors["expiresAt"] = ["到期时间必须晚于当前时间。"];
        }

        if (request.ExpectedRevision is < 1)
        {
            errors["expectedRevision"] = ["规则修订号无效。"];
        }

        return errors;
    }

    public static Dictionary<string, string[]> Validate(
        AdminServerAccessRuleDeleteRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (request.ExpectedRevision < 1)
        {
            errors["expectedRevision"] = ["规则修订号无效。"];
        }

        return errors;
    }

    public static (bool Allowed, AdminEffectiveAccessReason Reason) Evaluate(
        AdminUserSummary user,
        bool serverVisible,
        ServerStatus effectiveStatus,
        AccessTier minimumTier,
        AdminServerAccessRuleRecord? rule,
        DateTimeOffset now,
        TimeSpan maximumPermissionAge)
    {
        if (user.IsDisabled)
        {
            return (false, AdminEffectiveAccessReason.PlayerDisabled);
        }

        if (user.MinecraftUuid is null)
        {
            return (false, AdminEffectiveAccessReason.PlayerNotLinked);
        }

        if (user.IsMinecraftIdentityBanned)
        {
            return (false, AdminEffectiveAccessReason.MinecraftIdentityBanned);
        }

        if (!serverVisible)
        {
            return (false, AdminEffectiveAccessReason.ServerArchived);
        }

        if (effectiveStatus != ServerStatus.Online)
        {
            return (false, AdminEffectiveAccessReason.ServerUnavailable);
        }

        var activeRule = rule is not null &&
                         (rule.ExpiresAt is null || rule.ExpiresAt > now);
        if (activeRule && rule!.Decision == AdminServerAccessDecision.Deny)
        {
            return (false, AdminEffectiveAccessReason.DeniedByRule);
        }

        if (activeRule && rule!.Decision == AdminServerAccessDecision.Allow)
        {
            return (true, AdminEffectiveAccessReason.AllowedByRule);
        }

        if (minimumTier > AccessTier.Member &&
            (user.LuckPermsSyncedAt is null ||
             user.LuckPermsSyncedAt < now.Subtract(maximumPermissionAge)))
        {
            return (false, AdminEffectiveAccessReason.PermissionDataStale);
        }

        return user.AccessTier >= minimumTier
            ? (true, AdminEffectiveAccessReason.AllowedByTier)
            : (false, AdminEffectiveAccessReason.InsufficientTier);
    }
}

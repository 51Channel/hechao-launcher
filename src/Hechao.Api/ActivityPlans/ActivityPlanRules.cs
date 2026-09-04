using System.Text.RegularExpressions;
using Hechao.Contracts;

namespace Hechao.Api.ActivityPlans;

public static partial class ActivityPlanRules
{
    public const int MaximumDurationDays = 90;

    public static IReadOnlyDictionary<string, string[]> Validate(
        AdminActivityPlanCreateRequest request) =>
        ValidateFields(
            request.Title,
            request.Announcement,
            request.OpensAt,
            request.ClosesAt,
            request.MaximumPlayers,
            request.MinimumTier,
            request.PackageImportId,
            request.TargetServerId,
            expectedRevision: null);

    public static IReadOnlyDictionary<string, string[]> Validate(
        AdminActivityPlanUpdateRequest request) =>
        ValidateFields(
            request.Title,
            request.Announcement,
            request.OpensAt,
            request.ClosesAt,
            request.MaximumPlayers,
            request.MinimumTier,
            request.PackageImportId,
            request.TargetServerId,
            request.ExpectedRevision);

    public static IReadOnlyDictionary<string, string[]> Validate(
        AdminActivityPlanRevisionRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.ExpectedRevision <= 0)
        {
            errors["expectedRevision"] = ["企划修订号无效，请刷新后重试。"];
        }

        return errors;
    }

    public static IReadOnlyDictionary<string, string[]> Validate(
        string planId,
        AdminActivityPlanArchiveRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.ExpectedRevision <= 0)
        {
            errors["expectedRevision"] = ["企划修订号无效，请刷新后重试。"];
        }

        ValidateReason(request.Reason, errors);
        return errors;
    }

    public static IReadOnlyDictionary<string, string[]> Validate(
        string planId,
        AdminActivityPlanDeployRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.ExpectedRevision <= 0)
        {
            errors["expectedRevision"] = ["企划修订号无效，请刷新后重试。"];
        }

        ValidateReason(request.Reason, errors);
        var expected = $"DEPLOY {planId}";
        if (!string.Equals(
                request.Confirmation?.Trim(),
                expected,
                StringComparison.Ordinal))
        {
            errors["confirmation"] = [$"请输入“{expected}”确认部署。"];
        }

        return errors;
    }

    public static bool Overlaps(
        DateTimeOffset opensAt,
        DateTimeOffset closesAt,
        DateTimeOffset otherOpensAt,
        DateTimeOffset otherClosesAt) =>
        opensAt < otherClosesAt && otherOpensAt < closesAt;

    private static IReadOnlyDictionary<string, string[]> ValidateFields(
        string? title,
        string? announcement,
        DateTimeOffset opensAt,
        DateTimeOffset closesAt,
        int maximumPlayers,
        AccessTier minimumTier,
        Guid? packageImportId,
        string? targetServerId,
        long? expectedRevision)
    {
        var errors = new Dictionary<string, string[]>();
        var normalizedTitle = title?.Trim() ?? string.Empty;
        if (normalizedTitle.Length is < 2 or > 80 ||
            normalizedTitle.Any(char.IsControl))
        {
            errors["title"] = ["企划名称必须为 2 到 80 个可显示字符。"];
        }

        var normalizedAnnouncement = announcement?.Trim() ?? string.Empty;
        if (normalizedAnnouncement.Length > 280 ||
            normalizedAnnouncement.Any(character =>
                char.IsControl(character) && character is not '\r' and not '\n' and not '\t'))
        {
            errors["announcement"] = ["活动说明不能超过 280 个字符。"];
        }

        if (opensAt == default || closesAt == default || opensAt >= closesAt)
        {
            errors["schedule"] = ["结束时间必须晚于开放时间。"];
        }
        else if (closesAt - opensAt > TimeSpan.FromDays(MaximumDurationDays))
        {
            errors["schedule"] = [$"单个企划最长不能超过 {MaximumDurationDays} 天。"];
        }

        if (maximumPlayers is < 1 or > 1000)
        {
            errors["maximumPlayers"] = ["人数上限必须为 1 到 1000。"];
        }

        if (!Enum.IsDefined(minimumTier) || minimumTier == AccessTier.Administrator)
        {
            errors["minimumTier"] = ["最低称号只能是成员、活动成员或协作者。"];
        }

        if (packageImportId == Guid.Empty)
        {
            errors["packageImportId"] = ["整合包标识无效。"];
        }

        if (targetServerId is not null &&
            !ServerIdPattern().IsMatch(targetServerId.Trim()))
        {
            errors["targetServerId"] = ["承载服务器标识无效。"];
        }

        if (expectedRevision is <= 0)
        {
            errors["expectedRevision"] = ["企划修订号无效，请刷新后重试。"];
        }

        return errors;
    }

    private static void ValidateReason(
        string? reason,
        IDictionary<string, string[]> errors)
    {
        var normalized = reason?.Trim() ?? string.Empty;
        if (normalized.Length is < 4 or > 500 || normalized.Any(char.IsControl))
        {
            errors["reason"] = ["操作原因必须为 4 到 500 个可显示字符。"];
        }
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{1,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex ServerIdPattern();
}

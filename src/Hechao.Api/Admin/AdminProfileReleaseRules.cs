using System.Text.RegularExpressions;
using Hechao.Contracts;

namespace Hechao.Api.Admin;

public static partial class AdminProfileReleaseRules
{
    public static IReadOnlyDictionary<string, string[]> Validate(
        AdminClientProfileCreateRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (!IsValidProfileId(request.Id))
        {
            errors["id"] = ["档案 ID 必须为 2 至 64 位小写字母、数字、点、下划线或短横线。"];
        }

        ValidateDisplayName(request.DisplayName, errors);
        return errors;
    }

    public static IReadOnlyDictionary<string, string[]> Validate(
        AdminClientProfileUpdateRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        ValidateDisplayName(request.DisplayName, errors);
        if (request.ExpectedRevision <= 0)
        {
            errors["expectedRevision"] = ["档案修订号无效，请刷新后重试。"];
        }

        return errors;
    }

    public static IReadOnlyDictionary<string, string[]> Validate(
        AdminClientProfileArchiveRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        ValidateLifecycleReason(request.Reason, errors);
        ValidateProfileRevision(request.ExpectedRevision, errors);
        return errors;
    }

    public static IReadOnlyDictionary<string, string[]> Validate(
        AdminClientProfileRestoreRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        ValidateProfileRevision(request.ExpectedRevision, errors);
        return errors;
    }

    public static IReadOnlyDictionary<string, string[]> Validate(
        string profileId,
        AdminClientProfileDeleteRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        ValidateLifecycleReason(request.Reason, errors);
        ValidateProfileRevision(request.ExpectedRevision, errors);
        if (!string.Equals(
                request.Confirmation?.Trim(),
                $"DELETE {profileId}",
                StringComparison.Ordinal))
        {
            errors["confirmation"] =
                [$"请输入“DELETE {profileId}”确认永久删除客户端档案。"];
        }

        return errors;
    }

    public static IReadOnlyDictionary<string, string[]> Validate(
        ClientProfileReleaseChannel channel,
        AdminClientProfileChannelUpdateRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (!Enum.IsDefined(channel))
        {
            errors["channel"] = ["发布通道无效。"];
        }

        if (request.ManifestSha256 is not null &&
            !Sha256Regex().IsMatch(request.ManifestSha256))
        {
            errors["manifestSha256"] = ["发布清单 SHA-256 无效。"];
        }

        if (request.RolloutPercentage is < 0 or > 100 ||
            channel == ClientProfileReleaseChannel.Production &&
            request.RolloutPercentage != 100)
        {
            errors["rolloutPercentage"] =
                ["测试和灰度比例必须为 0 至 100，正式通道固定为 100。"];
        }

        if (request.ExpectedRevision <= 0)
        {
            errors["expectedRevision"] = ["通道修订号无效，请刷新后重试。"];
        }

        return errors;
    }

    public static IReadOnlyDictionary<string, string[]> Validate(
        AdminClientProfileChannelRollbackRequest request)
    {
        return request.ExpectedRevision > 0
            ? new Dictionary<string, string[]>()
            : new Dictionary<string, string[]>
            {
                ["expectedRevision"] = ["通道修订号无效，请刷新后重试。"]
            };
    }

    public static IReadOnlyDictionary<string, string[]> Validate(
        AdminClientProfileReleasePauseRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        var reason = request.Reason?.Trim() ?? string.Empty;
        if (request.IsPaused &&
            (reason.Length is < 2 or > 280 || reason.Any(char.IsControl)))
        {
            errors["reason"] = ["暂停原因必须为 2 至 280 个可显示字符。"];
        }

        if (request.ExpectedRevision <= 0)
        {
            errors["expectedRevision"] = ["发布修订号无效，请刷新后重试。"];
        }

        return errors;
    }

    public static bool IsValidProfileId(string? profileId) =>
        profileId is not null && ProfileIdRegex().IsMatch(profileId);

    public static bool IsValidManifestSha256(string? value) =>
        value is not null && Sha256Regex().IsMatch(value);

    public static string ToDatabaseValue(ClientProfileReleaseChannel channel) =>
        channel.ToString().ToLowerInvariant();

    private static void ValidateDisplayName(
        string? displayName,
        IDictionary<string, string[]> errors)
    {
        var normalized = displayName?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 or > 80 || normalized.Any(char.IsControl))
        {
            errors["displayName"] = ["显示名称必须为 1 至 80 个可显示字符。"];
        }
    }

    private static void ValidateLifecycleReason(
        string? reason,
        IDictionary<string, string[]> errors)
    {
        var normalized = reason?.Trim() ?? string.Empty;
        if (normalized.Length is < 4 or > 280 || normalized.Any(char.IsControl))
        {
            errors["reason"] = ["操作原因必须为 4 至 280 个可显示字符。"];
        }
    }

    private static void ValidateProfileRevision(
        long expectedRevision,
        IDictionary<string, string[]> errors)
    {
        if (expectedRevision <= 0)
        {
            errors["expectedRevision"] = ["档案修订号无效，请刷新后重试。"];
        }
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{1,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex ProfileIdRegex();

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Regex();
}

using System.Text.RegularExpressions;
using Hechao.Contracts;

namespace Hechao.Api.Admin;

public static class AdminServerRules
{
    private static readonly Regex IdPattern = new(
        "\\A[a-z0-9][a-z0-9._-]{1,63}\\z",
        RegexOptions.CultureInvariant);

    private static readonly Regex VersionPattern = new(
        "\\A[A-Za-z0-9][A-Za-z0-9._+\\-]{0,39}\\z",
        RegexOptions.CultureInvariant);

    private static readonly Regex VelocityTargetPattern = new(
        "\\A[a-z0-9][a-z0-9._-]{0,63}\\z",
        RegexOptions.CultureInvariant);

    public static Dictionary<string, string[]> Validate(AdminServerCreateRequest request)
    {
        var errors = ValidateCommon(
            request.DisplayName,
            request.ShortName,
            request.IconGlyph,
            request.Status,
            request.MaxPlayers,
            request.MinecraftVersion,
            request.Loader,
            request.MinimumTier,
            request.ClientProfileId,
            request.VelocityTarget,
            request.SortOrder);

        if (string.IsNullOrWhiteSpace(request.Id) || !IdPattern.IsMatch(request.Id))
        {
            errors["id"] = ["服务器 ID 必须为 2 到 64 位小写字母、数字、点、下划线或短横线。"];
        }

        return errors;
    }

    public static Dictionary<string, string[]> Validate(AdminServerUpdateRequest request)
    {
        var errors = ValidateCommon(
            request.DisplayName,
            request.ShortName,
            request.IconGlyph,
            request.Status,
            request.MaxPlayers,
            request.MinecraftVersion,
            request.Loader,
            request.MinimumTier,
            request.ClientProfileId,
            request.VelocityTarget,
            request.SortOrder);

        if (request.ExpectedRevision < 1)
        {
            errors["expectedRevision"] = ["服务器修订号无效。"];
        }

        return errors;
    }

    public static Dictionary<string, string[]> Validate(AdminServerVisibilityRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (request.ExpectedRevision < 1)
        {
            errors["expectedRevision"] = ["服务器修订号无效。"];
        }

        return errors;
    }

    public static bool IsValidServerId(string serverId) =>
        !string.IsNullOrWhiteSpace(serverId) && IdPattern.IsMatch(serverId);

    private static Dictionary<string, string[]> ValidateCommon(
        string displayName,
        string shortName,
        string iconGlyph,
        ServerStatus status,
        int maxPlayers,
        string minecraftVersion,
        ModLoaderKind loader,
        AccessTier minimumTier,
        string clientProfileId,
        string velocityTarget,
        int sortOrder)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        ValidateText(errors, "displayName", displayName, 1, 80, "显示名");
        ValidateText(errors, "shortName", shortName, 1, 12, "短名称");
        ValidateText(errors, "iconGlyph", iconGlyph, 1, 12, "图标字符");

        if (!Enum.IsDefined(status))
        {
            errors["status"] = ["服务器状态无效。"];
        }

        if (maxPlayers is < 1 or > 10000)
        {
            errors["maxPlayers"] = ["最大人数必须在 1 到 10000 之间。"];
        }

        if (string.IsNullOrWhiteSpace(minecraftVersion) ||
            !VersionPattern.IsMatch(minecraftVersion))
        {
            errors["minecraftVersion"] = ["Minecraft 版本格式无效。"];
        }

        if (!Enum.IsDefined(loader))
        {
            errors["loader"] = ["模组加载器类型无效。"];
        }

        if (!Enum.IsDefined(minimumTier))
        {
            errors["minimumTier"] = ["最低等级无效。"];
        }

        if (string.IsNullOrWhiteSpace(clientProfileId) ||
            !IdPattern.IsMatch(clientProfileId))
        {
            errors["clientProfileId"] = ["客户端档案 ID 无效。"];
        }

        if (string.IsNullOrWhiteSpace(velocityTarget) ||
            !VelocityTargetPattern.IsMatch(velocityTarget))
        {
            errors["velocityTarget"] = ["Velocity 目标名称无效。"];
        }

        if (sortOrder is < -100000 or > 100000)
        {
            errors["sortOrder"] = ["排序值必须在 -100000 到 100000 之间。"];
        }

        return errors;
    }

    private static void ValidateText(
        IDictionary<string, string[]> errors,
        string key,
        string value,
        int minimumLength,
        int maximumLength,
        string label)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length < minimumLength ||
            value.Length > maximumLength ||
            value.Any(char.IsControl))
        {
            errors[key] = [$"{label}长度必须在 {minimumLength} 到 {maximumLength} 之间，且不能包含控制字符。"];
        }
    }
}

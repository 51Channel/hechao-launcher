using System.Text.RegularExpressions;
using Hechao.Contracts;

namespace Hechao.Api.ServerControl;

public static partial class DeploymentSlotRules
{
    public const int MaximumDynamicSlotsPerAgent = 16;

    public static IReadOnlyDictionary<string, string[]> Validate(
        AdminCreateDeploymentSlotRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        var serverId = request.ServerId?.Trim() ?? string.Empty;
        if (!DynamicSlotId().IsMatch(serverId))
        {
            errors["serverId"] =
                ["槽 ID 必须以 activity- 开头，只能包含小写字母、数字和连字符。"];
        }

        var displayName = request.DisplayName?.Trim() ?? string.Empty;
        if (displayName.Length is < 2 or > 80 || displayName.Any(char.IsControl))
        {
            errors["displayName"] = ["槽名称必须为 2 至 80 个可显示字符。"];
        }

        if (!string.Equals(
                request.TemplateServerId?.Trim(),
                PackageImports.PackageImportRules.ActivityServerId,
                StringComparison.Ordinal))
        {
            errors["templateServerId"] = ["当前只允许使用已批准的 activity 模板。"];
        }

        var reason = request.Reason?.Trim() ?? string.Empty;
        if (reason.Length is < 4 or > 500 || reason.Any(char.IsControl))
        {
            errors["reason"] = ["创建原因必须为 4 至 500 个可显示字符。"];
        }

        var expectedConfirmation = $"CREATE {serverId}";
        if (!string.Equals(
                request.Confirmation?.Trim(),
                expectedConfirmation,
                StringComparison.Ordinal))
        {
            errors["confirmation"] = [$"请输入“{expectedConfirmation}”确认。"];
        }

        return errors;
    }

    [GeneratedRegex("^activity-[a-z0-9][a-z0-9-]{1,39}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex DynamicSlotId();
}

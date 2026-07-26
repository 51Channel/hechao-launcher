using System.Text.RegularExpressions;
using Hechao.Contracts;

namespace Hechao.Api.Admin;

public static partial class AdminLuckPermsTierRules
{
    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex PrimaryGroupPattern();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{2,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex AgentIdPattern();

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,119}$", RegexOptions.CultureInvariant)]
    private static partial Regex FailureCodePattern();

    public static IReadOnlyDictionary<string, string[]> Validate(
        AdminLuckPermsTierChangeRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (!Enum.IsDefined(request.TargetTier))
        {
            errors["targetTier"] = ["目标等级无效。"];
        }

        if (string.IsNullOrWhiteSpace(request.ExpectedPrimaryGroup) ||
            !PrimaryGroupPattern().IsMatch(request.ExpectedPrimaryGroup.Trim()))
        {
            errors["expectedPrimaryGroup"] = ["当前 LuckPerms 主组无效。"];
        }

        if (string.IsNullOrWhiteSpace(request.Reason) ||
            request.Reason.Trim().Length is < 4 or > 500 ||
            request.Reason.Any(char.IsControl))
        {
            errors["reason"] = ["操作原因必须为 4 到 500 个可见字符。"];
        }

        return errors;
    }

    public static IReadOnlyDictionary<string, string[]> Validate(
        LuckPermsTierCommandClaimRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.AgentId) ||
            !AgentIdPattern().IsMatch(request.AgentId.Trim()))
        {
            errors["agentId"] = ["代理标识无效。"];
        }

        if (request.Limit is < 1 or > 20)
        {
            errors["limit"] = ["领取数量必须在 1 到 20 之间。"];
        }

        return errors;
    }

    public static IReadOnlyDictionary<string, string[]> Validate(
        LuckPermsTierCommandCompletionRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.AgentId) ||
            !AgentIdPattern().IsMatch(request.AgentId.Trim()))
        {
            errors["agentId"] = ["代理标识无效。"];
        }

        if (!Enum.IsDefined(request.Outcome))
        {
            errors["outcome"] = ["执行结果无效。"];
        }

        if (request.AttemptCount < 1)
        {
            errors["attemptCount"] = ["领取序号必须大于零。"];
        }

        if (string.IsNullOrWhiteSpace(request.ObservedPrimaryGroup) ||
            !PrimaryGroupPattern().IsMatch(request.ObservedPrimaryGroup.Trim()))
        {
            errors["observedPrimaryGroup"] = ["实际 LuckPerms 主组无效。"];
        }

        var failureCode = request.FailureCode?.Trim();
        if (request.Outcome == LuckPermsTierCommandOutcome.Failed)
        {
            if (string.IsNullOrWhiteSpace(failureCode) ||
                !FailureCodePattern().IsMatch(failureCode))
            {
                errors["failureCode"] = ["失败结果必须提供安全的错误代码。"];
            }
        }
        else if (!string.IsNullOrWhiteSpace(failureCode))
        {
            errors["failureCode"] = ["成功或冲突结果不能携带失败代码。"];
        }

        return errors;
    }
}

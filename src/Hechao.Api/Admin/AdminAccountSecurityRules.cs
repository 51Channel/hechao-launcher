using Hechao.Contracts;

namespace Hechao.Api.Admin;

public static class AdminAccountSecurityRules
{
    public static Dictionary<string, string[]> Validate(AdminSecurityReasonRequest request)
    {
        return ValidateReason(request.Reason);
    }

    public static Dictionary<string, string[]> Validate(
        AdminMinecraftIdentityBanRequest request,
        DateTimeOffset now)
    {
        var errors = ValidateReason(request.Reason);
        if (request.ExpiresAt is not null && request.ExpiresAt <= now)
        {
            errors["expiresAt"] = ["封禁到期时间必须晚于当前时间。"];
        }

        if (request.ExpectedRevision is < 1)
        {
            errors["expectedRevision"] = ["封禁记录修订号无效。"];
        }

        return errors;
    }

    public static Dictionary<string, string[]> Validate(
        AdminMinecraftIdentityBanDeleteRequest request)
    {
        var errors = ValidateReason(request.Reason);
        if (request.ExpectedRevision < 1)
        {
            errors["expectedRevision"] = ["封禁记录修订号无效。"];
        }

        return errors;
    }

    private static Dictionary<string, string[]> ValidateReason(string? reason)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var normalized = reason?.Trim() ?? string.Empty;
        if (normalized.Length is < 4 or > 500 || normalized.Any(char.IsControl))
        {
            errors["reason"] = ["操作原因需要 4–500 个字符，且不能包含控制字符。"];
        }

        return errors;
    }
}

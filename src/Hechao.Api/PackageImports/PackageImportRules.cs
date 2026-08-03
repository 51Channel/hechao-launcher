using System.Text.RegularExpressions;
using Hechao.Api.Admin;
using Hechao.Contracts;

namespace Hechao.Api.PackageImports;

public static partial class PackageImportRules
{
    public const string ActivityConflictGroup = "owl5-activity-slot";
    public const int ActivityPort = 25568;

    public static IReadOnlyDictionary<string, string[]> Validate(
        AdminPackageUploadCreateRequest request,
        PackageImportOptions options)
    {
        var errors = new Dictionary<string, string[]>();
        var fileName = Path.GetFileName(request.FileName?.Trim() ?? string.Empty);
        if (fileName.Length is < 5 or > 180 ||
            !string.Equals(fileName, request.FileName?.Trim(), StringComparison.Ordinal) ||
            (!fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
             !fileName.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase)) ||
            fileName.Any(char.IsControl))
        {
            errors["fileName"] = ["文件名必须是 5 至 180 个字符的 ZIP 或 MRPACK 文件名。"];
        }

        if (request.TotalBytes < 1024 ||
            request.TotalBytes > options.MaximumUploadBytes)
        {
            errors["totalBytes"] =
                [$"整合包大小必须在 1 KiB 至 {options.MaximumUploadBytes} 字节之间。"];
        }

        return errors;
    }

    public static IReadOnlyDictionary<string, string[]> Validate(
        AdminPackageImportConfirmRequest request,
        AdminPackageImportRecord import)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.ExpectedRevision <= 0 || request.ExpectedRevision != import.Revision)
        {
            errors["expectedRevision"] = ["导入任务已变化，请刷新后重试。"];
        }

        if (import.Status != PackageImportStatus.AwaitingReview)
        {
            errors["status"] = ["只有等待确认的导入任务可以发布。"];
        }

        if (import.Analysis is null || import.Analysis.HasBlockingIssues ||
            import.Analysis.Client is null || import.Analysis.Server is null)
        {
            errors["analysis"] = ["识别结果仍有阻断项，不能开始发布。"];
        }

        if (!AdminProfileReleaseRules.IsValidProfileId(request.ProfileId))
        {
            errors["profileId"] = ["客户端档案 ID 无效。"];
        }

        ValidateText(request.ProfileDisplayName, "profileDisplayName", 1, 80, errors);
        ValidateText(request.ServerDisplayName, "serverDisplayName", 1, 80, errors);
        if (!VersionPattern().IsMatch(request.Version ?? string.Empty))
        {
            errors["version"] = ["版本号必须使用 major.minor.patch 格式。"];
        }

        if (!ServerIdPattern().IsMatch(request.TargetServerId ?? string.Empty))
        {
            errors["targetServerId"] = ["服务端控制目标无效。"];
        }

        if (!Enum.IsDefined(request.MinimumTier) ||
            request.MinimumTier == AccessTier.Administrator)
        {
            errors["minimumTier"] = ["最低称号只能是成员、活动成员或协作者。"];
        }

        if (request.MaximumMemoryMiB is < 1024 or > 32768 ||
            request.MaximumMemoryMiB % 256 != 0)
        {
            errors["maximumMemoryMiB"] = ["最大内存必须为 1024 至 32768 MiB 的 256 MiB 整数倍。"];
        }

        var expectedConfirmation = $"发布并部署 {import.ImportId:D}";
        if (!string.Equals(request.Confirmation?.Trim(), expectedConfirmation, StringComparison.Ordinal))
        {
            errors["confirmation"] = [$"请输入“{expectedConfirmation}”确认。"];
        }

        return errors;
    }

    public static bool IsActivityTarget(
        AdminServerControlTargetRecord target) =>
        string.Equals(
            target.ConflictGroup,
            ActivityConflictGroup,
            StringComparison.Ordinal) &&
        target.Port == ActivityPort;

    private static void ValidateText(
        string? value,
        string field,
        int minimumLength,
        int maximumLength,
        IDictionary<string, string[]> errors)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length < minimumLength ||
            normalized.Length > maximumLength ||
            normalized.Any(char.IsControl))
        {
            errors[field] = [$"字段必须为 {minimumLength} 至 {maximumLength} 个可显示字符。"];
        }
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{1,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex ServerIdPattern();

    [GeneratedRegex("^[0-9]+\\.[0-9]+\\.[0-9]+(?:[-+][0-9A-Za-z.-]+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();
}

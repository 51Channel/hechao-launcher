using System.Text.RegularExpressions;
using Hechao.Api.Admin;
using Hechao.Contracts;

namespace Hechao.Api.PackageImports;

public static partial class PackageImportRules
{
    public const string ActivityServerId = "activity";
    public const string ActivityAgentId = "owl5";
    public const string ActivityVelocityTarget = "activity";
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
        else if (!Enum.TryParse<ModLoaderKind>(
                     import.Analysis.Metadata.Loader,
                     ignoreCase: true,
                     out var loader) ||
                 !Enum.IsDefined(loader))
        {
            errors["analysis"] = ["识别出的加载器不在启动器支持范围内。"];
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

        if (request.MaximumMemoryMiB is < 1024 or > 65536 ||
            request.MaximumMemoryMiB % 256 != 0)
        {
            errors["maximumMemoryMiB"] = ["最大内存必须为 1024 至 65536 MiB 的 256 MiB 整数倍。"];
        }

        var expectedConfirmation = request.DeployServer
            ? $"发布并部署 {import.ImportId:D}"
            : $"发布并入库 {import.ImportId:D}";
        if (!string.Equals(request.Confirmation?.Trim(), expectedConfirmation, StringComparison.Ordinal))
        {
            errors["confirmation"] = [$"请输入“{expectedConfirmation}”确认。"];
        }

        return errors;
    }

    public static bool IsActivityTarget(
        AdminServerControlTargetRecord target) =>
        target.PackageDeploymentEnabled &&
        IsActivityTarget(
            target.ServerId,
            target.AgentId,
            target.ConflictGroup,
            target.Port);

    public static bool IsActivityTarget(
        string serverId,
        string agentId,
        string? conflictGroup,
        int port) =>
        string.Equals(serverId, ActivityServerId, StringComparison.Ordinal) &&
        IsOwl5ActivitySlot(agentId, conflictGroup, port);

    public static bool IsPackageDeploymentTarget(
        AdminServerControlTargetRecord target) =>
        target.PackageDeploymentEnabled &&
        target.DeploymentSlotStatus is not DeploymentSlotProvisioningStatus.Provisioning
            and not DeploymentSlotProvisioningStatus.Failed &&
        IsPackageDeploymentTarget(
            target.AgentId,
            target.ConflictGroup,
            target.Port);

    public static bool IsPackageDeploymentTarget(
        string agentId,
        string? conflictGroup,
        int port) =>
        IsOwl5ActivitySlot(agentId, conflictGroup, port);

    private static bool IsOwl5ActivitySlot(
        string agentId,
        string? conflictGroup,
        int port) =>
        string.Equals(agentId, ActivityAgentId, StringComparison.Ordinal) &&
        string.Equals(
            conflictGroup,
            ActivityConflictGroup,
            StringComparison.Ordinal) &&
        port == ActivityPort;

    public static ServerMemoryGuidance? ResolvePackageDeploymentMemoryGuidance(
        string serverId,
        string agentId,
        string? conflictGroup,
        int port,
        bool packageDeploymentEnabled,
        int? hostTotalMemoryMiB)
    {
        if (!packageDeploymentEnabled ||
            !IsPackageDeploymentTarget(agentId, conflictGroup, port) ||
            hostTotalMemoryMiB is not (>= 1024 and <= 1_048_576))
        {
            return null;
        }

        var recommendedMaximum = RoundDownToMemoryStep(Math.Clamp(
            hostTotalMemoryMiB.Value / 2,
            1024,
            16384));
        var recommendedMinimum = Math.Min(
            recommendedMaximum,
            RoundDownToMemoryStep(Math.Clamp(
                hostTotalMemoryMiB.Value / 8,
                4096,
                8192)));
        return new ServerMemoryGuidance(
            hostTotalMemoryMiB.Value,
            recommendedMinimum,
            recommendedMaximum);
    }

    private static int RoundDownToMemoryStep(int value) => value / 256 * 256;

    public static bool IsValidPublisherAgentId(string? agentId) =>
        agentId is not null && PublisherAgentIdPattern().IsMatch(agentId);

    public static IReadOnlyDictionary<string, string[]> ValidatePublisherHeartbeat(
        PackagePublisherHeartbeatRequest request,
        DateTimeOffset now)
    {
        var errors = new Dictionary<string, string[]>();
        if (!IsValidPublisherAgentId(request.AgentId))
        {
            errors["agentId"] = ["发布代理 ID 无效。"];
        }

        var version = request.AgentVersion?.Trim() ?? string.Empty;
        if (version.Length is < 1 or > 40 || version.Any(char.IsControl))
        {
            errors["agentVersion"] = ["发布代理版本无效。"];
        }

        if (request.CapturedAt < now.AddMinutes(-10) ||
            request.CapturedAt > now.AddMinutes(2))
        {
            errors["capturedAt"] = ["发布代理时钟与 API 相差过大。"];
        }

        if (request.ActiveImportId == Guid.Empty)
        {
            errors["activeImportId"] = ["活动中的发布任务 ID 无效。"];
        }

        return errors;
    }

    public static IReadOnlyDictionary<string, string[]> ValidatePublisherCompletion(
        PackagePublisherCompletionRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (!IsValidPublisherAgentId(request.AgentId))
        {
            errors["agentId"] = ["发布代理 ID 无效。"];
        }

        if (request.AttemptCount is < 1 or > 5)
        {
            errors["attemptCount"] = ["发布任务尝试次数无效。"];
        }

        if (!ResultCodePattern().IsMatch(request.ResultCode ?? string.Empty))
        {
            errors["resultCode"] = ["发布结果代码无效。"];
        }

        var message = request.ResultMessage?.Trim() ?? string.Empty;
        if (message.Length is < 1 or > 2000 || message.Any(char.IsControl))
        {
            errors["resultMessage"] = ["发布结果说明必须为 1 至 2000 个可显示字符。"];
        }

        if (request.UploadedObjects < 0 ||
            request.ExistingObjects < 0 ||
            request.UploadedBytes < 0 ||
            request.UploadedObjects > 200_000 ||
            request.ExistingObjects > 200_000)
        {
            errors["statistics"] = ["发布对象统计无效。"];
        }

        if (request.Outcome == PackagePublisherJobOutcome.Succeeded &&
            string.IsNullOrWhiteSpace(request.ManifestEnvelopeBase64))
        {
            errors["manifestEnvelopeBase64"] = ["成功结果必须包含签名清单。"];
        }

        if (request.Outcome == PackagePublisherJobOutcome.Failed &&
            request.ManifestEnvelopeBase64 is not null)
        {
            errors["manifestEnvelopeBase64"] = ["失败结果不能包含签名清单。"];
        }

        return errors;
    }

    public static IReadOnlyDictionary<string, string[]> ValidatePublisherProgress(
        PackagePublisherProgressRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (!IsValidPublisherAgentId(request.AgentId))
        {
            errors["agentId"] = ["发布代理 ID 无效。"];
        }

        if (request.AttemptCount is < 1 or > 5)
        {
            errors["attemptCount"] = ["发布任务尝试次数无效。"];
        }

        if (request.CompletedObjects < 0 ||
            request.TotalObjects < 0 ||
            request.CompletedObjects > request.TotalObjects ||
            request.TotalObjects > 200_000)
        {
            errors["objects"] = ["发布对象进度无效。"];
        }

        if (request.ProcessedBytes < 0 ||
            request.TotalBytes < 0 ||
            request.ProcessedBytes > request.TotalBytes)
        {
            errors["bytes"] = ["发布字节进度无效。"];
        }

        if (request.Phase == PackagePublisherProgressPhase.DownloadingArchive &&
            request.TotalBytes == 0)
        {
            errors["totalBytes"] = ["下载阶段必须包含归档总大小。"];
        }

        if (request.Phase == PackagePublisherProgressPhase.PublishingObjects &&
            request.TotalObjects == 0)
        {
            errors["totalObjects"] = ["对象发布阶段必须包含对象总数。"];
        }

        return errors;
    }

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

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{1,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex PublisherAgentIdPattern();

    [GeneratedRegex("^[A-Z][A-Z0-9_]{0,79}$", RegexOptions.CultureInvariant)]
    private static partial Regex ResultCodePattern();

    [GeneratedRegex("^[0-9]+\\.[0-9]+\\.[0-9]+(?:[-+][0-9A-Za-z.-]+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();
}

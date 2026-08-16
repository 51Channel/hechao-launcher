using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hechao.Modpack;

public sealed class ModpackDeploymentInspectionService
{
    private const int ReportSchemaVersion = 1;

    private static readonly JsonSerializerOptions ReportJsonOptions = new(
        JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public async Task<ModpackDeploymentReport> InspectAsync(
        string archivePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        var sourcePath = Path.GetFullPath(archivePath);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("找不到要检查的整合包。", sourcePath);
        }

        var extension = Path.GetExtension(sourcePath);
        if (!extension.Equals(".zip", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".mrpack", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("只支持 ZIP 或 MRPACK 整合包。");
        }

        using var archiveLock = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        var archiveBytes = archiveLock.Length;
        var sha256Task = ComputeSha256Async(sourcePath, cancellationToken);
        var analysisTask = ModpackArchiveAnalyzer.InspectAsync(
            sourcePath,
            cancellationToken: cancellationToken);
        await Task.WhenAll(sha256Task, analysisTask);

        var analysis = await analysisTask;
        var checks = BuildChecks(analysis);
        var readiness = checks.Any(check =>
                check.Status == DeploymentCheckStatus.Blocking)
            ? DeploymentReadiness.Blocked
            : checks.Any(check => check.Status == DeploymentCheckStatus.Warning)
                ? DeploymentReadiness.ReviewRequired
                : DeploymentReadiness.Compliant;

        return new ModpackDeploymentReport(
            ReportSchemaVersion,
            DateTimeOffset.UtcNow,
            Path.GetFileName(sourcePath),
            archiveBytes,
            await sha256Task,
            readiness,
            analysis.Layout,
            analysis.Metadata,
            ToSummary(analysis.Client),
            ToSummary(analysis.Server),
            analysis.SharedFileCount,
            analysis.ServerDeployment,
            checks);
    }

    public static async Task WriteJsonReportAsync(
        ModpackDeploymentReport report,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        var fullPath = Path.GetFullPath(destinationPath);
        var parent = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        await using var stream = new FileStream(
            fullPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            64 * 1024,
            FileOptions.Asynchronous);
        await JsonSerializer.SerializeAsync(
            stream,
            report,
            ReportJsonOptions,
            cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public static string SerializeJson(ModpackDeploymentReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return JsonSerializer.Serialize(report, ReportJsonOptions);
    }

    private static IReadOnlyList<ServerDeploymentCheck> BuildChecks(
        ModpackAnalysisResult analysis)
    {
        var checks = new List<ServerDeploymentCheck>();
        var knownCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddCheck(new ServerDeploymentCheck(
            "ARCHIVE_READABLE",
            DeploymentCheckStatus.Passed,
            "归档可以安全读取",
            "ZIP 目录、条目数量、路径和展开大小已通过基础读取检查。"));

        if (analysis.Client is not null)
        {
            AddCheck(new ServerDeploymentCheck(
                "CLIENT_PART_PRESENT",
                DeploymentCheckStatus.Passed,
                "客户端内容已识别",
                $"识别到 {analysis.Client.FileCount} 个客户端文件。"));
        }

        if (analysis.Server is not null)
        {
            AddCheck(new ServerDeploymentCheck(
                "SERVER_PART_PRESENT",
                DeploymentCheckStatus.Passed,
                "服务端内容已识别",
                $"识别到 {analysis.Server.FileCount} 个服务端文件。"));
        }

        if (analysis.ServerDeployment is not null)
        {
            foreach (var check in analysis.ServerDeployment.Checks)
            {
                AddCheck(check);
            }
        }

        foreach (var issue in analysis.Issues)
        {
            if (!knownCodes.Add(issue.Code))
            {
                continue;
            }

            checks.Add(new ServerDeploymentCheck(
                issue.Code,
                issue.Severity == ModpackIssueSeverity.Blocking
                    ? DeploymentCheckStatus.Blocking
                    : DeploymentCheckStatus.Warning,
                GetIssueTitle(issue.Code),
                issue.Message,
                issue.Path));
        }

        return checks;

        void AddCheck(ServerDeploymentCheck check)
        {
            if (knownCodes.Add(check.Code))
            {
                checks.Add(check);
            }
        }
    }

    private static string GetIssueTitle(string code) => code switch
    {
        "PATH_COLLISION" => "归档路径发生冲突",
        "UNSAFE_ARCHIVE_ENTRY" => "归档包含不安全条目",
        "CLIENT_PART_MISSING" => "缺少客户端内容",
        "SERVER_PART_MISSING" => "缺少服务端内容",
        "SHARED_MODS_ASSUMED" => "共享模组需要复核",
        "DESCRIPTOR_INVALID" => "整合包描述文件无效",
        "DESCRIPTOR_UNSUPPORTED" => "整合包描述版本不受支持",
        "CURSEFORGE_REMOTE_FILES" => "CurseForge 远程文件未包含",
        "MODRINTH_REMOTE_FILES" => "Modrinth 远程文件未包含",
        _ => "部署检查发现问题"
    };

    private static ModpackArchivePartSummary? ToSummary(ModpackArchivePart? part) =>
        part is null
            ? null
            : new ModpackArchivePartSummary(part.ExpandedBytes, part.FileCount);

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

}

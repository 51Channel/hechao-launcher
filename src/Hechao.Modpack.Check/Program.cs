using System.Text;
using Hechao.Modpack;

namespace Hechao.Modpack.Check;

public static class Program
{
    private const int CompliantExitCode = 0;
    private const int ReviewRequiredExitCode = 1;
    private const int BlockedExitCode = 2;
    private const int ExecutionErrorExitCode = 3;

    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        try
        {
            var options = ParseArguments(args);
            if (options.ShowHelp)
            {
                WriteUsage();
                return CompliantExitCode;
            }

            using var cancellation = new CancellationTokenSource();
            ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cancellation.Cancel();
            };
            Console.CancelKeyPress += cancelHandler;
            try
            {
                var service = new ModpackDeploymentInspectionService();
                var report = await service.InspectAsync(
                    options.ArchivePath!,
                    cancellation.Token);

                if (options.JsonPath == "-")
                {
                    Console.WriteLine(ModpackDeploymentInspectionService.SerializeJson(report));
                }
                else
                {
                    if (!options.Quiet)
                    {
                        WriteSummary(report);
                    }

                    if (options.JsonPath is not null)
                    {
                        await ModpackDeploymentInspectionService.WriteJsonReportAsync(
                            report,
                            options.JsonPath,
                            cancellation.Token);
                        if (!options.Quiet)
                        {
                            Console.WriteLine($"报告已写入：{Path.GetFullPath(options.JsonPath)}");
                        }
                    }
                }

                return report.Readiness switch
                {
                    DeploymentReadiness.Compliant => CompliantExitCode,
                    DeploymentReadiness.ReviewRequired => ReviewRequiredExitCode,
                    DeploymentReadiness.Blocked => BlockedExitCode,
                    _ => ExecutionErrorExitCode
                };
            }
            finally
            {
                Console.CancelKeyPress -= cancelHandler;
            }
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("检查已取消。");
            return ExecutionErrorExitCode;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"检查失败：{exception.Message}");
            return ExecutionErrorExitCode;
        }
    }

    private static CommandLineOptions ParseArguments(IReadOnlyList<string> args)
    {
        if (args.Count == 0)
        {
            throw new ArgumentException("缺少整合包路径。使用 --help 查看用法。");
        }

        string? archivePath = null;
        string? jsonPath = null;
        var quiet = false;
        for (var index = 0; index < args.Count; index++)
        {
            var argument = args[index];
            if (argument is "-h" or "--help")
            {
                return new CommandLineOptions(null, null, false, true);
            }

            if (argument == "--quiet")
            {
                quiet = true;
                continue;
            }

            if (argument == "--json")
            {
                if (++index >= args.Count)
                {
                    throw new ArgumentException("--json 后必须提供报告路径，或使用 - 输出到标准输出。");
                }

                jsonPath = args[index];
                continue;
            }

            if (argument.StartsWith('-'))
            {
                throw new ArgumentException($"未知参数：{argument}");
            }

            if (archivePath is not null)
            {
                throw new ArgumentException("一次只能检查一个整合包。");
            }

            archivePath = argument;
        }

        if (string.IsNullOrWhiteSpace(archivePath))
        {
            throw new ArgumentException("缺少整合包路径。");
        }

        return new CommandLineOptions(archivePath, jsonPath, quiet, false);
    }

    private static void WriteUsage()
    {
        Console.WriteLine("赫朝整合包部署检查 CLI 0.1.0");
        Console.WriteLine();
        Console.WriteLine("用法：");
        Console.WriteLine("  Hechao.Modpack.Check <整合包.zip|mrpack> [--json <报告.json|->] [--quiet]");
        Console.WriteLine();
        Console.WriteLine("退出码：0=符合标准，1=需要复核，2=禁止部署，3=执行失败");
    }

    private static void WriteSummary(ModpackDeploymentReport report)
    {
        var result = report.Readiness switch
        {
            DeploymentReadiness.Compliant => "符合部署标准",
            DeploymentReadiness.ReviewRequired => "需要人工复核",
            DeploymentReadiness.Blocked => "禁止部署",
            _ => "未知"
        };
        Console.WriteLine($"结果：{result}");
        Console.WriteLine($"归档：{report.ArchiveName}");
        Console.WriteLine($"SHA-256：{report.ArchiveSha256}");
        Console.WriteLine(
            $"元数据：Minecraft {report.Metadata.MinecraftVersion} / {report.Metadata.Loader} {report.Metadata.LoaderVersion}");
        Console.WriteLine(
            $"检查项：{report.BlockingCount} 阻断，{report.WarningCount} 警告，{report.PassedCount} 通过");

        foreach (var check in report.Checks.Where(check =>
                     check.Status != DeploymentCheckStatus.Passed))
        {
            var prefix = check.Status == DeploymentCheckStatus.Blocking ? "阻断" : "警告";
            Console.WriteLine($"[{prefix}] {check.Code} - {check.Title}");
            Console.WriteLine($"       {check.Message}");
            if (!string.IsNullOrWhiteSpace(check.Remediation))
            {
                Console.WriteLine($"       修复：{check.Remediation}");
            }
        }
    }

    private sealed record CommandLineOptions(
        string? ArchivePath,
        string? JsonPath,
        bool Quiet,
        bool ShowHelp);
}

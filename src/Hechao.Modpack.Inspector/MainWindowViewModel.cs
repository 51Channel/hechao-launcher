using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Hechao.Launcher.Controls;
using Hechao.Modpack;

namespace Hechao.Modpack.Inspector;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private static readonly Brush NeutralForeground = CreateBrush("#66696F");
    private static readonly Brush NeutralBackground = CreateBrush("#F1F2F3");
    private static readonly Brush SuccessForeground = CreateBrush("#18794E");
    private static readonly Brush SuccessBackground = CreateBrush("#ECF8F2");
    private static readonly Brush WarningForeground = CreateBrush("#9A5B00");
    private static readonly Brush WarningBackground = CreateBrush("#FFF7E8");
    private static readonly Brush ErrorForeground = CreateBrush("#B3261E");
    private static readonly Brush ErrorBackground = CreateBrush("#FFF1F0");

    private readonly ModpackDeploymentInspectionService inspectionService = new();
    private readonly ObservableCollection<CheckItemViewModel> checks = [];
    private CancellationTokenSource? inspectionCancellation;
    private int inspectionGeneration;
    private bool isBusy;
    private string? selectedArchivePath;
    private string? errorMessage;
    private string statusText = "等待选择整合包";
    private string activeFilter = "All";
    private ModpackDeploymentReport? report;

    public MainWindowViewModel()
    {
        FilteredChecks = CollectionViewSource.GetDefaultView(checks);
        FilteredChecks.Filter = FilterCheck;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICollectionView FilteredChecks { get; }

    public ModpackDeploymentReport? Report
    {
        get => report;
        private set
        {
            if (SetField(ref report, value))
            {
                NotifyReportProperties();
            }
        }
    }

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetField(ref isBusy, value))
            {
                OnPropertyChanged(nameof(CanSelectArchive));
                OnPropertyChanged(nameof(CanReinspect));
                OnPropertyChanged(nameof(CanExport));
                OnPropertyChanged(nameof(BusyVisibility));
            }
        }
    }

    public bool CanSelectArchive => !IsBusy;
    public bool CanReinspect => !IsBusy && selectedArchivePath is not null;
    public bool CanExport => !IsBusy && Report is not null;
    public Visibility BusyVisibility => IsBusy ? Visibility.Visible : Visibility.Collapsed;
    public Visibility EmptyStateVisibility =>
        !IsBusy && Report is null && errorMessage is null
            ? Visibility.Visible
            : Visibility.Collapsed;

    public string ArchivePromptTitle => selectedArchivePath is null
        ? "拖入整合包，或从本机选择文件"
        : Path.GetFileName(selectedArchivePath);

    public string ArchivePathText => selectedArchivePath ?? "尚未选择文件";
    public string StatusText
    {
        get => statusText;
        private set => SetField(ref statusText, value);
    }

    public string ResultTitle => errorMessage is not null
        ? "检查失败"
        : Report?.Readiness switch
        {
            DeploymentReadiness.Compliant => "符合部署标准",
            DeploymentReadiness.ReviewRequired => "需要人工复核",
            DeploymentReadiness.Blocked => "禁止部署",
            _ => "等待检查"
        };

    public string ResultSubtitle => errorMessage ?? Report?.Readiness switch
    {
        DeploymentReadiness.Compliant => "未发现阻断项或需要复核的配置。",
        DeploymentReadiness.ReviewRequired => "没有阻断项，但警告应在上传前逐项确认。",
        DeploymentReadiness.Blocked => "存在会导致部署失败、安全边界失效或启动核心错误的问题。",
        _ => "选择 ZIP 或 MRPACK 后，将按后台当前规则进行检查。"
    };

    public Brush ResultForeground => errorMessage is not null
        ? ErrorForeground
        : Report?.Readiness switch
        {
            DeploymentReadiness.Compliant => SuccessForeground,
            DeploymentReadiness.ReviewRequired => WarningForeground,
            DeploymentReadiness.Blocked => ErrorForeground,
            _ => NeutralForeground
        };

    public Brush ResultBackground => errorMessage is not null
        ? ErrorBackground
        : Report?.Readiness switch
        {
            DeploymentReadiness.Compliant => SuccessBackground,
            DeploymentReadiness.ReviewRequired => WarningBackground,
            DeploymentReadiness.Blocked => ErrorBackground,
            _ => NeutralBackground
        };

    public IconParkKind ResultIcon => errorMessage is not null
        ? IconParkKind.Close
        : Report?.Readiness switch
        {
            DeploymentReadiness.Compliant => IconParkKind.CheckOne,
            DeploymentReadiness.ReviewRequired => IconParkKind.Remind,
            DeploymentReadiness.Blocked => IconParkKind.Close,
            _ => IconParkKind.Shield
        };

    public string CheckCountSummary => Report is null
        ? "0 项"
        : $"{Report.BlockingCount} 阻断 · {Report.WarningCount} 警告 · {Report.PassedCount} 通过";

    public string ReportArchiveName => Report is null
        ? "尚未检查"
        : $"{Report.ArchiveName}  ·  {FormatBytes(Report.ArchiveBytes)}";

    public string ProfileVersionText => Report is null
        ? "-"
        : $"{ValueOrUnknown(Report.Metadata.SuggestedProfileId)} / {ValueOrUnknown(Report.Metadata.Version)}";

    public string RuntimeText => Report is null
        ? "-"
        : $"Minecraft {ValueOrUnknown(Report.Metadata.MinecraftVersion)} / {ValueOrUnknown(Report.Metadata.Loader)} {Report.Metadata.LoaderVersion}".Trim();

    public string PartSummaryText => Report is null
        ? "-"
        : $"{FormatPart(Report.Client)} / {FormatPart(Report.Server)}";

    public string CoreSummaryText
    {
        get
        {
            var deployment = Report?.ServerDeployment;
            if (deployment is null)
            {
                return "-";
            }

            var declared = string.IsNullOrWhiteSpace(deployment.DeclaredCore)
                ? "未声明"
                : deployment.DeclaredCore;
            return $"{declared} / {deployment.LaunchCore}";
        }
    }

    public string LaunchCommandText =>
        Report?.ServerDeployment?.LaunchCommand ?? "未识别到 Java 启动命令";

    public string ArchiveSha256Text => Report?.ArchiveSha256 ?? "-";

    public string GuidanceText => errorMessage is not null
        ? "请确认文件仍存在、未被其他程序独占，并且归档没有损坏。"
        : Report?.Readiness switch
        {
            DeploymentReadiness.Compliant => "可以进入后台上传与人工确认流程。静态检查通过不代替首次真实启动和进服验收。",
            DeploymentReadiness.ReviewRequired => "先处理或确认所有警告，再决定是否上传；导出的 JSON 可随整合包一起留档。",
            DeploymentReadiness.Blocked => "不要上传或部署。修复阻断项后重新打包，并再次运行本检查器。",
            _ => "检查器只读取归档，不会修改文件，也不会连接或控制服务器。"
        };

    public async Task InspectAsync(string archivePath)
    {
        var generation = Interlocked.Increment(ref inspectionGeneration);
        inspectionCancellation?.Cancel();
        inspectionCancellation?.Dispose();
        inspectionCancellation = new CancellationTokenSource();
        var cancellationToken = inspectionCancellation.Token;

        selectedArchivePath = Path.GetFullPath(archivePath);
        errorMessage = null;
        Report = null;
        checks.Clear();
        NotifySelectionProperties();
        IsBusy = true;
        StatusText = $"正在检查 {Path.GetFileName(selectedArchivePath)}";

        try
        {
            var inspection = await Task.Run(
                () => inspectionService.InspectAsync(
                    selectedArchivePath,
                    cancellationToken),
                cancellationToken);
            if (generation != inspectionGeneration)
            {
                return;
            }

            foreach (var check in inspection.Checks
                         .OrderBy(check => check.Status switch
                         {
                             DeploymentCheckStatus.Blocking => 0,
                             DeploymentCheckStatus.Warning => 1,
                             _ => 2
                         }))
            {
                checks.Add(new CheckItemViewModel(check));
            }

            Report = inspection;
            FilteredChecks.Refresh();
            StatusText = $"检查完成 · {inspection.InspectedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}";
        }
        catch (OperationCanceledException) when (generation != inspectionGeneration)
        {
        }
        catch (Exception exception)
        {
            if (generation != inspectionGeneration)
            {
                return;
            }

            errorMessage = exception.Message;
            StatusText = "检查失败";
            NotifyReportProperties();
        }
        finally
        {
            if (generation == inspectionGeneration)
            {
                IsBusy = false;
                OnPropertyChanged(nameof(EmptyStateVisibility));
            }
        }
    }

    public Task ReinspectAsync() => selectedArchivePath is null
        ? Task.CompletedTask
        : InspectAsync(selectedArchivePath);

    public async Task ExportAsync(string destinationPath)
    {
        if (Report is null)
        {
            throw new InvalidOperationException("当前没有可导出的检查报告。");
        }

        await ModpackDeploymentInspectionService.WriteJsonReportAsync(
            Report,
            destinationPath);
        StatusText = $"报告已导出到 {destinationPath}";
    }

    public void SetFilter(string filter)
    {
        activeFilter = filter;
        FilteredChecks.Refresh();
    }

    private bool FilterCheck(object item)
    {
        if (item is not CheckItemViewModel check || activeFilter == "All")
        {
            return true;
        }

        return string.Equals(
            check.Status.ToString(),
            activeFilter,
            StringComparison.OrdinalIgnoreCase);
    }

    private void NotifySelectionProperties()
    {
        OnPropertyChanged(nameof(ArchivePromptTitle));
        OnPropertyChanged(nameof(ArchivePathText));
        OnPropertyChanged(nameof(CanReinspect));
        OnPropertyChanged(nameof(EmptyStateVisibility));
    }

    private void NotifyReportProperties()
    {
        OnPropertyChanged(nameof(CanExport));
        OnPropertyChanged(nameof(ResultTitle));
        OnPropertyChanged(nameof(ResultSubtitle));
        OnPropertyChanged(nameof(ResultForeground));
        OnPropertyChanged(nameof(ResultBackground));
        OnPropertyChanged(nameof(ResultIcon));
        OnPropertyChanged(nameof(CheckCountSummary));
        OnPropertyChanged(nameof(ReportArchiveName));
        OnPropertyChanged(nameof(ProfileVersionText));
        OnPropertyChanged(nameof(RuntimeText));
        OnPropertyChanged(nameof(PartSummaryText));
        OnPropertyChanged(nameof(CoreSummaryText));
        OnPropertyChanged(nameof(LaunchCommandText));
        OnPropertyChanged(nameof(ArchiveSha256Text));
        OnPropertyChanged(nameof(GuidanceText));
        OnPropertyChanged(nameof(EmptyStateVisibility));
    }

    private static string FormatPart(ModpackArchivePartSummary? part) => part is null
        ? "未识别"
        : $"{part.FileCount} 文件 · {FormatBytes(part.ExpandedBytes)}";

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KiB", "MiB", "GiB", "TiB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }

    private static string ValueOrUnknown(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "未识别" : value;

    private static Brush CreateBrush(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class CheckItemViewModel
{
    public CheckItemViewModel(ServerDeploymentCheck check)
    {
        Code = check.Code;
        Status = check.Status;
        Title = check.Title;
        Message = check.Path is null
            ? check.Message
            : $"{check.Message}  ·  {check.Path}";
        Remediation = check.Remediation ?? string.Empty;
    }

    public string Code { get; }
    public DeploymentCheckStatus Status { get; }
    public string Title { get; }
    public string Message { get; }
    public string Remediation { get; }
    public Visibility RemediationVisibility => string.IsNullOrWhiteSpace(Remediation)
        ? Visibility.Collapsed
        : Visibility.Visible;
}

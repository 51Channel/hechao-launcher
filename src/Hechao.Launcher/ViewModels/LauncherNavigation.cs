using System.Diagnostics;
using Hechao.Distribution;
using Hechao.Launcher.Infrastructure;

namespace Hechao.Launcher.ViewModels;

public enum LauncherPage
{
    Servers,
    Downloads,
    Activities,
    Account,
    Settings
}

public enum DownloadJobStatus
{
    Running,
    Completed,
    Failed,
    Canceled
}

public sealed class DownloadJobViewModel : ObservableObject
{
    private double _percent;
    private long _completedBytes;
    private long _totalBytes;
    private string _currentFile = string.Empty;
    private DownloadJobStatus _status;
    private DateTimeOffset? _completedAt;
    private string? _failureMessage;
    private ClientInstallPhase? _phase;
    private readonly Stopwatch _speedClock = Stopwatch.StartNew();
    private long _lastSpeedSampleBytes;
    private TimeSpan _lastSpeedSampleAt;
    private double _bytesPerSecond;

    public DownloadJobViewModel(
        Guid id,
        string profileId,
        string displayName,
        string version,
        DateTimeOffset startedAt,
        DownloadJobStatus status,
        long completedBytes,
        long totalBytes,
        string currentFile,
        DateTimeOffset? completedAt = null,
        string? failureMessage = null,
        ClientInstallPhase? phase = null)
    {
        Id = id;
        ProfileId = profileId;
        DisplayName = displayName;
        Version = version;
        StartedAt = startedAt;
        _status = status;
        _completedBytes = completedBytes;
        _totalBytes = totalBytes;
        _currentFile = currentFile;
        _completedAt = completedAt;
        _failureMessage = failureMessage;
        _phase = phase;
        _percent = CalculatePercent(completedBytes, totalBytes, status);
        _lastSpeedSampleBytes = completedBytes;
    }

    public Guid Id { get; }
    public string ProfileId { get; }
    public string DisplayName { get; }
    public string Version { get; }
    public DateTimeOffset StartedAt { get; }
    public DateTimeOffset? CompletedAt => _completedAt;
    public double Percent => _percent;
    public long CompletedBytes => _completedBytes;
    public long TotalBytes => _totalBytes;
    public string CurrentFile => _currentFile;
    public DownloadJobStatus Status => _status;
    public string? FailureMessage => _failureMessage;
    public double BytesPerSecond => _bytesPerSecond;
    public ClientInstallPhase? Phase => _phase;

    public string StatusText => Status switch
    {
        DownloadJobStatus.Running => Phase switch
        {
            ClientInstallPhase.Checking => "正在检查本地文件",
            ClientInstallPhase.Downloading => "正在增量下载",
            ClientInstallPhase.Staging => "正在准备客户端",
            ClientInstallPhase.Switching => "正在切换版本",
            ClientInstallPhase.PreparingRuntime => "正在准备配套 Java",
            ClientInstallPhase.Complete => "客户端已就绪",
            _ => "正在准备"
        },
        DownloadJobStatus.Completed => "已完成",
        DownloadJobStatus.Canceled => "已取消",
        _ => "未完成"
    };

    public string ProgressText => Status switch
    {
        DownloadJobStatus.Running when Phase == ClientInstallPhase.Checking =>
            TotalBytes <= 0
                ? "正在检查本地文件"
                : $"已检查 {FormatBytes(CompletedBytes)} / {FormatBytes(TotalBytes)}",
        DownloadJobStatus.Running when Phase == ClientInstallPhase.Downloading =>
            TotalBytes <= 0
                ? "正在计算增量大小"
                : BytesPerSecond > 0
                    ? $"{FormatBytes(CompletedBytes)} / {FormatBytes(TotalBytes)} · " +
                      $"{FormatBytes((long)BytesPerSecond)}/s"
                    : $"{FormatBytes(CompletedBytes)} / {FormatBytes(TotalBytes)}",
        DownloadJobStatus.Running when Phase == ClientInstallPhase.Staging => "正在准备客户端文件",
        DownloadJobStatus.Running when Phase == ClientInstallPhase.Switching => "正在安全切换版本",
        DownloadJobStatus.Running when Phase == ClientInstallPhase.PreparingRuntime => "正在准备配套 Java",
        DownloadJobStatus.Completed => "增量安装已完成",
        _ when TotalBytes <= 0 => $"{Percent:0}%",
        _ => $"{FormatBytes(CompletedBytes)} / {FormatBytes(TotalBytes)}"
    };

    public string TimeText => (CompletedAt ?? StartedAt).ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    public void Update(
        ClientInstallPhase phase,
        double percent,
        long completedBytes,
        long totalBytes,
        string currentFile)
    {
        var phaseChanged = _phase != phase;
        _phase = phase;
        _percent = Math.Clamp(percent, 0, 100);
        _completedBytes = Math.Max(0, completedBytes);
        _totalBytes = Math.Max(0, totalBytes);
        _currentFile = currentFile;
        if (phaseChanged)
        {
            ResetSpeed(_completedBytes);
        }
        else if (phase == ClientInstallPhase.Downloading)
        {
            UpdateSpeed(_completedBytes);
        }
        else
        {
            _bytesPerSecond = 0;
        }
        OnPropertyChanged(nameof(Phase));
        OnPropertyChanged(nameof(Percent));
        OnPropertyChanged(nameof(CompletedBytes));
        OnPropertyChanged(nameof(TotalBytes));
        OnPropertyChanged(nameof(CurrentFile));
        OnPropertyChanged(nameof(BytesPerSecond));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(ProgressText));
    }

    public void Finish(DownloadJobStatus status, string? failureMessage = null)
    {
        _status = status;
        _failureMessage = failureMessage;
        _completedAt = DateTimeOffset.UtcNow;
        _bytesPerSecond = 0;
        if (status == DownloadJobStatus.Completed)
        {
            _percent = 100;
            _completedBytes = Math.Max(_completedBytes, _totalBytes);
        }

        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(FailureMessage));
        OnPropertyChanged(nameof(CompletedAt));
        OnPropertyChanged(nameof(Percent));
        OnPropertyChanged(nameof(BytesPerSecond));
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(TimeText));
    }

    private void UpdateSpeed(long completedBytes)
    {
        var now = _speedClock.Elapsed;
        var elapsed = now - _lastSpeedSampleAt;
        if (completedBytes < _lastSpeedSampleBytes)
        {
            _lastSpeedSampleBytes = completedBytes;
            _lastSpeedSampleAt = now;
            _bytesPerSecond = 0;
            return;
        }

        if (elapsed < TimeSpan.FromMilliseconds(500))
        {
            return;
        }

        var bytesSinceLastSample = completedBytes - _lastSpeedSampleBytes;
        var currentBytesPerSecond = elapsed.TotalSeconds <= 0
            ? 0
            : bytesSinceLastSample / elapsed.TotalSeconds;
        _bytesPerSecond = currentBytesPerSecond <= 0
            ? _bytesPerSecond
            : _bytesPerSecond <= 0
                ? currentBytesPerSecond
                : _bytesPerSecond * 0.65 + currentBytesPerSecond * 0.35;
        _lastSpeedSampleBytes = completedBytes;
        _lastSpeedSampleAt = now;
    }

    private void ResetSpeed(long completedBytes)
    {
        _lastSpeedSampleBytes = completedBytes;
        _lastSpeedSampleAt = _speedClock.Elapsed;
        _bytesPerSecond = 0;
    }

    private static double CalculatePercent(
        long completedBytes,
        long totalBytes,
        DownloadJobStatus status)
    {
        if (status == DownloadJobStatus.Completed)
        {
            return 100;
        }

        return totalBytes <= 0
            ? 0
            : Math.Clamp(completedBytes * 100d / totalBytes, 0, 100);
    }

    private static string FormatBytes(long bytes)
    {
        const double kibibyte = 1024d;
        const double mebibyte = 1024d * kibibyte;
        const double gibibyte = 1024d * mebibyte;
        return bytes >= gibibyte
            ? $"{bytes / gibibyte:0.##} GB"
            : bytes >= mebibyte
                ? $"{bytes / mebibyte:0.#} MB"
                : bytes >= kibibyte
                    ? $"{bytes / kibibyte:0.#} KB"
                    : $"{bytes} B";
    }
}

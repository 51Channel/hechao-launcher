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
        string? failureMessage = null)
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
        _percent = CalculatePercent(completedBytes, totalBytes, status);
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

    public string StatusText => Status switch
    {
        DownloadJobStatus.Running => "正在下载",
        DownloadJobStatus.Completed => "已完成",
        DownloadJobStatus.Canceled => "已取消",
        _ => "未完成"
    };

    public string ProgressText => TotalBytes <= 0
        ? $"{Percent:0}%"
        : $"{FormatBytes(CompletedBytes)} / {FormatBytes(TotalBytes)}";

    public string TimeText => (CompletedAt ?? StartedAt).ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    public void Update(double percent, long completedBytes, long totalBytes, string currentFile)
    {
        _percent = Math.Clamp(percent, 0, 100);
        _completedBytes = Math.Max(0, completedBytes);
        _totalBytes = Math.Max(0, totalBytes);
        _currentFile = currentFile;
        OnPropertyChanged(nameof(Percent));
        OnPropertyChanged(nameof(CompletedBytes));
        OnPropertyChanged(nameof(TotalBytes));
        OnPropertyChanged(nameof(CurrentFile));
        OnPropertyChanged(nameof(ProgressText));
    }

    public void Finish(DownloadJobStatus status, string? failureMessage = null)
    {
        _status = status;
        _failureMessage = failureMessage;
        _completedAt = DateTimeOffset.UtcNow;
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
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(TimeText));
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

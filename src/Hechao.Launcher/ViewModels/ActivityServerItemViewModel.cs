using Hechao.Contracts;
using Hechao.Distribution;
using Hechao.Launcher.Controls;
using Hechao.Launcher.Infrastructure;

namespace Hechao.Launcher.ViewModels;

public sealed class ActivityServerItemViewModel : ObservableObject
{
    private LocalProfileState _localProfileState = LocalProfileState.Missing;
    private bool _isClientStateChecked;
    private bool _isClientProfileAvailable = true;

    public ActivityServerItemViewModel(ServerSummary server)
    {
        Server = server ?? throw new ArgumentNullException(nameof(server));
    }

    public ServerSummary Server { get; }

    public string Id => Server.Id;

    public string Name => Server.Name;

    public string ShortName => Server.ShortName;

    public string IconGlyph => Server.IconGlyph;

    public ServerStatus Status => Server.Status;

    public int OnlinePlayers => Server.OnlinePlayers;

    public int MaxPlayers => Server.MaxPlayers;

    public string MinecraftVersion => Server.MinecraftVersion;

    public ModLoaderKind Loader => Server.Loader;

    public string AnnouncementText => string.IsNullOrWhiteSpace(Server.Announcement)
        ? "暂无活动公告"
        : Server.Announcement;

    public string ScheduleText => ServerCatalogPresentation.FormatSchedule(
        Server.OpensAt,
        Server.ClosesAt);

    public LocalProfileState LocalProfileState => _localProfileState;

    public bool IsClientStateChecked => _isClientStateChecked;

    public bool IsClientProfileAvailable => _isClientProfileAvailable;

    public bool IsClientInstalled =>
        IsClientProfileAvailable &&
        IsClientStateChecked &&
        LocalProfileState != LocalProfileState.Missing;

    public bool CanPrepareClient => IsClientProfileAvailable;

    public string ClientStateText => !IsClientProfileAvailable
        ? "客户端暂未发布"
        : !IsClientStateChecked
            ? "客户端状态待检查"
            : LocalProfileState switch
            {
                LocalProfileState.Ready => "客户端已准备",
                LocalProfileState.UpdateRequired => "客户端有可用更新",
                _ => "客户端尚未下载",
            };

    public string ClientActionText => !IsClientProfileAvailable
        ? "客户端暂未发布"
        : !IsClientStateChecked
            ? "检查客户端"
            : LocalProfileState switch
            {
                LocalProfileState.Ready => "在服务器主页查看",
                LocalProfileState.UpdateRequired => "更新活动客户端",
                _ => "下载活动客户端",
            };

    public string ClientActionAutomationName => $"{ClientActionText}：{Name}";

    public string ClientActionHint => !IsClientProfileAvailable
        ? "该活动尚未提供可下载的客户端档案"
        : LocalProfileState == LocalProfileState.Ready && IsClientStateChecked
            ? "客户端已准备，打开服务器主页"
            : "使用赫朝签名清单下载并准备活动客户端";

    public IconParkKind ClientActionIcon =>
        IsClientStateChecked && LocalProfileState == LocalProfileState.Ready
            ? IconParkKind.Right
            : IsClientStateChecked
                ? IconParkKind.Download
                : IconParkKind.Refresh;

    internal void ApplyClientState(LocalProfileState state)
    {
        _localProfileState = state;
        _isClientStateChecked = true;
        _isClientProfileAvailable = true;
        NotifyClientStateChanged();
    }

    internal void MarkClientStateCheckFailed()
    {
        _localProfileState = LocalProfileState.Missing;
        _isClientStateChecked = false;
        _isClientProfileAvailable = true;
        NotifyClientStateChanged();
    }

    internal void MarkClientProfileUnavailable()
    {
        _localProfileState = LocalProfileState.Missing;
        _isClientStateChecked = true;
        _isClientProfileAvailable = false;
        NotifyClientStateChanged();
    }

    internal void ResetClientState()
    {
        _localProfileState = LocalProfileState.Missing;
        _isClientStateChecked = false;
        _isClientProfileAvailable = true;
        NotifyClientStateChanged();
    }

    private void NotifyClientStateChanged()
    {
        OnPropertyChanged(nameof(LocalProfileState));
        OnPropertyChanged(nameof(IsClientStateChecked));
        OnPropertyChanged(nameof(IsClientProfileAvailable));
        OnPropertyChanged(nameof(IsClientInstalled));
        OnPropertyChanged(nameof(CanPrepareClient));
        OnPropertyChanged(nameof(ClientStateText));
        OnPropertyChanged(nameof(ClientActionText));
        OnPropertyChanged(nameof(ClientActionAutomationName));
        OnPropertyChanged(nameof(ClientActionHint));
        OnPropertyChanged(nameof(ClientActionIcon));
    }
}

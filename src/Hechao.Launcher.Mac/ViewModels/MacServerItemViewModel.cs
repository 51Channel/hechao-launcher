using Hechao.Contracts;
using Hechao.Launcher.Infrastructure;

namespace Hechao.Launcher.Mac.ViewModels;

public sealed class MacServerItemViewModel(ServerSummary server)
    : ObservableObject
{
    private bool _isSelected;

    public ServerSummary Server { get; } = server;
    public string Id => Server.Id;
    public string Name => Server.Name;
    public string ShortName => string.IsNullOrWhiteSpace(Server.ShortName)
        ? Server.Name[..1]
        : Server.ShortName;
    public string Announcement => string.IsNullOrWhiteSpace(Server.Announcement)
        ? "暂无服务器公告。"
        : Server.Announcement;
    public string StatusText => Server.Status switch
    {
        ServerStatus.Online => Server.CanJoin ? "在线" : "暂未获得进服权限",
        ServerStatus.Maintenance => "维护中",
        _ => "已关闭"
    };
    public string StatusColor => Server.Status switch
    {
        ServerStatus.Online when Server.CanJoin => "#34865A",
        ServerStatus.Maintenance => "#D39128",
        _ => "#979A94"
    };
    public string PopulationText =>
        $"{Server.OnlinePlayers}/{Server.MaxPlayers} 人";
    public string RuntimeText =>
        $"Minecraft {Server.MinecraftVersion} · {Server.Loader}";
    public string ScheduleText => FormatSchedule(Server.OpensAt, Server.ClosesAt);
    public bool IsActivity => Server.CatalogSection switch
    {
        ServerCatalogSection.Activity => true,
        ServerCatalogSection.Permanent => false,
        _ => !string.Equals(Server.Id, "survival2", StringComparison.OrdinalIgnoreCase)
    };
    public bool IsSelected
    {
        get => _isSelected;
        internal set => SetProperty(ref _isSelected, value);
    }

    private static string FormatSchedule(
        DateTimeOffset? opensAt,
        DateTimeOffset? closesAt)
    {
        if (opensAt is null && closesAt is null)
        {
            return "开放时间待定";
        }

        if (opensAt is not null && closesAt is not null)
        {
            var start = opensAt.Value.ToLocalTime();
            var end = closesAt.Value.ToLocalTime();
            return start.Date == end.Date
                ? $"{start:M月d日 HH:mm} - {end:HH:mm}"
                : $"{start:M月d日 HH:mm} - {end:M月d日 HH:mm}";
        }

        return opensAt is not null
            ? $"{opensAt.Value.ToLocalTime():M月d日 HH:mm} 开放"
            : $"开放至 {closesAt!.Value.ToLocalTime():M月d日 HH:mm}";
    }
}

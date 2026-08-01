using Hechao.Contracts;

namespace Hechao.Launcher.ViewModels;

public sealed class ActivityServerItemViewModel
{
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

    public string ScheduleText => FormatSchedule(Server.OpensAt, Server.ClosesAt);

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
            return $"本地时间 {FormatLocalTime(opensAt.Value)} - {FormatLocalTime(closesAt.Value)}";
        }

        return opensAt is not null
            ? $"本地时间 {FormatLocalTime(opensAt.Value)} 开放"
            : $"开放至本地时间 {FormatLocalTime(closesAt!.Value)}";
    }

    private static string FormatLocalTime(DateTimeOffset value) =>
        value.ToLocalTime().ToString("M月d日 HH:mm");
}

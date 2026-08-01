using Hechao.Contracts;
using Hechao.Launcher.ViewModels;

namespace Hechao.Launcher.Tests;

public sealed class ActivityServerItemViewModelTests
{
    [Fact]
    public void ScheduleText_UsesLocalTimeAndAnnouncementFallback()
    {
        var opensAt = new DateTimeOffset(2026, 8, 2, 10, 0, 0, TimeSpan.Zero);
        var closesAt = opensAt.AddHours(3);
        var item = new ActivityServerItemViewModel(CreateServer(opensAt, closesAt, null));

        Assert.Contains(opensAt.ToLocalTime().ToString("M月d日 HH:mm"), item.ScheduleText);
        Assert.Contains(closesAt.ToLocalTime().ToString("M月d日 HH:mm"), item.ScheduleText);
        Assert.Equal("暂无活动公告", item.AnnouncementText);
    }

    [Fact]
    public void ScheduleText_ExplainsWhenNoScheduleExists()
    {
        var item = new ActivityServerItemViewModel(CreateServer(null, null, "今晚开放"));

        Assert.Equal("开放时间待定", item.ScheduleText);
        Assert.Equal("今晚开放", item.AnnouncementText);
    }

    private static ServerSummary CreateServer(
        DateTimeOffset? opensAt,
        DateTimeOffset? closesAt,
        string? announcement) =>
        new(
            "activity-test",
            "活动测试服",
            "活",
            "活",
            ServerStatus.Maintenance,
            0,
            30,
            "1.21.11",
            ModLoaderKind.NeoForge,
            AccessTier.Member,
            "activity-profile",
            announcement ?? string.Empty,
            opensAt,
            closesAt,
            ServerCatalogSection.Activity);
}

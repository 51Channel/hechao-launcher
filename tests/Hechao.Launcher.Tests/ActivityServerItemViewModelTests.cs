using Hechao.Contracts;
using Hechao.Distribution;
using Hechao.Launcher.Controls;
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

    [Fact]
    public void ClientAction_TracksMissingUpdateAndReadyStates()
    {
        var item = new ActivityServerItemViewModel(
            CreateServer(null, null, "今晚开放"));

        Assert.Equal("检查客户端", item.ClientActionText);
        Assert.Equal(IconParkKind.Refresh, item.ClientActionIcon);
        Assert.False(item.IsClientInstalled);

        item.ApplyClientState(LocalProfileState.Missing);
        Assert.Equal("下载活动客户端", item.ClientActionText);
        Assert.Equal(IconParkKind.Download, item.ClientActionIcon);
        Assert.False(item.IsClientInstalled);

        item.ApplyClientState(LocalProfileState.UpdateRequired);
        Assert.Equal("更新活动客户端", item.ClientActionText);
        Assert.True(item.IsClientInstalled);

        item.ApplyClientState(LocalProfileState.Ready);
        Assert.Equal("在服务器主页查看", item.ClientActionText);
        Assert.Equal(IconParkKind.Right, item.ClientActionIcon);
        Assert.True(item.IsClientInstalled);
    }

    [Fact]
    public void MissingCatalogProfile_DisablesClientPreparation()
    {
        var item = new ActivityServerItemViewModel(
            CreateServer(null, null, null));

        item.MarkClientProfileUnavailable();

        Assert.False(item.CanPrepareClient);
        Assert.Equal("客户端暂未发布", item.ClientActionText);
        Assert.False(item.IsClientInstalled);
    }

    [Fact]
    public void JoinRestrictedActivityStillOffersClientPreparation()
    {
        var item = new ActivityServerItemViewModel(
            CreateServer(null, null, null) with
            {
                MinimumTier = AccessTier.Participant,
                CanJoin = false,
            });

        item.ApplyClientState(LocalProfileState.Missing);

        Assert.True(item.CanPrepareClient);
        Assert.Equal("下载活动客户端", item.ClientActionText);
        Assert.Contains("活动成员", item.AccessText, StringComparison.Ordinal);
        Assert.Contains("可提前下载", item.AccessText, StringComparison.Ordinal);
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

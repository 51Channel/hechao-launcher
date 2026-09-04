#if DEBUG
using Hechao.Launcher.Development;
using Hechao.Launcher.Services;
using Hechao.Launcher.ViewModels;

namespace Hechao.Launcher.Tests;

public sealed class LauncherUiPreviewTests
{
    [Fact]
    public void TryGetRequestedPage_DefaultsToServersWhenArgumentIsMissing()
    {
        var requested = LauncherUiPreview.TryGetRequestedPage(
            ["--ui-preview=dark"],
            out var page);

        Assert.False(requested);
        Assert.Equal(LauncherPage.Servers, page);
    }

    [Theory]
    [InlineData("servers", LauncherPage.Servers)]
    [InlineData("downloads", LauncherPage.Downloads)]
    [InlineData("ACTIVITIES", LauncherPage.Activities)]
    [InlineData(" Account ", LauncherPage.Account)]
    [InlineData("settings", LauncherPage.Settings)]
    public void TryGetRequestedPage_ParsesSupportedPages(
        string value,
        LauncherPage expected)
    {
        var requested = LauncherUiPreview.TryGetRequestedPage(
            [$"--ui-preview-page={value}"],
            out var page);

        Assert.True(requested);
        Assert.Equal(expected, page);
    }

    [Fact]
    public void TryGetRequestedPage_RejectsUnsupportedPages()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            LauncherUiPreview.TryGetRequestedPage(
                ["--ui-preview-page=home"],
                out _));

        Assert.Contains(
            "servers, downloads, activities, account, or settings",
            exception.Message);
    }

    [Fact]
    public void TryGetRequestedSettingsTab_DefaultsToGameWhenArgumentIsMissing()
    {
        var requested = LauncherUiPreview.TryGetRequestedSettingsTab(
            ["--ui-preview=dark"],
            out var selectedIndex);

        Assert.False(requested);
        Assert.Equal(0, selectedIndex);
    }

    [Theory]
    [InlineData("game", 0)]
    [InlineData("CLIENT", 1)]
    [InlineData(" behavior ", 2)]
    [InlineData("diagnostics", 3)]
    public void TryGetRequestedSettingsTab_ParsesSupportedTabs(
        string value,
        int expectedIndex)
    {
        var requested = LauncherUiPreview.TryGetRequestedSettingsTab(
            [$"--ui-preview-settings-tab={value}"],
            out var selectedIndex);

        Assert.True(requested);
        Assert.Equal(expectedIndex, selectedIndex);
    }

    [Fact]
    public void TryGetRequestedSettingsTab_RejectsUnsupportedTabs()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            LauncherUiPreview.TryGetRequestedSettingsTab(
                ["--ui-preview-settings-tab=network"],
                out _));

        Assert.Contains(
            "game, client, behavior, or diagnostics",
            exception.Message);
    }

    [Fact]
    public void TryGetScreenshotRequest_DefaultsToLauncherWindowSize()
    {
        var requested = LauncherUiPreview.TryGetScreenshotRequest(
            [$"--ui-preview-screenshot={Path.Combine(Path.GetTempPath(), "preview.png")}"],
            out var request);

        Assert.True(requested);
        Assert.Equal(1200, request.Width);
        Assert.Equal(720, request.Height);
    }

    [Theory]
    [InlineData("dark", true)]
    [InlineData("LIGHT", false)]
    public void TryGetScreenshotRequest_ParsesRuntimeThemeSwitch(
        string value,
        bool expectedUseDarkMode)
    {
        var requested = LauncherUiPreview.TryGetScreenshotRequest(
            [
                $"--ui-preview-screenshot={Path.Combine(Path.GetTempPath(), "preview.png")}",
                $"--ui-preview-switch-theme={value}"
            ],
            out var request);

        Assert.True(requested);
        Assert.Equal(expectedUseDarkMode, request.UseDarkModeAfterRender);
    }

    [Fact]
    public void TryGetScreenshotRequest_RejectsUnsupportedRuntimeTheme()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            LauncherUiPreview.TryGetScreenshotRequest(
                [
                    $"--ui-preview-screenshot={Path.Combine(Path.GetTempPath(), "preview.png")}",
                    "--ui-preview-switch-theme=sepia"
                ],
                out _));

        Assert.Contains("dark or light", exception.Message);
    }

    [Theory]
    [InlineData(LauncherPage.Downloads, "下载中心")]
    [InlineData(LauncherPage.Activities, "活动")]
    [InlineData(LauncherPage.Account, "服务器")]
    [InlineData(LauncherPage.Settings, "服务器")]
    public void CreateViewModel_SelectsRequestedPageWithoutExpandingStartupOptions(
        LauncherPage requestedPage,
        string expectedStartupPage)
    {
        var viewModel = LauncherUiPreview.CreateViewModel(
            useDarkMode: true,
            NullLauncherThemeService.Instance,
            requestedPage);

        Assert.Equal(requestedPage, viewModel.ActivePage);
        Assert.Equal(expectedStartupPage, viewModel.SelectedStartupPage);
        Assert.Equal(["服务器", "活动", "下载中心"], viewModel.StartupPageOptions);

        viewModel.ShowDownloadsCommand.Execute(null);
    }

    [Fact]
    public void CreateViewModel_SeedsDownloadHistoryWithoutAnActiveTask()
    {
        var viewModel = LauncherUiPreview.CreateViewModel(
            useDarkMode: true,
            NullLauncherThemeService.Instance,
            LauncherPage.Downloads);

        Assert.False(viewModel.HasActiveDownload);
        Assert.Equal(2, viewModel.DownloadHistoryCount);
        Assert.Contains(
            viewModel.DownloadHistory,
            item => item.Status == DownloadJobStatus.Completed);
        Assert.Contains(
            viewModel.DownloadHistory,
            item => item.Status == DownloadJobStatus.Failed);
    }

    [Fact]
    public void CreateViewModel_SeedsMultipleActivitiesOnTheSameDay()
    {
        var viewModel = LauncherUiPreview.CreateViewModel(
            useDarkMode: true,
            NullLauncherThemeService.Instance,
            LauncherPage.Activities);

        Assert.Contains(
            viewModel.ActivityCalendar.Days,
            day => day.ActivityCount >= 2);
        Assert.Single(viewModel.ActivityCalendar.UnscheduledActivities);
    }
}
#endif

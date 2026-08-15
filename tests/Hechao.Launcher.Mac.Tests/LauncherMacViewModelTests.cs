using Hechao.Launcher.Mac.ViewModels;

namespace Hechao.Launcher.Mac.Tests;

public sealed class LauncherMacViewModelTests
{
    [Fact]
    public async Task PlayerFlow_RestoresCatalogInstallsLaunchesAndStops()
    {
        var fixture = new TestLauncherFixture();
        var viewModel = fixture.CreateViewModel();

        await viewModel.InitializeAsync();

        Assert.True(viewModel.IsAuthenticated);
        Assert.True(viewModel.IsMinecraftLinked);
        Assert.Single(viewModel.Servers);
        Assert.Equal("赫朝生存二服 · 演示", viewModel.SelectedServerName);
        Assert.Equal("安装客户端", viewModel.PrimaryActionText);

        await viewModel.PrimaryActionCommand.ExecuteAsync();

        Assert.Equal(1, fixture.Installation.InstallCount);
        Assert.Equal("启动游戏", viewModel.PrimaryActionText);
        Assert.Single(viewModel.Downloads);
        Assert.Equal("已完成", viewModel.Downloads[0].StatusText);

        viewModel.NavigateCommand.Execute("Home");
        await viewModel.PrimaryActionCommand.ExecuteAsync();

        Assert.Equal(1, fixture.GameLauncher.LaunchCount);
        Assert.Equal(1, fixture.Authentication.AuthorizationCount);
        Assert.True(viewModel.IsGameRunning);

        await viewModel.StopGameCommand.ExecuteAsync();

        Assert.False(viewModel.IsGameRunning);
        Assert.Equal("Minecraft 已退出。", viewModel.StatusMessage);
    }
}

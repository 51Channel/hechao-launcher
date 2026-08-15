using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Hechao.Launcher.Mac.ViewModels;

namespace Hechao.Launcher.Mac.Tests;

public sealed class MainWindowRenderingTests
{
    [AvaloniaFact]
    public async Task MainWindow_RendersMinimumAndStandardWorkspaces()
    {
        var fixture = new TestLauncherFixture();
        var viewModel = fixture.CreateViewModel();
        await viewModel.InitializeAsync();

        await RenderAsync(viewModel, 1180, 680, "hechao-m4-minimum.png");
        await RenderAsync(viewModel, 1380, 840, "hechao-m4-standard.png");
        viewModel.NavigateCommand.Execute("Account");
        await RenderAsync(viewModel, 1380, 840, "hechao-m4-account.png");
        viewModel.NavigateCommand.Execute("Settings");
        await RenderAsync(viewModel, 1380, 840, "hechao-m4-settings.png");

        var busyFixture = new TestLauncherFixture();
        busyFixture.Installation.PauseNextInstall();
        var busyViewModel = busyFixture.CreateViewModel();
        await busyViewModel.InitializeAsync();
        var installTask = busyViewModel.PrimaryActionCommand.ExecuteAsync();
        await busyFixture.Installation.WaitForInstallAsync();
        await RenderAsync(busyViewModel, 1380, 840, "hechao-m4-installing.png");
        busyFixture.Installation.ResumeInstall();
        await installTask;

        var failureFixture = new TestLauncherFixture();
        failureFixture.Installation.FailNextInstall = true;
        var failureViewModel = failureFixture.CreateViewModel();
        await failureViewModel.InitializeAsync();
        await failureViewModel.PrimaryActionCommand.ExecuteAsync();
        await RenderAsync(failureViewModel, 1380, 840, "hechao-m4-install-failed.png");
    }

    private static Task RenderAsync(
        LauncherMacViewModel viewModel,
        double width,
        double height,
        string fileName)
    {
        var window = new MainWindow
        {
            DataContext = viewModel,
            Width = width,
            Height = height
        };
        window.Show();
        window.UpdateLayout();

        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        Assert.True(frame.PixelSize.Width >= width);
        Assert.True(frame.PixelSize.Height >= height);

        var outputDirectory = Environment.GetEnvironmentVariable(
            "HECHAO_UI_SNAPSHOT_DIR");
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
            frame.Save(Path.Combine(outputDirectory, fileName));
        }

        window.Close();
        return Task.CompletedTask;
    }
}

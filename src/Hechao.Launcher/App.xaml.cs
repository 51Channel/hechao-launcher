using System.Windows;
using Hechao.Distribution;
using Hechao.Launcher.Infrastructure;
using Hechao.Launcher.Services;

namespace Hechao.Launcher;

public partial class App : Application
{
    private SingleInstanceGuard? _singleInstanceGuard;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var updaterModeRequested = e.Args.Any(argument =>
            string.Equals(
                argument,
                "--apply-launcher-update",
                StringComparison.Ordinal));
        if (LauncherUpdateBootstrap.TryParse(e.Args, out var updateCommand))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var exitCode = await LauncherUpdateBootstrap.ExecuteAsync(
                updateCommand!);
            Shutdown(exitCode);
            return;
        }

        if (updaterModeRequested)
        {
            Shutdown(2);
            return;
        }

#if DEBUG
        if (Development.LauncherUiPreview.TryGetRequestedTheme(
                e.Args,
                out var previewUsesDarkMode))
        {
            Development.LauncherUiPreview.TryGetRequestedPage(
                e.Args,
                out var previewPage);
            var previewThemeService = new LauncherThemeService(this);
            previewThemeService.Apply(previewUsesDarkMode);
            var previewViewModel = Development.LauncherUiPreview.CreateViewModel(
                previewUsesDarkMode,
                previewThemeService,
                previewPage);
            var previewWindow = new MainWindow(
                previewViewModel);
            Development.LauncherUiPreview.TryGetRequestedSettingsTab(
                e.Args,
                out var previewSettingsTabIndex);
            previewWindow.SelectPreviewSettingsTab(previewSettingsTabIndex);
            previewWindow.Title = previewUsesDarkMode
                ? "赫朝启动器 - UI 预览（黑夜）"
                : "赫朝启动器 - UI 预览（日间）";
            if (Development.LauncherUiPreview.TryGetScreenshotRequest(
                    e.Args,
                    out var screenshotRequest))
            {
                previewWindow.Width = screenshotRequest.Width;
                previewWindow.Height = screenshotRequest.Height;
                previewWindow.WindowStartupLocation = WindowStartupLocation.Manual;
                previewWindow.Left = -10_000;
                previewWindow.Top = -10_000;
                previewWindow.ShowActivated = false;
                previewWindow.ShowInTaskbar = false;
                var captureStarted = false;
                previewWindow.ContentRendered += async (_, _) =>
                {
                    if (captureStarted)
                    {
                        return;
                    }

                    captureStarted = true;
                    try
                    {
                        await Development.LauncherUiPreview.CaptureWhenReadyAsync(
                            previewWindow,
                            previewViewModel,
                            screenshotRequest);
                        Shutdown(0);
                    }
                    catch (Exception exception)
                    {
                        System.Diagnostics.Trace.TraceError(
                            "Unable to capture launcher UI preview: {0}",
                            exception);
                        Shutdown(-1);
                    }
                };
            }
            MainWindow = previewWindow;
            previewWindow.Show();
            return;
        }
#endif

        if (!SingleInstanceGuard.TryAcquire(out _singleInstanceGuard))
        {
            MessageBox.Show(
                "赫朝启动器已经在运行，请切换到现有窗口。",
                "赫朝启动器",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown(0);
            return;
        }

        Exit += (_, _) =>
        {
            _singleInstanceGuard?.Dispose();
            _singleInstanceGuard = null;
        };

        var settingsStore = new JsonLauncherSettingsStore();
        LauncherSettings settings;
        try
        {
            settings = settingsStore.Load();
        }
        catch (ClientStorageMigrationException exception)
        {
            MessageBox.Show(
                $"游戏数据迁移未完成，原目录中的文件仍被保留。\n\n{exception.Message}",
                "赫朝启动器",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
            return;
        }

        var themeService = new LauncherThemeService(this);
        themeService.Apply(settings.UseDarkMode);
        var window = new MainWindow(settingsStore, themeService, settings);
        MainWindow = window;
        window.Show();
    }
}

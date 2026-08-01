using System.Windows;
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

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }
}

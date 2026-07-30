using System.Windows;
using Hechao.Launcher.Services;

namespace Hechao.Launcher;

public partial class App : Application
{
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

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }
}

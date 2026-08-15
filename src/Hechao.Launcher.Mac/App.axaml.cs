using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Hechao.Launcher.Mac.Services;

namespace Hechao.Launcher.Mac;

public sealed class App : Application
{
    private Mutex? _singleInstanceMutex;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _singleInstanceMutex = new Mutex(
                initiallyOwned: true,
                "world.hechao.launcher.mac.single-instance",
                out var createdNew);
            if (!createdNew)
            {
                desktop.Shutdown();
                return;
            }

            var viewModel = LauncherBootstrap.Create();
            desktop.MainWindow = new MainWindow
            {
                DataContext = viewModel
            };
            desktop.Exit += (_, _) => _singleInstanceMutex?.Dispose();
            _ = viewModel.InitializeAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}

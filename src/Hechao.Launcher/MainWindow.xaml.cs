using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Hechao.Launcher.Services;
using Hechao.Launcher.ViewModels;

namespace Hechao.Launcher;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var apiClient = LauncherApiClient.CreateDefault();
        var catalogClient = HttpServerCatalogClient.CreateDefault(new DemoServerCatalogClient(), apiClient);
        var authenticationService = new MicrosoftMinecraftAuthenticationService(
            apiClient,
            XboxMinecraftAuthenticationClient.CreateDefault(),
            LauncherIdentityConfiguration.MicrosoftClientId);
        var installationService = ClientInstallationService.CreateDefault(apiClient);
        var gameLauncherService = MinecraftGameLauncherService.CreateDefault(
            LauncherIdentityConfiguration.MicrosoftClientId);
        DataContext = new MainWindowViewModel(
            catalogClient,
            authenticationService,
            new JsonLauncherSettingsStore(),
            installationService,
            gameLauncherService);
    }

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<ButtonBase>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ToggleMaximizedState();
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void MinimizeButton_OnClick(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_OnClick(object sender, RoutedEventArgs e)
    {
        ToggleMaximizedState();
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ToggleMaximizedState()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private static T? FindAncestor<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T match)
            {
                return match;
            }

            child = VisualTreeHelper.GetParent(child);
        }

        return null;
    }
}

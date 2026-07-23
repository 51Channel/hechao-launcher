using System.IO;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Hechao.Launcher.Services;
using Hechao.Launcher.ViewModels;
using Microsoft.Win32;

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
        var viewModel = new MainWindowViewModel(
            catalogClient,
            authenticationService,
            new JsonLauncherSettingsStore(),
            installationService,
            gameLauncherService,
            new JsonDownloadHistoryStore());
        viewModel.CloseRequested += (_, _) => Close();
        DataContext = viewModel;
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

    private async void LoginAccountButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (await viewModel.LoginAccountAsync(
                LoginIdentifierTextBox.Text,
                LoginPasswordBox.Password))
        {
            LoginPasswordBox.Clear();
        }
    }

    private async void RegisterAccountButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (await viewModel.RegisterAccountAsync(
                RegisterUsernameTextBox.Text,
                RegisterDisplayNameTextBox.Text,
                RegisterPasswordBox.Password,
                RegisterEmailTextBox.Text))
        {
            RegisterPasswordBox.Clear();
        }
    }

    private void ChooseClientDirectoryButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var dialog = new OpenFolderDialog
        {
            Title = "选择赫朝客户端目录",
            Multiselect = false
        };
        var currentPath = Environment.ExpandEnvironmentVariables(viewModel.ClientDirectory);
        if (Directory.Exists(currentPath))
        {
            dialog.InitialDirectory = currentPath;
        }

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            viewModel.UpdateClientDirectory(dialog.FolderName);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            ArgumentException or NotSupportedException)
        {
            MessageBox.Show(
                this,
                "无法使用所选目录，请选择一个本机可写文件夹。",
                "赫朝启动器",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
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

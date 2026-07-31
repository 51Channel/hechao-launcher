using System.IO;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Hechao.Distribution;
using Hechao.Launcher.Services;
using Hechao.Launcher.ViewModels;
using Microsoft.Win32;

namespace Hechao.Launcher;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var settingsStore = new JsonLauncherSettingsStore();
        var useSystemProxy = settingsStore.Load().UseSystemProxy;
        var apiClient = LauncherApiClient.CreateDefault(
            useSystemProxy: useSystemProxy);
        var catalogClient = HttpServerCatalogClient.CreateDefault(new DemoServerCatalogClient(), apiClient);
        var authenticationService = new MicrosoftMinecraftAuthenticationService(
            apiClient,
                ForumRegistrationClient.CreateDefault(useSystemProxy),
                XboxMinecraftAuthenticationClient.CreateDefault(useSystemProxy),
            LauncherIdentityConfiguration.MicrosoftClientId);
        var installationService = ClientInstallationService.CreateDefault(
            apiClient,
            useSystemProxy);
        var gameLauncherService = MinecraftGameLauncherService.CreateDefault(
                LauncherIdentityConfiguration.MicrosoftClientId,
                useSystemProxy);
        MainWindowViewModel viewModel;
        try
        {
            viewModel = new MainWindowViewModel(
                catalogClient,
                authenticationService,
                settingsStore,
                installationService,
                gameLauncherService,
                new JsonDownloadHistoryStore(),
                new JsonGameDiagnosticsService(),
                new GameDiagnosticUploadService(apiClient),
                new JsonLauncherTelemetryService(apiClient),
                LauncherUpdateService.CreateDefault(apiClient, useSystemProxy),
                MinecraftSkinService.CreateDefault(useSystemProxy),
                new PlayerGameSettingsService());
        }
        catch (ClientStorageMigrationException exception)
        {
            MessageBox.Show(
                $"游戏数据迁移未完成，原目录中的文件仍被保留。\n\n{exception.Message}",
                "赫朝启动器",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Application.Current.Shutdown(-1);
            return;
        }

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
                RegisterEmailTextBox.Text,
                RegisterCodeTextBox.Text))
        {
            RegisterPasswordBox.Clear();
            RegisterCodeTextBox.Clear();
        }
    }

    private async void SendRegistrationCodeButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        SendRegistrationCodeButton.IsEnabled = false;
        var sent = await viewModel.SendRegistrationCodeAsync(RegisterEmailTextBox.Text);
        if (!sent)
        {
            SendRegistrationCodeButton.IsEnabled = true;
            return;
        }

        await Task.Delay(TimeSpan.FromSeconds(60));
        if (IsLoaded)
        {
            SendRegistrationCodeButton.IsEnabled = true;
        }
    }

    private async void ConfirmMinecraftUnlinkButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (await viewModel.UnlinkMinecraftAsync(UnlinkMinecraftPasswordBox.Password))
        {
            UnlinkMinecraftPasswordBox.Clear();
        }
    }

    private void CancelMinecraftUnlinkButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        UnlinkMinecraftPasswordBox.Clear();
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.CancelMinecraftUnlinkCommand.Execute(null);
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
            Title = "选择赫朝游戏数据目录",
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

    private async void ChooseProfileJavaButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = $"选择 {viewModel.SelectedProfileJavaVersionText}",
            Filter = "Java 可执行文件 (java.exe;javaw.exe)|java.exe;javaw.exe",
            CheckFileExists = true,
            Multiselect = false
        };
        if (viewModel.IsUsingCustomJava)
        {
            var currentDirectory = Path.GetDirectoryName(
                viewModel.SelectedProfileJavaPathText);
            if (Directory.Exists(currentDirectory))
            {
                dialog.InitialDirectory = currentDirectory;
            }
        }

        if (dialog.ShowDialog(this) == true)
        {
            await viewModel.UpdateSelectedProfileJavaPathAsync(dialog.FileName);
        }
    }

    private async void RollbackProfileButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel ||
            !viewModel.CanRollbackSelectedProfile)
        {
            return;
        }

        var result = MessageBox.Show(
            this,
            $"将当前客户端回滚到 v{viewModel.RollbackCandidateVersion}。\n\n" +
            "存档、截图、设置和服务器列表会保留；如果服务器目录仍发布新版本，回滚后会显示“更新客户端”。\n\n" +
            "是否继续？",
            "回滚客户端版本",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (result == MessageBoxResult.Yes)
        {
            await viewModel.RollbackSelectedProfileAsync();
        }
    }

    private async void DeleteProfileButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel ||
            !viewModel.CanDeleteSelectedProfile)
        {
            return;
        }

        var profileName = viewModel.SelectedProfileDisplayName;
        var result = MessageBox.Show(
            $"将删除 {profileName} 的客户端文件、配套 Java 和回滚副本。\n\n" +
            "灵敏度、按键绑定等个人游戏设置会保留；以后仍可重新安装。\n\n" +
            "确认继续吗？",
            "删除客户端",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (result == MessageBoxResult.Yes)
        {
            await viewModel.DeleteSelectedProfileAsync();
        }
    }

    private async void UploadDiagnosticButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel ||
            !viewModel.CanUploadDiagnosticBundle)
        {
            return;
        }

        var result = MessageBox.Show(
            this,
            "将上传刚刚生成的脱敏诊断包。\n\n" +
            "包内不含世界存档、账号密码或会话令牌；服务器最多保存 14 天，" +
            "管理员每次下载都会记录审计。\n\n是否继续？",
            "上传诊断包",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information,
            MessageBoxResult.No);
        if (result == MessageBoxResult.Yes)
        {
            await viewModel.UploadLatestDiagnosticBundleAsync();
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

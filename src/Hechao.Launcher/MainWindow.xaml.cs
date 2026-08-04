using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Hechao.Distribution;
using Hechao.Launcher.Services;
using Hechao.Launcher.ViewModels;
using Microsoft.Win32;

namespace Hechao.Launcher;

public partial class MainWindow : Window
{
    private IInputElement? _focusBeforeModal;
    private IInputElement? _focusBeforeNotifications;
    private bool _allowUpdaterClose;
    private bool _modalFocusCaptured;

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

        viewModel.CloseRequested += ViewModel_OnCloseRequested;
        DataContext = viewModel;
        viewModel.PropertyChanged += ViewModel_OnPropertyChanged;
        Closed += (_, _) =>
        {
            viewModel.CloseRequested -= ViewModel_OnCloseRequested;
            viewModel.PropertyChanged -= ViewModel_OnPropertyChanged;
        };
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowUpdaterClose &&
            DataContext is MainWindowViewModel viewModel &&
            (viewModel.IsProgressActive || viewModel.IsLauncherUpdateBusy))
        {
            e.Cancel = true;
            MessageBox.Show(
                "当前任务尚未结束。请先等待任务完成，或在下载中心取消后再关闭启动器。",
                "赫朝启动器",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        base.OnClosing(e);
    }

    private void ViewModel_OnCloseRequested(object? sender, EventArgs e)
    {
        _allowUpdaterClose = true;
        Close();
    }

    private void ViewModel_OnPropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is
            nameof(MainWindowViewModel.IsMicrosoftSignInVisible) or
            nameof(MainWindowViewModel.IsLauncherUpdateVisible) or
            nameof(MainWindowViewModel.IsLauncherUpdateBusy))
        {
            Dispatcher.BeginInvoke(
                DispatcherPriority.Input,
                UpdateModalKeyboardFocus);
        }

        if (eventArgs.PropertyName == nameof(MainWindowViewModel.IsNotificationsOpen))
        {
            Dispatcher.BeginInvoke(
                DispatcherPriority.Input,
                UpdateNotificationsKeyboardFocus);
        }

        if (eventArgs.PropertyName ==
            nameof(MainWindowViewModel.ToastAnnouncementRevision))
        {
            Dispatcher.BeginInvoke(
                DispatcherPriority.ContextIdle,
                ToastLiveRegion.RaiseLiveRegionChanged);
        }

        if (eventArgs.PropertyName == nameof(MainWindowViewModel.CatalogAnnouncementRevision))
        {
            Dispatcher.BeginInvoke(
                DispatcherPriority.ContextIdle,
                () =>
                {
                    ServerCatalogStatusLiveRegion.RaiseLiveRegionChanged();
                    ActivityCalendarStatusLiveRegion.RaiseLiveRegionChanged();
                });
        }

        if (eventArgs.PropertyName == nameof(MainWindowViewModel.AccountFormAnnouncementRevision))
        {
            Dispatcher.BeginInvoke(
                DispatcherPriority.ContextIdle,
                () =>
                {
                    AccountFormLiveRegion.RaiseLiveRegionChanged();
                    AuthenticatedAccountFormLiveRegion.RaiseLiveRegionChanged();
                });
        }

        if (eventArgs.PropertyName == nameof(MainWindowViewModel.LauncherUpdateStatus))
        {
            Dispatcher.BeginInvoke(
                DispatcherPriority.ContextIdle,
                LauncherUpdateProgressRegion.RaiseLiveRegionChanged);
        }
    }

    private void UpdateNotificationsKeyboardFocus()
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (viewModel.IsNotificationsOpen)
        {
            _focusBeforeNotifications ??= Keyboard.FocusedElement;
            CloseNotificationsButton.Focus();
            Keyboard.Focus(CloseNotificationsButton);
            return;
        }

        if (_focusBeforeNotifications is UIElement previousFocus &&
            previousFocus.IsVisible &&
            previousFocus.IsEnabled)
        {
            previousFocus.Focus();
            Keyboard.Focus(previousFocus);
        }

        _focusBeforeNotifications = null;
    }

    private void UpdateModalKeyboardFocus()
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (viewModel.IsLauncherUpdateVisible || viewModel.IsMicrosoftSignInVisible)
        {
            if (!_modalFocusCaptured)
            {
                _focusBeforeModal = Keyboard.FocusedElement;
                _modalFocusCaptured = true;
            }

            UIElement target = viewModel.IsLauncherUpdateVisible
                ? InstallLauncherUpdateButton.IsEnabled
                    ? InstallLauncherUpdateButton
                    : LauncherUpdateProgressRegion
                : CancelMicrosoftSignInButton;
            target.Focus();
            Keyboard.Focus(target);
            return;
        }

        if (!_modalFocusCaptured)
        {
            return;
        }

        _modalFocusCaptured = false;
        if (_focusBeforeModal is UIElement previousFocus &&
            previousFocus.IsVisible &&
            previousFocus.IsEnabled)
        {
            previousFocus.Focus();
            Keyboard.Focus(previousFocus);
        }
        else
        {
            Keyboard.ClearFocus();
        }

        _focusBeforeModal = null;
    }

    private void MainWindow_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (viewModel.IsNotificationsOpen || viewModel.IsSettingsOpen)
        {
            viewModel.CloseOverlaysCommand.Execute(null);
            e.Handled = true;
        }
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

        await viewModel.SendRegistrationCodeAsync(RegisterEmailTextBox.Text);
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

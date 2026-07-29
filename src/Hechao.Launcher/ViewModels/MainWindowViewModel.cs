using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using Hechao.Contracts;
using Hechao.Distribution;
using Hechao.Launcher.Infrastructure;
using Hechao.Launcher.Services;

namespace Hechao.Launcher.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly IServerCatalogClient _catalogClient;
    private readonly ILauncherAuthenticationService _authenticationService;
    private readonly ILauncherSettingsStore _settingsStore;
    private readonly IClientInstallationService _installationService;
    private readonly IMinecraftGameLauncherService _gameLauncherService;
    private readonly IDownloadHistoryStore _downloadHistoryStore;
    private readonly IGameDiagnosticsService _gameDiagnosticsService;
    private readonly IGameDiagnosticUploadService _diagnosticUploadService;
    private readonly ILauncherTelemetryService _telemetryService;
    private readonly SynchronizationContext? _uiContext;
    private readonly Dictionary<string, ClientProfileSummary> _clientProfiles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _profileJavaPaths =
        new(StringComparer.Ordinal);
    private LauncherSettings _settings;
    private LauncherPage _activePage = LauncherPage.Servers;
    private ServerSummary? _selectedServer;
    private LocalProfileState _selectedProfileState = LocalProfileState.Missing;
    private bool _selectedProfileStateChecked;
    private bool _suppressNextAutomaticProfileCheck;
    private bool _hasLoadedCatalog;
    private InstalledProfileState? _rollbackCandidate;
    private double _updateProgress;
    private string _clientStatusText = "正在检查客户端";
    private string _primaryActionText = "安装客户端";
    private bool _isProgressActive;
    private bool _isNotificationsOpen;
    private bool _isSettingsOpen;
    private bool _isToastVisible;
    private string _toastMessage = string.Empty;
    private string _selectedMemory;
    private string _clientDirectory;
    private bool _checkForUpdates;
    private bool _keepDownloadsAfterClose;
    private bool _closeLauncherAfterGameStart;
    private bool _openDownloadsWhenInstalling;
    private string _selectedStartupPage;
    private bool _isCatalogLoading;
    private HechaoAccount? _currentAccount;
    private string? _accountStatusHint;
    private bool _isAccountBusy;
    private bool _isAdminConsoleBusy;
    private bool _isMinecraftUnlinkFormVisible;
    private string _accountFormMessage = string.Empty;
    private bool _isAccountFormError;
    private bool _isMicrosoftSignInVisible;
    private CancellationTokenSource? _microsoftSignInCancellation;
    private DownloadJobViewModel? _activeDownload;
    private CancellationTokenSource? _activeInstallCancellation;
    private bool _isInstallingClient;
    private GameExitRecord? _latestGameExit;
    private bool _isDiagnosticBusy;
    private GameDiagnosticBundleResult? _latestDiagnosticBundle;
    private string? _latestDiagnosticProfileId;
    private string? _runningServerId;
    private string _diagnosticUploadStatus = "先在本机生成诊断包，再决定是否上传。";

    public MainWindowViewModel(
        IServerCatalogClient catalogClient,
        ILauncherAuthenticationService authenticationService,
        ILauncherSettingsStore settingsStore,
        IClientInstallationService installationService,
        IMinecraftGameLauncherService gameLauncherService,
        IDownloadHistoryStore downloadHistoryStore,
        IGameDiagnosticsService gameDiagnosticsService,
        IGameDiagnosticUploadService diagnosticUploadService,
        ILauncherTelemetryService? telemetryService = null)
    {
        _catalogClient = catalogClient;
        _authenticationService = authenticationService;
        _settingsStore = settingsStore;
        _installationService = installationService;
        _gameLauncherService = gameLauncherService;
        _downloadHistoryStore = downloadHistoryStore;
        _gameDiagnosticsService = gameDiagnosticsService;
        _diagnosticUploadService = diagnosticUploadService;
        _telemetryService =
            telemetryService ?? NullLauncherTelemetryService.Instance;
        _uiContext = SynchronizationContext.Current;
        _settings = settingsStore.Load();
        if (_settings.ProfileJavaPaths is not null)
        {
            foreach (var (profileId, javaPath) in _settings.ProfileJavaPaths)
            {
                if (!string.IsNullOrWhiteSpace(profileId) &&
                    !string.IsNullOrWhiteSpace(javaPath))
                {
                    _profileJavaPaths[profileId] = javaPath;
                }
            }
        }
        _latestGameExit = gameDiagnosticsService.LoadLatestExit();
        _gameLauncherService.ProcessExited += GameLauncherService_OnProcessExited;
        _runningServerId = _gameLauncherService.GetRunningGame()?.ServerId;

        MemoryOptions = ["2 GB", "4 GB", "6 GB", "8 GB", "12 GB", "16 GB"];
        _selectedMemory = MemoryOptions.Contains(_settings.Memory) ? _settings.Memory : "6 GB";
        _clientDirectory = string.IsNullOrWhiteSpace(_settings.ClientDirectory)
            ? JsonLauncherSettingsStore.DefaultClientDataDirectory
            : _settings.ClientDirectory;
        _checkForUpdates = _settings.CheckForUpdates;
        _keepDownloadsAfterClose = _settings.KeepDownloadsAfterClose;
        _closeLauncherAfterGameStart = _settings.CloseLauncherAfterGameStart;
        _openDownloadsWhenInstalling = _settings.OpenDownloadsWhenInstalling;
        StartupPageOptions = ["服务器", "下载中心", "活动"];
        _selectedStartupPage = StartupPageOptions.Contains(_settings.StartupPage)
            ? _settings.StartupPage
            : "服务器";
        _activePage = GetStartupPage(_selectedStartupPage);

        SelectServerCommand = new RelayCommand<ServerSummary>(SelectServer);
        PrimaryActionCommand = new AsyncRelayCommand(
            StartPrimaryActionAsync,
            HandleUnexpectedPrimaryActionError,
            CanUseSelectedServer);
        RepairCommand = new AsyncRelayCommand(
            () => InstallSelectedProfileAsync(isRepair: true),
            HandleUnexpectedPrimaryActionError,
            () => !IsProgressActive);
        RefreshCommand = new RelayCommand(() => _ = LoadCatalogAsync(userInitiated: true), () => !_isCatalogLoading);
        OpenClientDirectoryCommand = new RelayCommand(OpenClientDirectory);
        ToggleNotificationsCommand = new RelayCommand(ToggleNotifications);
        ToggleSettingsCommand = new RelayCommand(ToggleSettings);
        CloseOverlaysCommand = new RelayCommand(CloseOverlays);
        AccountActionCommand = new RelayCommand(
            () => ActivePage = LauncherPage.Account);
        LogoutAccountCommand = new RelayCommand(
            StartAccountLogout,
            () => IsAuthenticated && !IsAccountBusy);
        LinkMinecraftCommand = new AsyncRelayCommand(
            StartMinecraftLinkAsync,
            HandleUnexpectedMicrosoftSignInError,
            () => IsAuthenticated && !IsMinecraftLinked && !IsAccountBusy);
        CancelMicrosoftSignInCommand = new RelayCommand(
            CancelMicrosoftSignIn,
            () => IsMicrosoftSignInVisible &&
                _microsoftSignInCancellation is { IsCancellationRequested: false });
        UnlinkMinecraftCommand = new RelayCommand(
            BeginMinecraftUnlink,
            () => IsAuthenticated && IsMinecraftLinked && !IsAccountBusy);
        CancelMinecraftUnlinkCommand = new RelayCommand(
            CancelMinecraftUnlink,
            () => IsMinecraftUnlinkFormVisible && !IsAccountBusy);
        LogoutAllDevicesCommand = new RelayCommand(
            StartLogoutAllDevices,
            () => IsAuthenticated && !IsAccountBusy);
        OpenAdminConsoleCommand = new RelayCommand(
            OpenAdminConsole,
            () => IsAdministrator && !IsAdminConsoleBusy);
        ShowServersCommand = new RelayCommand(() => ActivePage = LauncherPage.Servers);
        ShowDownloadsCommand = new RelayCommand(() => ActivePage = LauncherPage.Downloads);
        ShowActivitiesCommand = new RelayCommand(() => ActivePage = LauncherPage.Activities);
        ShowAccountCommand = new RelayCommand(() => ActivePage = LauncherPage.Account);
        ShowSettingsPageCommand = new RelayCommand(() => ActivePage = LauncherPage.Settings);
        CancelDownloadCommand = new RelayCommand(
            CancelActiveDownload,
            () => _isInstallingClient && _activeInstallCancellation is not null);
        ClearDownloadHistoryCommand = new RelayCommand(
            ClearDownloadHistory,
            () => DownloadHistory.Count > 0);
        ViewActivityServerCommand = new RelayCommand<ServerSummary>(ViewActivityServer);
        ResetLauncherSettingsCommand = new RelayCommand(ResetLauncherSettings);
        CreateDiagnosticBundleCommand = new RelayCommand(
            StartCreateDiagnosticBundle,
            CanCreateDiagnosticBundle);
        OpenDiagnosticsDirectoryCommand = new RelayCommand(OpenDiagnosticsDirectory);
        UseManagedJavaCommand = new RelayCommand(UseManagedJava);

        LoadDownloadHistory();

        _ = _telemetryService.RecordAsync(
            LauncherTelemetryEventType.LauncherStarted,
            LauncherTelemetryOutcome.Success);
        _ = InitializeAsync();
    }

    public ObservableCollection<ServerSummary> Servers { get; } = [];
    public ObservableCollection<ServerSummary> ActivityServers { get; } = [];
    public ObservableCollection<DownloadJobViewModel> DownloadHistory { get; } = [];
    public IReadOnlyList<string> MemoryOptions { get; }
    public IReadOnlyList<string> StartupPageOptions { get; }
    public string LauncherVersionText { get; } = $"v{LauncherProductInfo.Version}";
    public RelayCommand<ServerSummary> SelectServerCommand { get; }
    public AsyncRelayCommand PrimaryActionCommand { get; }
    public AsyncRelayCommand RepairCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand OpenClientDirectoryCommand { get; }
    public RelayCommand ToggleNotificationsCommand { get; }
    public RelayCommand ToggleSettingsCommand { get; }
    public RelayCommand CloseOverlaysCommand { get; }
    public RelayCommand AccountActionCommand { get; }
    public RelayCommand LogoutAccountCommand { get; }
    public AsyncRelayCommand LinkMinecraftCommand { get; }
    public RelayCommand CancelMicrosoftSignInCommand { get; }
    public RelayCommand UnlinkMinecraftCommand { get; }
    public RelayCommand CancelMinecraftUnlinkCommand { get; }
    public RelayCommand LogoutAllDevicesCommand { get; }
    public RelayCommand OpenAdminConsoleCommand { get; }
    public RelayCommand ShowServersCommand { get; }
    public RelayCommand ShowDownloadsCommand { get; }
    public RelayCommand ShowActivitiesCommand { get; }
    public RelayCommand ShowAccountCommand { get; }
    public RelayCommand ShowSettingsPageCommand { get; }
    public RelayCommand CancelDownloadCommand { get; }
    public RelayCommand ClearDownloadHistoryCommand { get; }
    public RelayCommand<ServerSummary> ViewActivityServerCommand { get; }
    public RelayCommand ResetLauncherSettingsCommand { get; }
    public RelayCommand CreateDiagnosticBundleCommand { get; }
    public RelayCommand OpenDiagnosticsDirectoryCommand { get; }
    public RelayCommand UseManagedJavaCommand { get; }
    public event EventHandler? CloseRequested;

    public LauncherPage ActivePage
    {
        get => _activePage;
        set
        {
            if (!SetProperty(ref _activePage, value))
            {
                return;
            }

            CloseOverlays();
            OnPropertyChanged(nameof(IsServersPage));
            OnPropertyChanged(nameof(IsDownloadsPage));
            OnPropertyChanged(nameof(IsActivitiesPage));
            OnPropertyChanged(nameof(IsAccountPage));
            OnPropertyChanged(nameof(IsSettingsPage));
            OnPropertyChanged(nameof(CurrentPageTitle));
        }
    }

    public bool IsServersPage => ActivePage == LauncherPage.Servers;
    public bool IsDownloadsPage => ActivePage == LauncherPage.Downloads;
    public bool IsActivitiesPage => ActivePage == LauncherPage.Activities;
    public bool IsAccountPage => ActivePage == LauncherPage.Account;
    public bool IsSettingsPage => ActivePage == LauncherPage.Settings;
    public string CurrentPageTitle => ActivePage switch
    {
        LauncherPage.Downloads => "下载中心",
        LauncherPage.Activities => "活动",
        LauncherPage.Account => "赫朝账户",
        LauncherPage.Settings => "设置",
        _ => SelectedServer?.Name ?? "服务器"
    };

    public DownloadJobViewModel? ActiveDownload
    {
        get => _activeDownload;
        private set
        {
            if (!SetProperty(ref _activeDownload, value))
            {
                return;
            }

            OnPropertyChanged(nameof(HasActiveDownload));
            OnPropertyChanged(nameof(HasNoActiveDownload));
            OnPropertyChanged(nameof(DownloadQueueStatusText));
        }
    }

    public bool HasActiveDownload => ActiveDownload is not null;
    public bool HasNoActiveDownload => !HasActiveDownload;
    public bool HasDownloadHistory => DownloadHistory.Count > 0;
    public bool HasNoDownloadHistory => !HasDownloadHistory;
    public int DownloadHistoryCount => DownloadHistory.Count;
    public int ActivityServerCount => ActivityServers.Count;
    public bool HasActivityServers => ActivityServers.Count > 0;
    public string DownloadQueueStatusText => HasActiveDownload
        ? "1 个任务正在进行"
        : DownloadHistory.Count > 0
            ? $"{DownloadHistory.Count} 条历史记录"
            : "暂无下载任务";
    public string LatestGameExitText
    {
        get
        {
            if (_latestGameExit is null)
            {
                return "尚无游戏退出记录";
            }

            var profileName = _clientProfiles.TryGetValue(
                _latestGameExit.ProfileId,
                out var profile)
                ? profile.DisplayName
                : _latestGameExit.ProfileId;
            var exitStatus = _latestGameExit.ExitCode switch
            {
                0 => "正常退出",
                int exitCode => $"异常退出（代码 {exitCode}）",
                _ => "退出状态未知"
            };
            return $"{profileName} · {exitStatus} · " +
                   _latestGameExit.ExitedAt.ToLocalTime().ToString("MM-dd HH:mm");
        }
    }

    public string DiagnosticActionText => IsDiagnosticBusy
        ? "正在生成"
        : "生成诊断包";

    public string DiagnosticUploadActionText => IsDiagnosticBusy
        ? "正在处理"
        : "上传给管理员";

    public string DiagnosticUploadStatus => _diagnosticUploadStatus;

    public bool CanUploadDiagnosticBundle =>
        IsAuthenticated &&
        !IsDiagnosticBusy &&
        _latestDiagnosticBundle is not null &&
        !string.IsNullOrWhiteSpace(_latestDiagnosticProfileId) &&
        File.Exists(_latestDiagnosticBundle.BundlePath);

    public bool IsDiagnosticBusy
    {
        get => _isDiagnosticBusy;
        private set
        {
            if (!SetProperty(ref _isDiagnosticBusy, value))
            {
                return;
            }

            OnPropertyChanged(nameof(DiagnosticActionText));
            OnPropertyChanged(nameof(DiagnosticUploadActionText));
            OnPropertyChanged(nameof(CanUploadDiagnosticBundle));
            CreateDiagnosticBundleCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsAuthenticated => _currentAccount is not null;
    public bool IsMinecraftLinked => _currentAccount?.IsMinecraftLinked == true;
    public bool IsAdministrator =>
        _currentAccount?.AccessTier == AccessTier.Administrator;
    public string AccountDisplayName => _currentAccount?.DisplayName ?? "访客";
    public string AccountUsername => _currentAccount is null
        ? "尚未登录赫朝账号"
        : $"@{_currentAccount.Username}";
    public string AccountStatusText => IsAccountBusy
        ? "正在处理账号请求"
        : IsAuthenticated
            ? "赫朝账号已登录"
            : _accountStatusHint ?? "尚未登录";
    public string AccountAccessText => _currentAccount is null
        ? "先注册或登录赫朝账号"
        : IsMinecraftLinked
            ? $"{GetAccessTierText(_currentAccount.AccessTier)} · {_currentAccount.LuckPermsPrimaryGroup}"
            : "尚未绑定 Minecraft 正版身份";
    public string MinecraftIdentityText => IsMinecraftLinked
        ? $"{_currentAccount!.MinecraftName} · {_currentAccount.MinecraftUuid:D}"
        : "未绑定";
    public string MinecraftLinkStatusText => IsMinecraftLinked
        ? "Minecraft 正版身份已认证"
        : "需要完成 Microsoft 正版认证后才能启动游戏";
    public string AccountActionGlyph => "\uE77B";
    public string AccountActionTooltip => "打开赫朝账户";
    public string AdminConsoleButtonText =>
        IsAdminConsoleBusy ? "正在打开" : "打开管理后台";
    public string AccountFormMessage
    {
        get => _accountFormMessage;
        private set
        {
            if (SetProperty(ref _accountFormMessage, value))
            {
                OnPropertyChanged(nameof(HasAccountFormMessage));
            }
        }
    }
    public bool HasAccountFormMessage =>
        !string.IsNullOrWhiteSpace(AccountFormMessage);
    public bool IsAccountFormError
    {
        get => _isAccountFormError;
        private set => SetProperty(ref _isAccountFormError, value);
    }

    public bool IsMinecraftUnlinkFormVisible
    {
        get => _isMinecraftUnlinkFormVisible;
        private set
        {
            if (!SetProperty(ref _isMinecraftUnlinkFormVisible, value))
            {
                return;
            }

            CancelMinecraftUnlinkCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsMicrosoftSignInVisible
    {
        get => _isMicrosoftSignInVisible;
        private set
        {
            if (!SetProperty(ref _isMicrosoftSignInVisible, value))
            {
                return;
            }

            CancelMicrosoftSignInCommand.RaiseCanExecuteChanged();
        }
    }

    public string MicrosoftSignInTitle => "正在等待 Microsoft 登录";

    public string MicrosoftSignInDescription =>
        "浏览器已打开，请在 Microsoft 页面选择正版账号并完成授权。完成后，启动器会自动继续验证 Minecraft Java 版。";

    public bool IsAccountBusy
    {
        get => _isAccountBusy;
        private set
        {
            if (!SetProperty(ref _isAccountBusy, value))
            {
                return;
            }

            OnPropertyChanged(nameof(AccountStatusText));
            AccountActionCommand.RaiseCanExecuteChanged();
            LogoutAccountCommand.RaiseCanExecuteChanged();
            LinkMinecraftCommand.RaiseCanExecuteChanged();
            UnlinkMinecraftCommand.RaiseCanExecuteChanged();
            CancelMinecraftUnlinkCommand.RaiseCanExecuteChanged();
            LogoutAllDevicesCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsAdminConsoleBusy
    {
        get => _isAdminConsoleBusy;
        private set
        {
            if (!SetProperty(ref _isAdminConsoleBusy, value))
            {
                return;
            }

            OnPropertyChanged(nameof(AdminConsoleButtonText));
            OpenAdminConsoleCommand.RaiseCanExecuteChanged();
        }
    }

    public ServerSummary? SelectedServer
    {
        get => _selectedServer;
        set
        {
            if (!SetProperty(ref _selectedServer, value))
            {
                return;
            }

            SetRollbackCandidate(null);
            OnPropertyChanged(nameof(SelectedServerStatusText));
            OnPropertyChanged(nameof(SelectedServerLoaderText));
            OnPropertyChanged(nameof(SelectedServerPlayerText));
            OnPropertyChanged(nameof(SelectedServerCategoryText));
            OnPropertyChanged(nameof(SelectedServerDescriptionText));
            OnPropertyChanged(nameof(SelectedServerVersionText));
            OnPropertyChanged(nameof(SelectedServerAccessText));
            OnPropertyChanged(nameof(SelectedProfileDisplayName));
            OnPropertyChanged(nameof(SelectedProfileMetaText));
            OnPropertyChanged(nameof(SelectedProfileGameDirectory));
            NotifySelectedProfileJavaPropertiesChanged();
            OnPropertyChanged(nameof(IsSelectedServerOnline));
            OnPropertyChanged(nameof(CurrentPageTitle));
            PrimaryActionCommand.RaiseCanExecuteChanged();
            CreateDiagnosticBundleCommand.RaiseCanExecuteChanged();
            if (value is not null)
            {
                _selectedProfileState = LocalProfileState.Missing;
                _selectedProfileStateChecked = false;
                CreateDiagnosticBundleCommand.RaiseCanExecuteChanged();
                UpdateProgress = 0;
                var shouldCheckProfile = !_suppressNextAutomaticProfileCheck;
                _suppressNextAutomaticProfileCheck = false;
                ClientStatusText = shouldCheckProfile
                    ? "正在检查客户端"
                    : "启动检查已关闭";
                UpdatePrimaryActionForState();
                SaveSettings();
                if (shouldCheckProfile)
                {
                    _ = RefreshClientStateAsync();
                }
            }
        }
    }

    public string SelectedServerStatusText => SelectedServer?.Status switch
    {
        ServerStatus.Online => "在线",
        ServerStatus.Maintenance => "维护中",
        _ => "未开放"
    };

    public string SelectedServerLoaderText => SelectedServer?.Loader.ToString() ?? string.Empty;
    public string SelectedServerPlayerText => SelectedServer is null ? string.Empty : $"{SelectedServer.OnlinePlayers}/{SelectedServer.MaxPlayers}";
    public string SelectedServerCategoryText => SelectedServer?.Id switch
    {
        "survival2" => "长期生存世界",
        "activity" => "限时活动",
        "dollnight" => "特别企划",
        _ => "赫朝服务器"
    };
    public string SelectedServerDescriptionText =>
        !string.IsNullOrWhiteSpace(SelectedServer?.Announcement)
            ? SelectedServer.Announcement
            : SelectedServer?.Id switch
            {
                "survival2" => "长期生存、建设与共同冒险的主世界。",
                "activity" => "本期活动客户端会自动安装并匹配服务器版本。",
                "dollnight" => "在夜幕与规则之间，完成这一场特别录制。",
                _ => "与赫朝的伙伴们一起创造新的 Minecraft 故事。"
            };
    public string SelectedServerVersionText => SelectedServer is null
        ? string.Empty
        : $"Minecraft {SelectedServer.MinecraftVersion} · {SelectedServer.Loader}";
    public string SelectedServerAccessText => SelectedServer is null
        ? string.Empty
        : $"{GetAccessTierText(SelectedServer.MinimumTier)}可进入";
    public string SelectedProfileDisplayName => GetSelectedProfile()?.DisplayName ?? "等待客户端档案";
    public string SelectedProfileMetaText
    {
        get
        {
            var profile = GetSelectedProfile();
            return profile is null
                ? "目录暂未提供版本信息"
                : $"v{profile.Version} · {FormatDownloadSize(profile.DownloadBytes)}";
        }
    }
    public bool IsSelectedServerOnline => SelectedServer?.Status == ServerStatus.Online;
    public bool CanRollbackSelectedProfile =>
        _rollbackCandidate is not null &&
        !IsProgressActive &&
        !IsSelectedProfileRunning();
    public string RollbackCandidateVersion => _rollbackCandidate?.Version ?? string.Empty;
    public string RollbackProfileToolTip => _rollbackCandidate is null
        ? "当前没有可回滚的上一版本"
        : IsSelectedProfileRunning()
            ? "请先退出当前客户端"
            : $"回滚到 v{_rollbackCandidate.Version}";

    public double UpdateProgress
    {
        get => _updateProgress;
        private set => SetProperty(ref _updateProgress, value);
    }

    public string ClientStatusText
    {
        get => _clientStatusText;
        private set => SetProperty(ref _clientStatusText, value);
    }

    public string PrimaryActionText
    {
        get => _primaryActionText;
        private set => SetProperty(ref _primaryActionText, value);
    }
    public string PrimaryActionGlyph => !IsAuthenticated
        ? "\uE77B"
        : _selectedProfileState != LocalProfileState.Ready
            ? "\uE896"
            : !IsMinecraftLinked
                ? "\uE774"
                : "\uE768";

    public bool IsProgressActive
    {
        get => _isProgressActive;
        private set
        {
            if (SetProperty(ref _isProgressActive, value))
            {
                RepairCommand.RaiseCanExecuteChanged();
                PrimaryActionCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(CanRollbackSelectedProfile));
                OnPropertyChanged(nameof(RollbackProfileToolTip));
            }
        }
    }

    public string SelectedMemory
    {
        get => _selectedMemory;
        set
        {
            if (!SetProperty(ref _selectedMemory, value))
            {
                return;
            }

            SaveSettings();
            ShowToast($"已将游戏内存设为 {value}");
        }
    }

    public string ClientDirectory => _clientDirectory;

    public string SelectedProfileGameDirectory
    {
        get
        {
            var profileId = SelectedServer?.ClientProfileId;
            if (string.IsNullOrWhiteSpace(profileId))
            {
                return ClientDirectory;
            }

            try
            {
                return new ClientStorageLayout(ClientDirectory)
                    .GetProfileGameDirectory(profileId);
            }
            catch (Exception exception) when (
                exception is ArgumentException or NotSupportedException)
            {
                return ClientDirectory;
            }
        }
    }

    public string SelectedProfileJavaVersionText =>
        $"Java {GetSelectedProfileJavaMajorVersion()}";

    public bool IsUsingManagedJava =>
        string.IsNullOrWhiteSpace(GetSelectedProfileCustomJavaPath());

    public bool IsUsingCustomJava => !IsUsingManagedJava;

    public string SelectedProfileJavaModeText =>
        IsUsingManagedJava ? "随客户端安装" : "自定义 Java";

    public string SelectedProfileJavaPathText
    {
        get
        {
            var customPath = GetSelectedProfileCustomJavaPath();
            if (!string.IsNullOrWhiteSpace(customPath))
            {
                return customPath;
            }

            var profileId = SelectedServer?.ClientProfileId;
            if (string.IsNullOrWhiteSpace(profileId))
            {
                return "选择服务器后显示";
            }

            try
            {
                return new ClientStorageLayout(ClientDirectory)
                    .GetProfileRuntimeRoot(profileId);
            }
            catch (Exception exception) when (
                exception is ArgumentException or NotSupportedException)
            {
                return "随客户端安装";
            }
        }
    }

    public bool CloseLauncherAfterGameStart
    {
        get => _closeLauncherAfterGameStart;
        set
        {
            if (SetProperty(ref _closeLauncherAfterGameStart, value))
            {
                SaveSettings();
            }
        }
    }

    public bool OpenDownloadsWhenInstalling
    {
        get => _openDownloadsWhenInstalling;
        set
        {
            if (SetProperty(ref _openDownloadsWhenInstalling, value))
            {
                SaveSettings();
            }
        }
    }

    public string SelectedStartupPage
    {
        get => _selectedStartupPage;
        set
        {
            var normalized = StartupPageOptions.Contains(value) ? value : "服务器";
            if (SetProperty(ref _selectedStartupPage, normalized))
            {
                SaveSettings();
            }
        }
    }

    public bool CheckForUpdates
    {
        get => _checkForUpdates;
        set
        {
            if (SetProperty(ref _checkForUpdates, value))
            {
                SaveSettings();
                if (value &&
                    !_selectedProfileStateChecked &&
                    SelectedServer is not null &&
                    !IsProgressActive)
                {
                    ClientStatusText = "正在检查客户端";
                    UpdatePrimaryActionForState();
                    _ = RefreshClientStateAsync();
                }
            }
        }
    }

    public bool KeepDownloadsAfterClose
    {
        get => _keepDownloadsAfterClose;
        set
        {
            if (SetProperty(ref _keepDownloadsAfterClose, value))
            {
                SaveSettings();
            }
        }
    }

    public bool IsNotificationsOpen
    {
        get => _isNotificationsOpen;
        private set => SetProperty(ref _isNotificationsOpen, value);
    }

    public bool IsSettingsOpen
    {
        get => _isSettingsOpen;
        private set => SetProperty(ref _isSettingsOpen, value);
    }

    public bool IsToastVisible
    {
        get => _isToastVisible;
        private set => SetProperty(ref _isToastVisible, value);
    }

    public string ToastMessage
    {
        get => _toastMessage;
        private set => SetProperty(ref _toastMessage, value);
    }

    private async Task InitializeAsync()
    {
        IsAccountBusy = true;
        try
        {
            SetCurrentAccount(await _authenticationService.TryRestoreAsync());
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or LauncherApiException)
        {
            SetCurrentAccount(null);
        }
        finally
        {
            IsAccountBusy = false;
        }

        await LoadCatalogAsync();
    }

    private async Task LoadCatalogAsync(bool userInitiated = false)
    {
        if (_isCatalogLoading)
        {
            return;
        }

        _isCatalogLoading = true;
        RefreshCommand.RaiseCanExecuteChanged();
        try
        {
            var snapshot = await _catalogClient.GetCatalogAsync();
            _clientProfiles.Clear();
            foreach (var profile in snapshot.ClientProfiles)
            {
                _clientProfiles[profile.Id] = profile;
            }
            OnPropertyChanged(nameof(LatestGameExitText));

            Servers.Clear();
            ActivityServers.Clear();
            foreach (var server in snapshot.Servers.Where(IsPlayerServer))
            {
                Servers.Add(server);
                if (server.Id != "survival2")
                {
                    ActivityServers.Add(server);
                }
            }
            OnPropertyChanged(nameof(ActivityServerCount));
            OnPropertyChanged(nameof(HasActivityServers));

            var selectedServer =
                Servers.FirstOrDefault(server => server.Id == _settings.SelectedServerId) ??
                Servers.FirstOrDefault();
            _suppressNextAutomaticProfileCheck =
                !_hasLoadedCatalog &&
                !CheckForUpdates &&
                selectedServer is not null;
            SelectedServer = selectedServer;
            _hasLoadedCatalog = true;

            if (_catalogClient is ICatalogSourceState sourceState)
            {
                switch (sourceState.LastSource)
                {
                    case CatalogSource.Cache:
                        ShowToast("目录服务暂时不可用，已显示上次成功数据");
                        break;
                    case CatalogSource.BuiltIn:
                        ShowToast("目录服务暂时不可用，已显示内置应急目录");
                        break;
                    case CatalogSource.Live when userInitiated:
                        ShowToast("服务器状态已刷新");
                        break;
                }
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or JsonException)
        {
            ShowToast("暂时无法加载服务器目录");
        }
        catch (LauncherAuthenticationRequiredException)
        {
            Servers.Clear();
            ActivityServers.Clear();
            OnPropertyChanged(nameof(ActivityServerCount));
            OnPropertyChanged(nameof(HasActivityServers));
            SelectedServer = null;
            ShowToast("请先登录赫朝账号");
        }
        catch (LauncherApiException exception)
        {
            ShowToast(exception.ApiDetail ?? "目录服务暂时不可用");
        }
        finally
        {
            _isCatalogLoading = false;
            RefreshCommand.RaiseCanExecuteChanged();
        }
    }

    private void SelectServer(ServerSummary? server)
    {
        if (server is null)
        {
            return;
        }

        SelectedServer = server;
        ActivePage = LauncherPage.Servers;
        CloseOverlays();
        SaveSettings();
    }

    private void ViewActivityServer(ServerSummary? server)
    {
        if (server is null)
        {
            return;
        }

        SelectedServer = server;
        ActivePage = LauncherPage.Servers;
    }

    private async Task StartPrimaryActionAsync()
    {
        if (IsProgressActive || SelectedServer is null)
        {
            return;
        }

        if (!IsAuthenticated)
        {
            ActivePage = LauncherPage.Account;
            ShowToast("请先注册或登录赫朝账号");
            return;
        }

        if (!_selectedProfileStateChecked)
        {
            ClientStatusText = "正在检查客户端";
            UpdatePrimaryActionForState();
            if (!await RefreshClientStateAsync())
            {
                ShowToast("客户端状态检查未完成，请重试");
                return;
            }
        }

        if (_selectedProfileState != LocalProfileState.Ready)
        {
            if (!await InstallSelectedProfileAsync(isRepair: false))
            {
                return;
            }
        }

        if (!IsMinecraftLinked)
        {
            ActivePage = LauncherPage.Account;
            ShowToast("客户端已就绪，请绑定 Minecraft 正版身份");
            return;
        }

        await LaunchSelectedServerAsync();
    }

    private async Task<bool> InstallSelectedProfileAsync(bool isRepair)
    {
        if (IsProgressActive || SelectedServer is null ||
            !_clientProfiles.TryGetValue(SelectedServer.ClientProfileId, out var profile))
        {
            ShowToast("当前服务器没有可用的客户端档案");
            return false;
        }

        IsProgressActive = true;
        UpdateProgress = 0;
        ClientStatusText = isRepair ? "正在校验客户端" : "正在准备下载";
        PrimaryActionText = isRepair ? "正在修复" : "正在安装";
        var succeeded = false;
        var completionStatus = DownloadJobStatus.Failed;
        string? completionMessage = null;
        var telemetryClock = Stopwatch.StartNew();
        var telemetryOutcome = LauncherTelemetryOutcome.Failure;
        var telemetryFailure = LauncherTelemetryFailureCode.Unexpected;

        try
        {
            _activeInstallCancellation = new CancellationTokenSource();
            _isInstallingClient = true;
            BeginDownload(profile, isRepair);
            CancelDownloadCommand.RaiseCanExecuteChanged();
            var progress = new InlineProgress<ClientInstallProgress>(
                value => DispatchToUi(() => ApplyInstallProgress(value)));
            await _installationService.InstallAsync(
                profile,
                new ClientInstallationOptions(ClientDirectory, KeepDownloadsAfterClose),
                progress,
                _activeInstallCancellation.Token);
            _selectedProfileState = LocalProfileState.Ready;
            _selectedProfileStateChecked = true;
            await RefreshRollbackCandidateAsync(profile, SelectedServer?.Id);
            NotifySelectedProfileJavaPropertiesChanged();
            CreateDiagnosticBundleCommand.RaiseCanExecuteChanged();
            UpdateProgress = 100;
            ClientStatusText = "客户端已就绪";
            PrimaryActionText = "进入服务器";
            ShowToast(isRepair ? "客户端修复完成" : "客户端安装完成");
            succeeded = true;
            completionStatus = DownloadJobStatus.Completed;
            telemetryOutcome = LauncherTelemetryOutcome.Success;
            telemetryFailure = LauncherTelemetryFailureCode.None;
        }
        catch (LauncherAuthenticationRequiredException)
        {
            telemetryFailure = LauncherTelemetryFailureCode.AuthenticationRequired;
            ClientStatusText = "需要登录后下载";
            ShowToast("请先登录赫朝账号");
        }
        catch (LauncherApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            telemetryFailure = LauncherTelemetryFailureCode.ProfileUnavailable;
            ClientStatusText = "客户端尚未发布";
            ShowToast("该客户端档案尚未发布下载清单");
        }
        catch (LauncherApiException exception)
        {
            telemetryFailure = LauncherTelemetryFailureCode.ApiUnavailable;
            ClientStatusText = "下载服务不可用";
            ShowToast(exception.ApiDetail ?? "客户端分发服务暂时不可用");
        }
        catch (ManifestSignatureException)
        {
            telemetryFailure = LauncherTelemetryFailureCode.SignatureInvalid;
            ClientStatusText = "清单签名无效";
            ShowToast("客户端清单未通过签名验证，安装已停止");
        }
        catch (Exception exception) when (exception is ManifestIntegrityException or ClientManifestMismatchException)
        {
            telemetryFailure = LauncherTelemetryFailureCode.IntegrityFailed;
            ClientStatusText = "文件校验失败";
            ShowToast("下载内容与发布清单不一致，安装已停止");
        }
        catch (InsufficientDiskSpaceException)
        {
            telemetryFailure = LauncherTelemetryFailureCode.InsufficientDiskSpace;
            ClientStatusText = "磁盘空间不足";
            ShowToast("游戏数据目录所在磁盘空间不足");
        }
        catch (ProfileInstallInProgressException)
        {
            telemetryFailure = LauncherTelemetryFailureCode.InstallBusy;
            ClientStatusText = "安装正在进行";
            ShowToast("另一个启动器窗口正在安装这个客户端");
        }
        catch (ProfileJavaRuntimeException)
        {
            telemetryFailure = LauncherTelemetryFailureCode.RuntimePreparationFailed;
            ClientStatusText = "Java 安装未完成";
            ShowToast("客户端文件已校验，但配套 Java 安装失败；重新修复会继续下载");
        }
        catch (OperationCanceledException) when (
            _activeInstallCancellation?.IsCancellationRequested == true)
        {
            completionStatus = DownloadJobStatus.Canceled;
            completionMessage = "用户取消了下载";
            telemetryOutcome = LauncherTelemetryOutcome.Canceled;
            telemetryFailure = LauncherTelemetryFailureCode.UserCanceled;
            ClientStatusText = "下载已取消";
            ShowToast("下载任务已取消");
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            telemetryFailure = exception is HttpRequestException or TaskCanceledException
                ? LauncherTelemetryFailureCode.NetworkUnavailable
                : LauncherTelemetryFailureCode.IoFailure;
            ClientStatusText = "安装未完成";
            ShowToast("客户端安装中断，重新操作会从已下载位置继续");
        }
        catch (Exception exception)
        {
            Trace.TraceError(
                "Unexpected client installation failure: {0}",
                exception);
            ClientStatusText = "安装已安全停止";
            completionMessage = "启动器遇到未预期的安装错误";
            ShowToast("安装已安全停止，客户端当前版本没有被替换");
        }
        finally
        {
            var completedBytes = ActiveDownload?.CompletedBytes;
            completionMessage ??= succeeded ? null : ClientStatusText;
            CompleteActiveDownload(completionStatus, completionMessage);
            await _telemetryService.RecordAsync(
                isRepair
                    ? LauncherTelemetryEventType.Repair
                    : LauncherTelemetryEventType.Install,
                telemetryOutcome,
                telemetryFailure,
                profile.Id,
                profile.Version,
                telemetryClock.Elapsed,
                completedBytes);
            _isInstallingClient = false;
            _activeInstallCancellation?.Dispose();
            _activeInstallCancellation = null;
            CancelDownloadCommand.RaiseCanExecuteChanged();
            IsProgressActive = false;
            UpdatePrimaryActionForState();
        }

        return succeeded;
    }

    public async Task<bool> RollbackSelectedProfileAsync()
    {
        var selectedServer = SelectedServer;
        if (selectedServer is null ||
            !_clientProfiles.TryGetValue(selectedServer.ClientProfileId, out var profile))
        {
            ShowToast("当前服务器没有可回滚的客户端档案");
            return false;
        }

        if (_rollbackCandidate is null)
        {
            ShowToast("当前没有可回滚的上一版本");
            return false;
        }

        if (IsProgressActive)
        {
            return false;
        }

        if (_gameLauncherService.IsProfileRunning(profile.Id))
        {
            ShowToast("请先退出当前客户端再回滚版本");
            return false;
        }

        var activatedVersion = _rollbackCandidate.Version;
        var telemetryClock = Stopwatch.StartNew();
        var telemetryOutcome = LauncherTelemetryOutcome.Failure;
        var telemetryFailure = LauncherTelemetryFailureCode.Unexpected;
        IsProgressActive = true;
        UpdateProgress = 10;
        ClientStatusText = $"正在回滚到 v{activatedVersion}";
        PrimaryActionText = "正在回滚";
        var switched = false;

        try
        {
            var progress = new InlineProgress<ClientInstallProgress>(
                value => DispatchToUi(() => ApplyInstallProgress(value)));
            var activatedState = await _installationService.RollbackAsync(
                profile,
                ClientDirectory,
                progress);
            switched = true;
            _selectedProfileState = string.Equals(
                    activatedState.Version,
                    profile.Version,
                    StringComparison.Ordinal)
                ? LocalProfileState.Ready
                : LocalProfileState.UpdateRequired;
            _selectedProfileStateChecked = true;
            UpdateProgress = 100;
            ClientStatusText = $"已回滚到 v{activatedState.Version}";
            NotifySelectedProfileJavaPropertiesChanged();
            ShowToast($"客户端已回滚到 v{activatedState.Version}，存档与设置已保留");
            telemetryOutcome = LauncherTelemetryOutcome.Success;
            telemetryFailure = LauncherTelemetryFailureCode.None;
            return true;
        }
        catch (ProfileRollbackRuntimeException exception)
        {
            telemetryFailure = LauncherTelemetryFailureCode.RuntimePreparationFailed;
            switched = true;
            _selectedProfileState = LocalProfileState.UpdateRequired;
            _selectedProfileStateChecked = true;
            NotifySelectedProfileJavaPropertiesChanged();
            ClientStatusText = $"已回滚到 v{exception.ActivatedState.Version}，Java 待修复";
            ShowToast("版本已回滚，但配套 Java 未准备完成；点击修复客户端即可补齐");
            return true;
        }
        catch (ProfileRollbackUnavailableException)
        {
            telemetryFailure = LauncherTelemetryFailureCode.RollbackUnavailable;
            SetRollbackCandidate(null);
            ClientStatusText = "没有可回滚版本";
            ShowToast("上一版本不存在或未通过完整性检查");
        }
        catch (ProfileInstallInProgressException)
        {
            telemetryFailure = LauncherTelemetryFailureCode.InstallBusy;
            ClientStatusText = "客户端正在使用";
            ShowToast("另一个启动器窗口正在安装或回滚这个客户端");
        }
        catch (OperationCanceledException)
        {
            telemetryOutcome = LauncherTelemetryOutcome.Canceled;
            telemetryFailure = LauncherTelemetryFailureCode.UserCanceled;
            ClientStatusText = "回滚已取消";
            ShowToast("客户端回滚已取消，当前版本保持不变");
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            telemetryFailure = LauncherTelemetryFailureCode.IoFailure;
            ClientStatusText = "回滚未完成";
            ShowToast("无法安全切换版本，请退出 Minecraft 后重试");
        }
        catch (Exception exception)
        {
            Trace.TraceError(
                "Unexpected client rollback failure: {0}",
                exception);
            ClientStatusText = "回滚已安全停止";
            ShowToast("回滚已安全停止，当前客户端版本没有被替换");
        }
        finally
        {
            await _telemetryService.RecordAsync(
                LauncherTelemetryEventType.Rollback,
                telemetryOutcome,
                telemetryFailure,
                profile.Id,
                activatedVersion,
                telemetryClock.Elapsed);
            IsProgressActive = false;
            await RefreshRollbackCandidateAsync(profile, selectedServer.Id);
            if (!switched)
            {
                UpdateProgress = _selectedProfileState == LocalProfileState.Ready ? 100 : 0;
            }
            UpdatePrimaryActionForState();
        }

        return false;
    }

    private async Task LaunchSelectedServerAsync()
    {
        if (IsProgressActive || SelectedServer is null)
        {
            return;
        }

        var selectedServer = SelectedServer;
        var profileVersion = _clientProfiles.TryGetValue(
                selectedServer.ClientProfileId,
                out var selectedProfile)
            ? selectedProfile.Version
            : "unknown";
        var telemetryClock = Stopwatch.StartNew();
        var telemetryOutcome = LauncherTelemetryOutcome.Failure;
        var telemetryFailure = LauncherTelemetryFailureCode.Unexpected;
        var closeAfterLaunch = false;
        IsProgressActive = true;
        UpdateProgress = 0;
        ClientStatusText = "正在准备正版游戏会话";
        PrimaryActionText = "正在启动";
        var progress = new Progress<MinecraftLaunchProgress>(ApplyLaunchProgress);

        try
        {
            var launchSession = await GetMinecraftLaunchSessionWithRefreshAsync();
            SetCurrentAccount(_authenticationService.CurrentAccount);
            var runningGame = _gameLauncherService.GetRunningGame();
            if (runningGame is not null)
            {
                ClientStatusText = "正在安全关闭当前游戏";
                PrimaryActionText = "正在切换";
                var stopProgress = new Progress<MinecraftStopProgress>(
                    ApplyStopProgress);
                await _gameLauncherService.StopRunningGameAsync(
                    TimeSpan.FromSeconds(15),
                    stopProgress);
                _runningServerId = null;
                OnPropertyChanged(nameof(CanRollbackSelectedProfile));
                OnPropertyChanged(nameof(RollbackProfileToolTip));
            }

            await _gameLauncherService.LaunchAsync(
                new MinecraftLaunchRequest(
                    ClientDirectory,
                    selectedServer.ClientProfileId,
                    ParseMemoryInMiB(SelectedMemory),
                    launchSession,
                    GetSelectedProfileCustomJavaPath(),
                    selectedServer.Id),
                progress,
                async cancellationToken =>
                {
                    await _authenticationService.PrepareVelocityLaunchAsync(
                        selectedServer.Id,
                        cancellationToken);
                });
            _runningServerId = selectedServer.Id;
            OnPropertyChanged(nameof(CanRollbackSelectedProfile));
            OnPropertyChanged(nameof(RollbackProfileToolTip));
            UpdateProgress = 100;
            ClientStatusText = "游戏已启动";
            ShowToast($"正在进入 {selectedServer.Name}");
            telemetryOutcome = LauncherTelemetryOutcome.Success;
            telemetryFailure = LauncherTelemetryFailureCode.None;
            if (CloseLauncherAfterGameStart)
            {
                closeAfterLaunch = true;
            }
        }
        catch (LauncherAuthenticationRequiredException)
        {
            telemetryFailure = LauncherTelemetryFailureCode.AuthenticationRequired;
            ClientStatusText = "需要登录后启动";
            ActivePage = LauncherPage.Account;
            ShowToast("请先登录赫朝账号");
        }
        catch (MinecraftIdentityLinkRequiredException)
        {
            telemetryFailure = LauncherTelemetryFailureCode.MinecraftIdentityRequired;
            ClientStatusText = "需要绑定正版身份";
            ActivePage = LauncherPage.Account;
            ShowToast("请先在赫朝账户页绑定 Minecraft 正版身份");
        }
        catch (MicrosoftReauthenticationRequiredException)
        {
            telemetryFailure =
                LauncherTelemetryFailureCode.MicrosoftReauthenticationRequired;
            ClientStatusText = "游戏凭据刷新未完成";
            ShowToast("请重新点击进入服务器，并在浏览器中完成 Microsoft 正版认证");
        }
        catch (MicrosoftAuthenticationNotConfiguredException)
        {
            telemetryFailure = LauncherTelemetryFailureCode.MicrosoftNotConfigured;
            ClientStatusText = "Microsoft 登录尚未配置";
            ShowToast("当前启动器无法进行 Microsoft 正版认证，请更新启动器");
        }
        catch (MicrosoftSignInCanceledException)
        {
            telemetryOutcome = LauncherTelemetryOutcome.Canceled;
            telemetryFailure = LauncherTelemetryFailureCode.MicrosoftCanceled;
            ClientStatusText = "已取消正版认证";
            ShowToast("已取消 Microsoft 正版认证，游戏没有启动");
        }
        catch (MicrosoftAccountMismatchException exception)
        {
            telemetryFailure = LauncherTelemetryFailureCode.MicrosoftAccountMismatch;
            ClientStatusText = "Microsoft 账号不匹配";
            ActivePage = LauncherPage.Account;
            ShowToast(
                $"当前选择的是 {exception.AuthenticatedMinecraftName}，请登录已绑定玩家 {exception.LinkedMinecraftName} 对应的 Microsoft 账号");
        }
        catch (MicrosoftSignInFailedException)
        {
            telemetryFailure = LauncherTelemetryFailureCode.MicrosoftSignInFailed;
            ClientStatusText = "Microsoft 登录失败";
            ShowToast("Microsoft 登录失败，请稍后重试");
        }
        catch (MinecraftSignInException exception)
        {
            telemetryFailure = exception.Failure == MinecraftSignInFailure.ServiceUnavailable
                ? LauncherTelemetryFailureCode.NetworkUnavailable
                : LauncherTelemetryFailureCode.MinecraftOwnership;
            ClientStatusText = "正版身份验证失败";
            ShowToast(GetMinecraftSignInError(exception.Failure));
        }
        catch (MinecraftLaunchSessionExpiredException)
        {
            telemetryFailure = LauncherTelemetryFailureCode.MinecraftSessionExpired;
            ClientStatusText = "游戏登录已过期";
            ShowToast("Minecraft 游戏凭据已过期，请重新启动");
        }
        catch (LauncherApiException exception)
        {
            telemetryFailure = LauncherTelemetryFailureCode.LaunchAuthorizationFailed;
            ClientStatusText = "进服授权失败";
            ShowToast(exception.ApiDetail ?? "暂时无法取得服务器进入权限");
        }
        catch (MinecraftAlreadyRunningException)
        {
            telemetryFailure = LauncherTelemetryFailureCode.GameAlreadyRunning;
            ClientStatusText = "游戏正在运行";
            ShowToast("当前游戏仍在运行，未启动新的客户端");
        }
        catch (MinecraftProcessStopException)
        {
            telemetryFailure = LauncherTelemetryFailureCode.GameAlreadyRunning;
            ClientStatusText = "无法安全关闭当前游戏";
            ShowToast("当前游戏未能退出，已取消切换且没有申请新服授权");
        }
        catch (MinecraftLaunchException exception)
        {
            telemetryFailure = exception.Failure switch
            {
                MinecraftLaunchFailure.InvalidProfile =>
                    LauncherTelemetryFailureCode.InvalidProfile,
                MinecraftLaunchFailure.InvalidJavaSelection =>
                    LauncherTelemetryFailureCode.InvalidJavaSelection,
                MinecraftLaunchFailure.RuntimePreparation =>
                    LauncherTelemetryFailureCode.RuntimePreparationFailed,
                MinecraftLaunchFailure.NativeLibraryPreparation =>
                    LauncherTelemetryFailureCode.RuntimePreparationFailed,
                _ => LauncherTelemetryFailureCode.ProcessCreationFailed
            };
            ClientStatusText = exception.Failure switch
            {
                MinecraftLaunchFailure.InvalidProfile => "客户端启动信息无效",
                MinecraftLaunchFailure.InvalidJavaSelection => "自定义 Java 不兼容",
                MinecraftLaunchFailure.RuntimePreparation => "Java 准备失败",
                MinecraftLaunchFailure.NativeLibraryPreparation => "游戏原生库准备失败",
                MinecraftLaunchFailure.ProcessCreation => "无法生成游戏进程",
                _ => "游戏启动失败"
            };
            ShowToast(exception.Failure switch
            {
                MinecraftLaunchFailure.InvalidProfile =>
                    "客户端不完整，请先修复客户端",
                MinecraftLaunchFailure.InvalidJavaSelection =>
                    $"当前客户端需要 Java {GetSelectedProfileJavaMajorVersion()}，请重新选择或恢复自动 Java",
                MinecraftLaunchFailure.NativeLibraryPreparation =>
                    "原生库未通过完整性或依赖检查，请使用“修复客户端”后重试",
                _ => "游戏启动未完成，请检查网络或使用客户端修复"
            });
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            telemetryFailure = LauncherTelemetryFailureCode.NetworkUnavailable;
            ClientStatusText = "进服授权服务不可用";
            ShowToast("无法连接赫朝授权服务，请稍后再试");
        }
        finally
        {
            await _telemetryService.RecordAsync(
                LauncherTelemetryEventType.Launch,
                telemetryOutcome,
                telemetryFailure,
                selectedServer.ClientProfileId,
                profileVersion,
                telemetryClock.Elapsed);
            IsProgressActive = false;
            UpdatePrimaryActionForState();
        }

        if (closeAfterLaunch)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task<MinecraftLaunchSession> GetMinecraftLaunchSessionWithRefreshAsync()
    {
        try
        {
            return await _authenticationService.GetMinecraftLaunchSessionAsync();
        }
        catch (MicrosoftReauthenticationRequiredException)
        {
            ClientStatusText = "请在浏览器中刷新 Microsoft 游戏凭据";
            ShowToast("游戏凭据已过期，请在浏览器中完成 Microsoft 正版认证");
            var cancellation = new CancellationTokenSource();
            _microsoftSignInCancellation = cancellation;
            IsMicrosoftSignInVisible = true;
            CancelMicrosoftSignInCommand.RaiseCanExecuteChanged();
            try
            {
                return await _authenticationService.RefreshMinecraftLaunchSessionAsync(
                    cancellation.Token);
            }
            finally
            {
                if (ReferenceEquals(_microsoftSignInCancellation, cancellation))
                {
                    _microsoftSignInCancellation = null;
                }

                IsMicrosoftSignInVisible = false;
                cancellation.Dispose();
            }
        }
    }

    private void ApplyInstallProgress(ClientInstallProgress progress)
    {
        UpdateProgress = progress.Percent;
        ActiveDownload?.Update(
            progress.Percent,
            progress.CompletedBytes,
            progress.TotalBytes,
            string.IsNullOrWhiteSpace(progress.CurrentPath)
                ? string.Empty
                : Path.GetFileName(progress.CurrentPath));
        ClientStatusText = progress.Phase switch
        {
            ClientInstallPhase.Checking => "正在检查本地文件",
            ClientInstallPhase.Downloading => string.IsNullOrWhiteSpace(progress.CurrentPath)
                ? "正在下载客户端"
                : $"正在下载 {Path.GetFileName(progress.CurrentPath)}",
            ClientInstallPhase.Staging => "正在准备客户端",
            ClientInstallPhase.Switching => "正在切换客户端版本",
            ClientInstallPhase.PreparingRuntime => string.IsNullOrWhiteSpace(progress.CurrentPath)
                ? "正在安装配套 Java"
                : $"正在安装 {progress.CurrentPath}",
            ClientInstallPhase.Complete => "客户端已就绪",
            _ => ClientStatusText
        };
    }

    private void BeginDownload(ClientProfileSummary profile, bool isRepair)
    {
        ActiveDownload = new DownloadJobViewModel(
            Guid.NewGuid(),
            profile.Id,
            isRepair ? $"修复 · {profile.DisplayName}" : profile.DisplayName,
            profile.Version,
            DateTimeOffset.UtcNow,
            DownloadJobStatus.Running,
            0,
            profile.DownloadBytes,
            string.Empty);
        if (OpenDownloadsWhenInstalling)
        {
            ActivePage = LauncherPage.Downloads;
        }
        PersistDownloadHistory();
    }

    private void CompleteActiveDownload(
        DownloadJobStatus status,
        string? failureMessage)
    {
        var download = ActiveDownload;
        if (download is null)
        {
            return;
        }

        download.Finish(status, failureMessage);
        DownloadHistory.Insert(0, download);
        ActiveDownload = null;
        PersistDownloadHistory();
        NotifyDownloadHistoryChanged();
    }

    private void CancelActiveDownload()
    {
        _activeInstallCancellation?.Cancel();
    }

    private void ClearDownloadHistory()
    {
        DownloadHistory.Clear();
        _downloadHistoryStore.Save([]);
        NotifyDownloadHistoryChanged();
        ShowToast("下载历史已清空");
    }

    private void LoadDownloadHistory()
    {
        var needsRewrite = false;
        foreach (var record in _downloadHistoryStore
                     .Load()
                     .OrderByDescending(record => record.CompletedAt ?? record.StartedAt))
        {
            var status = record.Status;
            var completedAt = record.CompletedAt;
            var failureMessage = record.FailureMessage;
            if (status == DownloadJobStatus.Running)
            {
                status = DownloadJobStatus.Failed;
                completedAt = DateTimeOffset.UtcNow;
                failureMessage = "启动器在任务完成前退出";
                needsRewrite = true;
            }

            DownloadHistory.Add(new DownloadJobViewModel(
                record.Id,
                record.ProfileId,
                record.DisplayName,
                record.Version,
                record.StartedAt,
                status,
                record.CompletedBytes,
                record.TotalBytes,
                record.CurrentFile,
                completedAt,
                failureMessage));
        }

        if (needsRewrite)
        {
            PersistDownloadHistory();
        }

        NotifyDownloadHistoryChanged();
    }

    private void PersistDownloadHistory()
    {
        var downloads = ActiveDownload is null
            ? DownloadHistory.AsEnumerable()
            : DownloadHistory.Prepend(ActiveDownload);
        _downloadHistoryStore.Save(downloads.Select(download =>
            new DownloadHistoryRecord(
                download.Id,
                download.ProfileId,
                download.DisplayName,
                download.Version,
                download.StartedAt,
                download.CompletedAt,
                download.Status,
                download.CompletedBytes,
                download.TotalBytes,
                download.CurrentFile,
                download.FailureMessage)));
    }

    private void NotifyDownloadHistoryChanged()
    {
        OnPropertyChanged(nameof(HasDownloadHistory));
        OnPropertyChanged(nameof(HasNoDownloadHistory));
        OnPropertyChanged(nameof(DownloadHistoryCount));
        OnPropertyChanged(nameof(DownloadQueueStatusText));
        ClearDownloadHistoryCommand.RaiseCanExecuteChanged();
    }

    private void ApplyLaunchProgress(MinecraftLaunchProgress progress)
    {
        UpdateProgress = progress.Percent;
        ClientStatusText = progress.Phase switch
        {
            MinecraftLaunchPhase.LoadingProfile => "正在读取客户端",
            MinecraftLaunchPhase.PreparingRuntime => "正在准备 Java 21",
            MinecraftLaunchPhase.BuildingProcess => "正在生成游戏进程",
            MinecraftLaunchPhase.Authorizing => "正在申请进服授权",
            MinecraftLaunchPhase.Starting => "正在启动 Minecraft",
            _ => ClientStatusText
        };
    }

    private void ApplyStopProgress(MinecraftStopProgress progress)
    {
        ClientStatusText = progress.Phase switch
        {
            MinecraftStopPhase.RequestingExit => "正在请求当前游戏退出",
            MinecraftStopPhase.WaitingForExit => "正在等待当前游戏保存并退出",
            MinecraftStopPhase.ForcingExit => "当前游戏无响应，正在结束进程",
            MinecraftStopPhase.Complete => "当前游戏已退出",
            _ => ClientStatusText
        };
    }

    private void GameLauncherService_OnProcessExited(
        object? sender,
        MinecraftProcessExitedEventArgs eventArgs)
    {
        var record = new GameExitRecord(
            Guid.NewGuid(),
            eventArgs.ProfileId,
            eventArgs.ProcessId,
            eventArgs.ExitCode,
            eventArgs.StartedAt,
            eventArgs.ExitedAt);
        _ = RecordGameExitAsync(record, eventArgs.ExitKind);
    }

    private async Task RecordGameExitAsync(
        GameExitRecord record,
        MinecraftProcessExitKind exitKind)
    {
        try
        {
            await _gameDiagnosticsService.RecordExitAsync(record);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
        }

        var profileVersion = _clientProfiles.TryGetValue(
                record.ProfileId,
                out var profile)
            ? profile.Version
            : "unknown";
        await _telemetryService.RecordAsync(
            LauncherTelemetryEventType.GameExit,
            exitKind != MinecraftProcessExitKind.Natural || record.ExitCode == 0
                ? LauncherTelemetryOutcome.Success
                : LauncherTelemetryOutcome.Failure,
            exitKind != MinecraftProcessExitKind.Natural || record.ExitCode == 0
                ? LauncherTelemetryFailureCode.None
                : record.ExitCode is null
                    ? LauncherTelemetryFailureCode.Unexpected
                    : LauncherTelemetryFailureCode.GameExitedNonZero,
            record.ProfileId,
            profileVersion,
            record.ExitedAt - record.StartedAt);

        DispatchToUi(() =>
        {
            _latestGameExit = record;
            OnPropertyChanged(nameof(LatestGameExitText));
            OnPropertyChanged(nameof(CanRollbackSelectedProfile));
            OnPropertyChanged(nameof(RollbackProfileToolTip));
            var runningGame = _gameLauncherService.GetRunningGame();
            _runningServerId = runningGame?.ServerId;
            if (runningGame is null && !IsProgressActive)
            {
                ClientStatusText =
                    exitKind != MinecraftProcessExitKind.Natural
                        ? "游戏已退出"
                        : record.ExitCode == 0
                    ? "游戏已退出"
                    : "游戏异常退出";
                UpdatePrimaryActionForState();
            }

            if (exitKind == MinecraftProcessExitKind.Natural &&
                record.ExitCode != 0)
            {
                ShowToast("Minecraft 异常退出，可在设置页生成脱敏诊断包");
            }
        });
    }

    private bool CanCreateDiagnosticBundle() =>
        SelectedServer is not null &&
        _selectedProfileState != LocalProfileState.Missing &&
        !IsDiagnosticBusy;

    private async void StartCreateDiagnosticBundle()
    {
        var selectedServer = SelectedServer;
        if (selectedServer is null || !CanCreateDiagnosticBundle())
        {
            return;
        }

        IsDiagnosticBusy = true;
        try
        {
            var sensitiveValues = new List<string>();
            if (_currentAccount is not null)
            {
                sensitiveValues.Add(_currentAccount.UserId.ToString("D"));
                sensitiveValues.Add(_currentAccount.Username);
                sensitiveValues.Add(_currentAccount.DisplayName);
                if (!string.IsNullOrWhiteSpace(_currentAccount.Email))
                {
                    sensitiveValues.Add(_currentAccount.Email);
                }

                if (_currentAccount.MinecraftUuid is Guid minecraftUuid)
                {
                    sensitiveValues.Add(minecraftUuid.ToString("D"));
                    sensitiveValues.Add(minecraftUuid.ToString("N"));
                }

                if (!string.IsNullOrWhiteSpace(_currentAccount.MinecraftName))
                {
                    sensitiveValues.Add(_currentAccount.MinecraftName);
                }
            }

            var matchingExit = _latestGameExit?.ProfileId == selectedServer.ClientProfileId
                ? _latestGameExit
                : null;
            var result = await _gameDiagnosticsService.CreateBundleAsync(
                new GameDiagnosticBundleRequest(
                    ClientDirectory,
                    selectedServer.ClientProfileId,
                    matchingExit,
                    sensitiveValues));
            _latestDiagnosticBundle = result;
            _latestDiagnosticProfileId = selectedServer.ClientProfileId;
            _diagnosticUploadStatus =
                $"{Path.GetFileName(result.BundlePath)} · {FormatFileSize(result.Size)} · 尚未上传";
            OnPropertyChanged(nameof(DiagnosticUploadStatus));
            OnPropertyChanged(nameof(CanUploadDiagnosticBundle));
            OpenDirectory(_gameDiagnosticsService.DiagnosticsDirectory);
            ShowToast(result.IncludedCrashReport
                ? "脱敏诊断包已生成，并包含最新崩溃报告"
                : "脱敏诊断包已生成");
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            ArgumentException or InvalidDataException or ManifestFormatException)
        {
            ShowToast("无法生成诊断包，请先确认客户端已安装且日志可读取");
        }
        finally
        {
            IsDiagnosticBusy = false;
        }
    }

    public async Task<bool> UploadLatestDiagnosticBundleAsync()
    {
        var bundle = _latestDiagnosticBundle;
        var profileId = _latestDiagnosticProfileId;
        if (!CanUploadDiagnosticBundle ||
            bundle is null ||
            string.IsNullOrWhiteSpace(profileId))
        {
            return false;
        }

        IsDiagnosticBusy = true;
        _diagnosticUploadStatus = "正在校验并上传诊断包…";
        OnPropertyChanged(nameof(DiagnosticUploadStatus));
        try
        {
            var receipt = await _diagnosticUploadService.UploadAsync(
                bundle,
                profileId);
            _latestDiagnosticBundle = null;
            _latestDiagnosticProfileId = null;
            var shortId = receipt.UploadId.ToString("N")[..8];
            _diagnosticUploadStatus =
                $"已上传 · 编号 {shortId} · " +
                $"{receipt.ExpiresAt.ToLocalTime():yyyy-MM-dd} 自动删除";
            OnPropertyChanged(nameof(DiagnosticUploadStatus));
            ShowToast("诊断包已安全上传，管理员下载会写入审计记录");
            return true;
        }
        catch (LauncherAuthenticationRequiredException)
        {
            ClearAuthenticatedState();
            _diagnosticUploadStatus = "登录已过期，诊断包仍保留在本机。";
            OnPropertyChanged(nameof(DiagnosticUploadStatus));
            ShowToast("请重新登录后再上传诊断包");
            return false;
        }
        catch (LauncherApiException exception)
        {
            _diagnosticUploadStatus = "上传未完成，诊断包仍保留在本机。";
            OnPropertyChanged(nameof(DiagnosticUploadStatus));
            ShowToast(exception.ApiDetail ?? "诊断上传暂时不可用");
            return false;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            InvalidDataException or HttpRequestException or TaskCanceledException)
        {
            _diagnosticUploadStatus = "上传未完成，诊断包仍保留在本机。";
            OnPropertyChanged(nameof(DiagnosticUploadStatus));
            ShowToast("诊断上传失败，请检查网络后重试");
            return false;
        }
        finally
        {
            IsDiagnosticBusy = false;
            OnPropertyChanged(nameof(CanUploadDiagnosticBundle));
        }
    }

    private void OpenDiagnosticsDirectory()
    {
        try
        {
            Directory.CreateDirectory(_gameDiagnosticsService.DiagnosticsDirectory);
            OpenDirectory(_gameDiagnosticsService.DiagnosticsDirectory);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            System.ComponentModel.Win32Exception)
        {
            ShowToast("暂时无法打开诊断目录");
        }
    }

    private static void OpenDirectory(string path)
    {
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private void DispatchToUi(Action action)
    {
        if (_uiContext is null || ReferenceEquals(SynchronizationContext.Current, _uiContext))
        {
            action();
            return;
        }

        _uiContext.Post(_ => action(), null);
    }

    private async Task<bool> RefreshClientStateAsync()
    {
        var selectedServer = SelectedServer;
        if (selectedServer is null || IsProgressActive ||
            !_clientProfiles.TryGetValue(selectedServer.ClientProfileId, out var profile))
        {
            return false;
        }

        try
        {
            var stateTask = _installationService.GetLocalStateAsync(
                profile,
                ClientDirectory);
            var rollbackTask = _installationService.GetRollbackCandidateAsync(
                profile,
                ClientDirectory);
            await Task.WhenAll(stateTask, rollbackTask);
            var state = await stateTask;
            var rollbackCandidate = await rollbackTask;
            if (SelectedServer?.Id != selectedServer.Id || IsProgressActive)
            {
                return false;
            }

            _selectedProfileState = state;
            _selectedProfileStateChecked = true;
            SetRollbackCandidate(rollbackCandidate);
            CreateDiagnosticBundleCommand.RaiseCanExecuteChanged();
            switch (state)
            {
                case LocalProfileState.Ready:
                    UpdateProgress = 100;
                    ClientStatusText = "客户端已就绪";
                    break;
                case LocalProfileState.UpdateRequired:
                    UpdateProgress = 0;
                    ClientStatusText = "发现新版本";
                    break;
                default:
                    UpdateProgress = 0;
                    ClientStatusText = "尚未安装";
                    break;
            }
            UpdatePrimaryActionForState();
            return true;
        }
        catch (Exception exception)
        {
            Trace.TraceError(
                "Client state check failed for profile {0}: {1}",
                profile.Id,
                exception);
            if (SelectedServer?.Id == selectedServer.Id && !IsProgressActive)
            {
                _selectedProfileStateChecked = false;
                UpdateProgress = 0;
                ClientStatusText = "客户端检查未完成";
                UpdatePrimaryActionForState();
            }
            return false;
        }
    }

    private async Task RefreshRollbackCandidateAsync(
        ClientProfileSummary profile,
        string? selectedServerId)
    {
        try
        {
            var candidate = await _installationService.GetRollbackCandidateAsync(
                profile,
                ClientDirectory);
            if (SelectedServer?.Id == selectedServerId)
            {
                SetRollbackCandidate(candidate);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            if (SelectedServer?.Id == selectedServerId)
            {
                SetRollbackCandidate(null);
            }
        }
    }

    private void OpenClientDirectory()
    {
        try
        {
            var expandedPath = Environment.ExpandEnvironmentVariables(ClientDirectory);
            Directory.CreateDirectory(expandedPath);
            Process.Start(new ProcessStartInfo(expandedPath) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            ShowToast("暂时无法打开游戏数据目录");
        }
    }

    public void UpdateClientDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var normalized = Path.GetFullPath(
            Environment.ExpandEnvironmentVariables(path.Trim()));
        if (string.Equals(
                normalized.TrimEnd(Path.DirectorySeparatorChar),
                Environment.ExpandEnvironmentVariables(ClientDirectory)
                    .TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _clientDirectory = normalized;
        _selectedProfileState = LocalProfileState.Missing;
        _selectedProfileStateChecked = false;
        OnPropertyChanged(nameof(ClientDirectory));
        OnPropertyChanged(nameof(SelectedProfileGameDirectory));
        NotifySelectedProfileJavaPropertiesChanged();
        CreateDiagnosticBundleCommand.RaiseCanExecuteChanged();
        UpdateProgress = 0;
        ClientStatusText = "正在检查客户端";
        UpdatePrimaryActionForState();
        SaveSettings();
        _ = RefreshClientStateAsync();
        ShowToast("游戏数据目录已更新");
    }

    private void ResetLauncherSettings()
    {
        SelectedMemory = "6 GB";
        _clientDirectory = JsonLauncherSettingsStore.DefaultClientDataDirectory;
        _profileJavaPaths.Clear();
        _selectedProfileState = LocalProfileState.Missing;
        _selectedProfileStateChecked = false;
        OnPropertyChanged(nameof(ClientDirectory));
        OnPropertyChanged(nameof(SelectedProfileGameDirectory));
        NotifySelectedProfileJavaPropertiesChanged();
        CreateDiagnosticBundleCommand.RaiseCanExecuteChanged();
        UpdateProgress = 0;
        ClientStatusText = "正在检查客户端";
        UpdatePrimaryActionForState();
        CheckForUpdates = true;
        KeepDownloadsAfterClose = true;
        CloseLauncherAfterGameStart = false;
        OpenDownloadsWhenInstalling = true;
        SelectedStartupPage = "服务器";
        SaveSettings();
        _ = RefreshClientStateAsync();
        ShowToast("启动器设置已恢复默认");
    }

    private bool CanUseSelectedServer() =>
        !IsProgressActive && SelectedServer?.Status == ServerStatus.Online;

    public async Task<bool> LoginAccountAsync(
        string usernameOrEmail,
        string password)
    {
        if (IsAccountBusy)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(usernameOrEmail) ||
            string.IsNullOrEmpty(password))
        {
            SetAccountFormStatus("请填写赫朝账号和密码。", isError: true);
            return false;
        }

        IsAccountBusy = true;
        SetAccountFormStatus("正在登录赫朝账号…", isError: false);
        try
        {
            var account = await _authenticationService.LoginAsync(
                usernameOrEmail.Trim(),
                password);
            SetCurrentAccount(account);
            SetAccountFormStatus(string.Empty, isError: false);
            await LoadCatalogAsync(userInitiated: true);
            ShowToast($"欢迎回来，{account.DisplayName}");
            return true;
        }
        catch (LauncherApiException exception)
        {
            SetAccountFormStatus(
                exception.ApiDetail ?? "赫朝账号或密码不正确。",
                isError: true);
            return false;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException or IOException)
        {
            SetAccountFormStatus("暂时无法连接赫朝账号服务。", isError: true);
            return false;
        }
        catch (Exception exception)
        {
            Trace.TraceError("Unexpected Hechao account login failure: {0}", exception);
            SetAccountFormStatus(
                "登录未完成，请检查账号信息后重试。",
                isError: true);
            return false;
        }
        finally
        {
            IsAccountBusy = false;
        }
    }

    public async Task<bool> RegisterAccountAsync(
        string username,
        string displayName,
        string password,
        string email,
        string code)
    {
        if (IsAccountBusy)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(username) ||
            string.IsNullOrWhiteSpace(displayName) ||
            string.IsNullOrWhiteSpace(email) ||
            string.IsNullOrEmpty(password) ||
            string.IsNullOrWhiteSpace(code))
        {
            SetAccountFormStatus(
                "请完整填写账号名、显示名称、邮箱、验证码和密码。",
                isError: true);
            return false;
        }

        IsAccountBusy = true;
        SetAccountFormStatus("正在创建赫朝账号…", isError: false);
        try
        {
            var account = await _authenticationService.RegisterAsync(
                username.Trim(),
                displayName.Trim(),
                password,
                email.Trim(),
                code.Trim());
            SetCurrentAccount(account);
            SetAccountFormStatus(string.Empty, isError: false);
            await LoadCatalogAsync(userInitiated: true);
            ShowToast($"赫朝账号 @{account.Username} 已创建，并已同步社区");
            return true;
        }
        catch (ForumRegistrationException exception)
        {
            SetAccountFormStatus(exception.Detail, isError: true);
            return false;
        }
        catch (LauncherApiException exception)
        {
            SetAccountFormStatus(
                exception.ApiDetail ??
                "账号已经创建，但自动登录失败。请切换到登录页重试。",
                isError: true);
            return false;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException or IOException)
        {
            SetAccountFormStatus("暂时无法连接赫朝账号服务。", isError: true);
            return false;
        }
        finally
        {
            IsAccountBusy = false;
        }
    }

    public async Task<bool> SendRegistrationCodeAsync(string email)
    {
        if (IsAccountBusy)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            SetAccountFormStatus("请先填写用于赫朝账号的邮箱。", isError: true);
            return false;
        }

        IsAccountBusy = true;
        SetAccountFormStatus("正在发送邮箱验证码…", isError: false);
        try
        {
            await _authenticationService.SendRegistrationCodeAsync(email.Trim());
            SetAccountFormStatus(
                "验证码已发送，请检查收件箱和垃圾邮件。",
                isError: false);
            return true;
        }
        catch (ForumRegistrationException exception)
        {
            SetAccountFormStatus(exception.Detail, isError: true);
            return false;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException or IOException)
        {
            SetAccountFormStatus("暂时无法连接赫朝社区，请稍后再试。", isError: true);
            return false;
        }
        finally
        {
            IsAccountBusy = false;
        }
    }

    private async Task StartMinecraftLinkAsync()
    {
        if (!IsAuthenticated || IsMinecraftLinked || IsAccountBusy)
        {
            return;
        }

        IsAccountBusy = true;
        var cancellation = new CancellationTokenSource();
        _microsoftSignInCancellation = cancellation;
        IsMicrosoftSignInVisible = true;
        CancelMicrosoftSignInCommand.RaiseCanExecuteChanged();
        SetAccountFormStatus(
            "请在浏览器中完成 Microsoft 正版认证。",
            isError: false);
        try
        {
            var account = await _authenticationService.LinkMinecraftAsync(
                cancellation.Token);
            SetCurrentAccount(account);
            SetAccountFormStatus(string.Empty, isError: false);
            await LoadCatalogAsync(userInitiated: true);
            ShowToast($"已绑定 Minecraft 玩家 {account.MinecraftName}");
        }
        catch (MicrosoftAuthenticationNotConfiguredException)
        {
            SetAccountFormStatus("Microsoft 登录应用尚未完成配置。", isError: true);
        }
        catch (MicrosoftSignInCanceledException)
        {
            SetAccountFormStatus("已取消 Microsoft 正版认证。", isError: false);
        }
        catch (MicrosoftSignInFailedException)
        {
            SetAccountFormStatus("Microsoft 登录失败，请稍后重试。", isError: true);
        }
        catch (MinecraftSignInException exception)
        {
            SetAccountFormStatus(GetMinecraftSignInError(exception.Failure), isError: true);
        }
        catch (LauncherApiException exception)
        {
            SetAccountFormStatus(
                exception.ApiDetail ?? "Minecraft 身份绑定失败。",
                isError: true);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException or IOException)
        {
            SetAccountFormStatus("正版认证服务暂时不可用。", isError: true);
        }
        catch (Exception exception)
        {
            Trace.TraceError(
                "Unexpected Microsoft sign-in failure: {0}",
                exception);
            SetAccountFormStatus(
                "Microsoft 登录未完成，请关闭浏览器页面后重试。",
                isError: true);
        }
        finally
        {
            if (ReferenceEquals(_microsoftSignInCancellation, cancellation))
            {
                _microsoftSignInCancellation = null;
            }

            IsMicrosoftSignInVisible = false;
            cancellation.Dispose();
            IsAccountBusy = false;
        }
    }

    private void CancelMicrosoftSignIn()
    {
        if (_microsoftSignInCancellation is null)
        {
            return;
        }

        SetAccountFormStatus("正在取消 Microsoft 正版认证…", isError: false);
        _microsoftSignInCancellation.Cancel();
        CancelMicrosoftSignInCommand.RaiseCanExecuteChanged();
    }

    private void HandleUnexpectedPrimaryActionError(Exception exception)
    {
        Trace.TraceError("Unexpected launcher primary action failure: {0}", exception);
        _activeInstallCancellation?.Cancel();
        IsProgressActive = false;
        ClientStatusText = "操作已安全停止";
        UpdatePrimaryActionForState();
        ShowToast("操作已安全停止，请重试；现有客户端文件未被替换");
    }

    private void HandleUnexpectedMicrosoftSignInError(Exception exception)
    {
        Trace.TraceError("Unhandled Microsoft sign-in command failure: {0}", exception);
        var cancellation = _microsoftSignInCancellation;
        _microsoftSignInCancellation = null;
        cancellation?.Cancel();
        cancellation?.Dispose();
        IsMicrosoftSignInVisible = false;
        IsAccountBusy = false;
        SetAccountFormStatus(
            "Microsoft 登录未完成，请关闭浏览器页面后重试。",
            isError: true);
    }

    private void BeginMinecraftUnlink()
    {
        if (!IsAuthenticated || !IsMinecraftLinked || IsAccountBusy)
        {
            return;
        }

        IsMinecraftUnlinkFormVisible = true;
        SetAccountFormStatus(
            "解除绑定后，所有设备会退出，重新进服前需要再次绑定正版身份。",
            isError: false);
    }

    private void CancelMinecraftUnlink()
    {
        IsMinecraftUnlinkFormVisible = false;
        SetAccountFormStatus(string.Empty, isError: false);
    }

    public async Task<bool> UnlinkMinecraftAsync(string currentPassword)
    {
        if (!IsAuthenticated ||
            !IsMinecraftLinked ||
            IsAccountBusy ||
            !IsMinecraftUnlinkFormVisible)
        {
            return false;
        }

        if (string.IsNullOrEmpty(currentPassword))
        {
            SetAccountFormStatus("请输入当前赫朝账号密码。", isError: true);
            return false;
        }

        IsAccountBusy = true;
        SetAccountFormStatus("正在解除 Minecraft 正版身份…", isError: false);
        try
        {
            await _authenticationService.UnlinkMinecraftAsync(currentPassword);
            IsMinecraftUnlinkFormVisible = false;
            ClearAuthenticatedState();
            SetAccountFormStatus(string.Empty, isError: false);
            ShowToast("Minecraft 绑定已解除，所有设备均已退出");
            return true;
        }
        catch (LauncherAuthenticationRequiredException)
        {
            IsMinecraftUnlinkFormVisible = false;
            ClearAuthenticatedState();
            SetAccountFormStatus("登录已过期，请重新登录。", isError: true);
            return false;
        }
        catch (LauncherApiException exception)
        {
            SetAccountFormStatus(
                exception.ApiDetail ?? "暂时无法解除 Minecraft 绑定。",
                isError: true);
            return false;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException or IOException)
        {
            SetAccountFormStatus("账号安全服务暂时不可用。", isError: true);
            return false;
        }
        finally
        {
            IsAccountBusy = false;
        }
    }

    private async void StartAccountLogout()
    {
        if (!IsAuthenticated || IsAccountBusy)
        {
            return;
        }

        IsAccountBusy = true;
        try
        {
            await _authenticationService.LogoutAsync();
            ClearAuthenticatedState();
            SetAccountFormStatus(string.Empty, isError: false);
            ShowToast("已退出赫朝账号");
        }
        finally
        {
            IsAccountBusy = false;
        }
    }

    private async void StartLogoutAllDevices()
    {
        if (!IsAuthenticated || IsAccountBusy)
        {
            return;
        }

        IsAccountBusy = true;
        SetAccountFormStatus("正在撤销所有设备的登录会话…", isError: false);
        try
        {
            var response = await _authenticationService.LogoutAllDevicesAsync();
            ClearAuthenticatedState();
            SetAccountFormStatus(string.Empty, isError: false);
            var deviceCount = Math.Max(1, response.RevokedLauncherSessions);
            ShowToast($"已退出所有设备，共撤销 {deviceCount} 个启动器会话");
        }
        catch (LauncherAuthenticationRequiredException)
        {
            ClearAuthenticatedState();
            SetAccountFormStatus("登录已过期，请重新登录。", isError: true);
        }
        catch (LauncherApiException exception)
        {
            SetAccountFormStatus(
                exception.ApiDetail ?? "暂时无法退出所有设备。",
                isError: true);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException or IOException)
        {
            SetAccountFormStatus("账号安全服务暂时不可用。", isError: true);
        }
        finally
        {
            IsAccountBusy = false;
        }
    }

    private async void OpenAdminConsole()
    {
        if (!IsAdministrator || IsAdminConsoleBusy)
        {
            return;
        }

        IsAdminConsoleBusy = true;
        try
        {
            var ticket = await _authenticationService.CreateAdminBrowserTicketAsync();
            if (!Uri.TryCreate(ticket.BrowserUrl, UriKind.Absolute, out var browserUri) ||
                (browserUri.Scheme != Uri.UriSchemeHttps &&
                 (browserUri.Scheme != Uri.UriSchemeHttp || !browserUri.IsLoopback)) ||
                !string.IsNullOrEmpty(browserUri.UserInfo))
            {
                throw new InvalidDataException("The admin console URL is invalid.");
            }

            Process.Start(new ProcessStartInfo(browserUri.AbsoluteUri)
            {
                UseShellExecute = true
            });
            ShowToast("管理后台已在浏览器中打开");
        }
        catch (LauncherAuthenticationRequiredException)
        {
            SetCurrentAccount(null);
            ShowToast("登录已过期，请重新登录");
        }
        catch (LauncherApiException exception)
        {
            ShowToast(exception.ApiDetail ?? "暂时无法打开管理后台");
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException or
            IOException or System.ComponentModel.Win32Exception)
        {
            ShowToast("暂时无法打开管理后台");
        }
        finally
        {
            IsAdminConsoleBusy = false;
        }
    }

    private void SetCurrentAccount(HechaoAccount? account)
    {
        _currentAccount = account;
        if (account is not null)
        {
            _telemetryService.TryFlush();
        }
        _accountStatusHint = null;
        if (account?.IsMinecraftLinked != true)
        {
            IsMinecraftUnlinkFormVisible = false;
        }
        OnPropertyChanged(nameof(IsAuthenticated));
        OnPropertyChanged(nameof(IsMinecraftLinked));
        OnPropertyChanged(nameof(IsAdministrator));
        OnPropertyChanged(nameof(AccountDisplayName));
        OnPropertyChanged(nameof(AccountUsername));
        OnPropertyChanged(nameof(AccountStatusText));
        OnPropertyChanged(nameof(AccountAccessText));
        OnPropertyChanged(nameof(MinecraftIdentityText));
        OnPropertyChanged(nameof(MinecraftLinkStatusText));
        OnPropertyChanged(nameof(AccountActionGlyph));
        OnPropertyChanged(nameof(AccountActionTooltip));
        OnPropertyChanged(nameof(CanUploadDiagnosticBundle));
        PrimaryActionCommand.RaiseCanExecuteChanged();
        LogoutAccountCommand.RaiseCanExecuteChanged();
        LinkMinecraftCommand.RaiseCanExecuteChanged();
        UnlinkMinecraftCommand.RaiseCanExecuteChanged();
        LogoutAllDevicesCommand.RaiseCanExecuteChanged();
        OpenAdminConsoleCommand.RaiseCanExecuteChanged();
        UpdatePrimaryActionForState();
    }

    private static string FormatFileSize(long bytes) =>
        bytes >= 1024 * 1024
            ? $"{bytes / (1024d * 1024d):0.0} MiB"
            : $"{Math.Max(1, bytes / 1024d):0.0} KiB";

    private void ClearAuthenticatedState()
    {
        SetCurrentAccount(null);
        Servers.Clear();
        ActivityServers.Clear();
        OnPropertyChanged(nameof(ActivityServerCount));
        OnPropertyChanged(nameof(HasActivityServers));
        SelectedServer = null;
    }

    private void UpdatePrimaryActionForState()
    {
        if (IsProgressActive)
        {
            return;
        }

        PrimaryActionText = !IsAuthenticated
            ? "登录赫朝账号"
            : !_selectedProfileStateChecked
                ? "检查客户端"
                : _selectedProfileState == LocalProfileState.UpdateRequired
                    ? "更新客户端"
                    : _selectedProfileState != LocalProfileState.Ready
                        ? "安装客户端"
                        : !IsMinecraftLinked
                            ? "绑定正版身份"
                            : GetLaunchActionText();
        OnPropertyChanged(nameof(PrimaryActionGlyph));
    }

    private string GetLaunchActionText()
    {
        if (SelectedServer is null)
        {
            return "进入服务器";
        }

        if (_gameLauncherService.GetRunningGame() is not { } runningGame)
        {
            _runningServerId = null;
            return "进入服务器";
        }

        _runningServerId = runningGame.ServerId;
        return string.Equals(
                _runningServerId,
                SelectedServer.Id,
                StringComparison.Ordinal)
            ? "重新连接"
            : "切换服务器";
    }

    private static bool IsPlayerServer(ServerSummary server) =>
        !string.Equals(server.Id, "lobby", StringComparison.OrdinalIgnoreCase);

    private void SetAccountFormStatus(string message, bool isError)
    {
        AccountFormMessage = message;
        IsAccountFormError = isError;
    }

    private static string GetAccessTierText(AccessTier accessTier)
    {
        return accessTier switch
        {
            AccessTier.Member => "成员",
            AccessTier.Participant => "活动成员",
            AccessTier.Collaborator => "协作者",
            AccessTier.Administrator => "管理员",
            _ => "成员"
        };
    }

    private static string GetMinecraftSignInError(MinecraftSignInFailure failure)
    {
        return failure switch
        {
            MinecraftSignInFailure.XboxAccountRequired => "该 Microsoft 账号尚未创建 Xbox 档案",
            MinecraftSignInFailure.FamilyRestriction => "该账号受到 Microsoft 家庭设置限制",
            MinecraftSignInFailure.ApplicationNotApproved => "赫朝启动器尚未通过 Minecraft API 审核",
            MinecraftSignInFailure.ServiceUnavailable => "Microsoft 或 Minecraft 登录服务暂时不可用",
            _ => "无法完成 Minecraft 正版身份验证"
        };
    }

    private static string GetMinecraftSignInStatus(MinecraftSignInFailure failure)
    {
        return failure switch
        {
            MinecraftSignInFailure.XboxAccountRequired => "缺少 Xbox 档案",
            MinecraftSignInFailure.FamilyRestriction => "账号受家庭限制",
            MinecraftSignInFailure.ApplicationNotApproved => "Minecraft API 待审核",
            MinecraftSignInFailure.ServiceUnavailable => "登录服务暂不可用",
            _ => "正版验证未完成"
        };
    }

    private static int ParseMemoryInMiB(string value)
    {
        var firstPart = value.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return int.TryParse(firstPart, out var gibibytes)
            ? checked(gibibytes * 1024)
            : 6 * 1024;
    }

    private ClientProfileSummary? GetSelectedProfile()
    {
        return SelectedServer is not null &&
               _clientProfiles.TryGetValue(SelectedServer.ClientProfileId, out var profile)
            ? profile
            : null;
    }

    private static string FormatDownloadSize(long bytes)
    {
        const double bytesPerMebibyte = 1024d * 1024d;
        const double bytesPerGibibyte = 1024d * bytesPerMebibyte;
        return bytes >= bytesPerGibibyte
            ? $"{bytes / bytesPerGibibyte:0.##} GB"
            : $"{bytes / bytesPerMebibyte:0.#} MB";
    }

    private void ToggleNotifications()
    {
        IsSettingsOpen = false;
        IsNotificationsOpen = !IsNotificationsOpen;
    }

    private void ToggleSettings()
    {
        IsNotificationsOpen = false;
        IsSettingsOpen = !IsSettingsOpen;
    }

    private void CloseOverlays()
    {
        IsNotificationsOpen = false;
        IsSettingsOpen = false;
    }

    private async void ShowToast(string message)
    {
        ToastMessage = message;
        IsToastVisible = true;
        await Task.Delay(4000);
        if (ToastMessage == message)
        {
            IsToastVisible = false;
        }
    }

    private void SaveSettings()
    {
        _settings = new LauncherSettings(
            SelectedServer?.Id ?? _settings.SelectedServerId,
            SelectedMemory,
            ClientDirectory,
            CheckForUpdates,
            KeepDownloadsAfterClose,
            CloseLauncherAfterGameStart,
            OpenDownloadsWhenInstalling,
            SelectedStartupPage,
            ClientStorageLayout.CurrentStorageSchemaVersion,
            new Dictionary<string, string>(
                _profileJavaPaths,
                StringComparer.Ordinal));
        _settingsStore.Save(_settings);
    }

    public async Task<bool> UpdateSelectedProfileJavaPathAsync(string path)
    {
        var profileId = SelectedServer?.ClientProfileId;
        if (string.IsNullOrWhiteSpace(profileId) || string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            var validated = await JavaRuntimeValidator.ValidateAsync(
                path,
                GetSelectedProfileJavaMajorVersion());
            _profileJavaPaths[profileId] = validated.ExecutablePath;
            SaveSettings();
            NotifySelectedProfileJavaPropertiesChanged();
            ShowToast(
                $"当前客户端已改用自定义 Java {validated.MajorVersion}");
            return true;
        }
        catch (JavaRuntimeVersionMismatchException exception)
        {
            ShowToast(
                $"当前客户端需要 Java {exception.ExpectedMajorVersion}，所选文件是 Java {exception.ActualMajorVersion}");
            return false;
        }
        catch (JavaRuntimeValidationException)
        {
            ShowToast("无法使用所选 Java，请选择完整运行时中的 java.exe 或 javaw.exe");
            return false;
        }
    }

    private void UseManagedJava()
    {
        var profileId = SelectedServer?.ClientProfileId;
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return;
        }

        if (_profileJavaPaths.Remove(profileId))
        {
            SaveSettings();
        }

        NotifySelectedProfileJavaPropertiesChanged();
        ShowToast($"当前客户端已恢复自动 Java {GetSelectedProfileJavaMajorVersion()}");
    }

    private string? GetSelectedProfileCustomJavaPath()
    {
        var profileId = SelectedServer?.ClientProfileId;
        return !string.IsNullOrWhiteSpace(profileId) &&
               _profileJavaPaths.TryGetValue(profileId, out var javaPath)
            ? javaPath
            : null;
    }

    private int GetSelectedProfileJavaMajorVersion()
    {
        try
        {
            var metadataPath = Path.Combine(
                SelectedProfileGameDirectory,
                "hechao-profile.json");
            if (File.Exists(metadataPath))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(metadataPath));
                if (document.RootElement.TryGetProperty(
                        "javaMajorVersion",
                        out var javaMajorVersion) &&
                    javaMajorVersion.TryGetInt32(out var installedMajorVersion) &&
                    installedMajorVersion is >= 8 and <= 99)
                {
                    return installedMajorVersion;
                }
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
        }

        return GetRecommendedJavaMajorVersion(
            SelectedServer?.MinecraftVersion ?? string.Empty);
    }

    internal static int GetRecommendedJavaMajorVersion(string minecraftVersion)
    {
        var components = minecraftVersion
            .Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Select(value => int.TryParse(value, out var parsed) ? parsed : 0)
            .Take(3)
            .ToArray();
        if (components.Length < 2 || components[0] != 1)
        {
            return 21;
        }

        var minor = components[1];
        var patch = components.Length > 2 ? components[2] : 0;
        if (minor > 20 || minor == 20 && patch >= 5)
        {
            return 21;
        }

        if (minor >= 18)
        {
            return 17;
        }

        return minor == 17 ? 16 : 8;
    }

    private void NotifySelectedProfileJavaPropertiesChanged()
    {
        OnPropertyChanged(nameof(SelectedProfileJavaVersionText));
        OnPropertyChanged(nameof(IsUsingManagedJava));
        OnPropertyChanged(nameof(IsUsingCustomJava));
        OnPropertyChanged(nameof(SelectedProfileJavaModeText));
        OnPropertyChanged(nameof(SelectedProfileJavaPathText));
    }

    private bool IsSelectedProfileRunning()
    {
        var profileId = SelectedServer?.ClientProfileId;
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return false;
        }

        try
        {
            return _gameLauncherService.IsProfileRunning(profileId);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private void SetRollbackCandidate(InstalledProfileState? candidate)
    {
        if (Equals(_rollbackCandidate, candidate))
        {
            return;
        }

        _rollbackCandidate = candidate;
        OnPropertyChanged(nameof(CanRollbackSelectedProfile));
        OnPropertyChanged(nameof(RollbackCandidateVersion));
        OnPropertyChanged(nameof(RollbackProfileToolTip));
    }

    private static LauncherPage GetStartupPage(string startupPage)
    {
        return startupPage switch
        {
            "下载中心" => LauncherPage.Downloads,
            "活动" => LauncherPage.Activities,
            _ => LauncherPage.Servers
        };
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Hechao.Contracts;
using Hechao.Distribution;
using Hechao.Launcher.Controls;
using Hechao.Launcher.Infrastructure;
using Hechao.Launcher.Services;
using ToastLevel = Hechao.Launcher.ViewModels.ToastSeverity;

namespace Hechao.Launcher.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private static readonly TimeSpan MaxCatalogBoundaryDelay = TimeSpan.FromDays(30);
    private static readonly TimeSpan CatalogBoundaryGracePeriod = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan RegistrationCodeCooldown = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan DefaultCatalogFallbackRetryDelay = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan DefaultActivityCatalogRefreshInterval = TimeSpan.FromSeconds(30);

    private readonly IServerCatalogClient _catalogClient;
    private readonly ILauncherAuthenticationService _authenticationService;
    private readonly ILauncherSettingsStore _settingsStore;
    private readonly IClientInstallationService _installationService;
    private readonly IMinecraftGameLauncherService _gameLauncherService;
    private readonly IDownloadHistoryStore _downloadHistoryStore;
    private readonly IGameDiagnosticsService _gameDiagnosticsService;
    private readonly IGameDiagnosticUploadService _diagnosticUploadService;
    private readonly ILauncherTelemetryService _telemetryService;
    private readonly ILauncherUpdateService _launcherUpdateService;
    private readonly IMinecraftSkinService _minecraftSkinService;
    private readonly IPlayerGameSettingsService _playerGameSettingsService;
    private readonly ILauncherThemeService _themeService;
    private readonly bool _isUiPreview;
    private readonly TimeSpan _catalogFallbackRetryDelay;
    private readonly TimeSpan _activityCatalogRefreshInterval;
    private readonly SynchronizationContext? _uiContext;
    private readonly Dictionary<string, ClientProfileSummary> _clientProfiles = new(StringComparer.Ordinal);
    private readonly List<ServerSummary> _catalogPlayerServers = [];
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
    private ToastSeverity _toastSeverity = ToastSeverity.Info;
    private long _toastGeneration;
    private long _toastAnnouncementRevision;
    private ClientInstallPhase? _clientInstallPhase;
    private bool _installStepFailed;
    private string _selectedMemory;
    private string _clientDirectory;
    private bool _checkForUpdates;
    private bool _keepDownloadsAfterClose;
    private bool _closeLauncherAfterGameStart;
    private bool _openDownloadsWhenInstalling;
    private bool _useSystemProxy;
    private bool _useDarkMode;
    private string _selectedStartupPage;
    private bool _isCatalogLoading;
    private bool _catalogRefreshPending;
    private bool _hasCatalogLoadError;
    private bool _isCatalogStale;
    private string _catalogStatusMessage = "正在加载服务器目录...";
    private int _catalogAnnouncementRevision;
    private CancellationTokenSource? _catalogLoadCancellation;
    private long _catalogLoadGeneration;
    private long _clientStateRefreshGeneration;
    private long _clientContextGeneration;
    private CancellationTokenSource? _catalogScheduleCancellation;
    private CancellationTokenSource? _catalogRetryCancellation;
    private CancellationTokenSource? _activityCatalogRefreshCancellation;
    private CancellationTokenSource? _activityClientStateRefreshCancellation;
    private long _activityClientStateRefreshGeneration;
    private HechaoAccount? _currentAccount;
    private string? _accountStatusHint;
    private bool _isAccountBusy;
    private bool _isAdminConsoleBusy;
    private bool _isMinecraftUnlinkFormVisible;
    private string _accountFormMessage = string.Empty;
    private bool _isAccountFormError;
    private int _accountFormAnnouncementRevision;
    private bool _isRegistrationCodeCooldownActive;
    private bool _isRegistrationLegalAccepted;
    private CancellationTokenSource? _registrationCodeCooldownCancellation;
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
    private LauncherUpdatePlan? _launcherUpdatePlan;
    private bool _isLauncherUpdateChecking;
    private long _launcherUpdateCheckGeneration;
    private bool _launcherUpdateAutoInstallPending;
    private bool _isLauncherUpdateVisible;
    private bool _isLauncherUpdateBusy;
    private double _launcherUpdateProgress;
    private string _launcherUpdateStatus = string.Empty;
    private bool _hasCheckedLauncherUpdate;
    private ImageSource? _accountSkinSource;
    private long _accountSkinRevision;

    public MainWindowViewModel(
        IServerCatalogClient catalogClient,
        ILauncherAuthenticationService authenticationService,
        ILauncherSettingsStore settingsStore,
        IClientInstallationService installationService,
        IMinecraftGameLauncherService gameLauncherService,
        IDownloadHistoryStore downloadHistoryStore,
        IGameDiagnosticsService gameDiagnosticsService,
        IGameDiagnosticUploadService diagnosticUploadService,
        ILauncherTelemetryService? telemetryService = null,
        ILauncherUpdateService? launcherUpdateService = null,
        IMinecraftSkinService? minecraftSkinService = null,
        IPlayerGameSettingsService? playerGameSettingsService = null,
        TimeSpan? catalogFallbackRetryDelay = null,
        TimeSpan? activityCatalogRefreshInterval = null,
        ILauncherThemeService? themeService = null,
        LauncherSettings? initialSettings = null,
        bool isUiPreview = false)
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
        _launcherUpdateService =
            launcherUpdateService ?? NullLauncherUpdateService.Instance;
        _minecraftSkinService =
            minecraftSkinService ?? NullMinecraftSkinService.Instance;
        _playerGameSettingsService =
            playerGameSettingsService ?? NullPlayerGameSettingsService.Instance;
        _themeService = themeService ?? NullLauncherThemeService.Instance;
        _isUiPreview = isUiPreview;
        _catalogFallbackRetryDelay =
            catalogFallbackRetryDelay ?? DefaultCatalogFallbackRetryDelay;
        if (_catalogFallbackRetryDelay <= TimeSpan.Zero ||
            _catalogFallbackRetryDelay > TimeSpan.FromMinutes(5))
        {
            throw new ArgumentOutOfRangeException(
                nameof(catalogFallbackRetryDelay));
        }
        _activityCatalogRefreshInterval =
            activityCatalogRefreshInterval ?? DefaultActivityCatalogRefreshInterval;
        if (_activityCatalogRefreshInterval <= TimeSpan.Zero ||
            _activityCatalogRefreshInterval > TimeSpan.FromMinutes(5))
        {
            throw new ArgumentOutOfRangeException(
                nameof(activityCatalogRefreshInterval));
        }
        _uiContext = SynchronizationContext.Current;
        _settings = initialSettings ?? settingsStore.Load();
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
        _useSystemProxy = _settings.UseSystemProxy;
        _useDarkMode = _settings.UseDarkMode;
        _themeService.Apply(_useDarkMode);
        StartupPageOptions = ["服务器", "下载中心", "活动"];
        _selectedStartupPage = StartupPageOptions.Contains(_settings.StartupPage)
            ? _settings.StartupPage
            : "服务器";
        _activePage = GetStartupPage(_selectedStartupPage);
        ActivityCalendar = new ActivityCalendarViewModel();

        SelectServerCommand = new RelayCommand<ServerSummary>(
            SelectServer,
            _ => CanSelectServer);
        PrimaryActionCommand = new AsyncRelayCommand(
            StartPrimaryActionAsync,
            HandleUnexpectedPrimaryActionError,
            CanUseSelectedServer);
        RepairCommand = new AsyncRelayCommand(
            () => InstallSelectedProfileAsync(isRepair: true),
            HandleUnexpectedPrimaryActionError,
            () => !IsProgressActive);
        RefreshCommand = new RelayCommand(
            () => _ = LoadCatalogAsync(userInitiated: true),
            () => !_isCatalogLoading && !IsProgressActive);
        OpenClientDirectoryCommand = new RelayCommand(
            OpenClientDirectory,
            () => !_isUiPreview);
        OpenSelectedProfileGameDirectoryCommand = new RelayCommand(
            OpenSelectedProfileGameDirectory,
            () => !_isUiPreview &&
                  !string.IsNullOrWhiteSpace(SelectedServer?.ClientProfileId));
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
        PrepareActivityClientCommand = new AsyncRelayCommand<ActivityServerItemViewModel>(
            PrepareActivityClientAsync,
            HandleUnexpectedPrimaryActionError,
            CanPrepareActivityClient);
        ResetLauncherSettingsCommand = new RelayCommand(
            ResetLauncherSettings,
            () => CanChangeClientDirectory);
        CreateDiagnosticBundleCommand = new RelayCommand(
            StartCreateDiagnosticBundle,
            CanCreateDiagnosticBundle);
        OpenDiagnosticsDirectoryCommand = new RelayCommand(
            OpenDiagnosticsDirectory,
            () => !_isUiPreview);
        UseManagedJavaCommand = new RelayCommand(
            UseManagedJava,
            () => CanUseProfileJavaActions);
        CheckLauncherUpdateCommand = new AsyncRelayCommand(
            () => TryCheckLauncherUpdateAsync(userInitiated: true),
            HandleUnexpectedLauncherUpdateError,
            () => IsAuthenticated &&
                  !IsLauncherUpdateBusy &&
                  !IsLauncherUpdateChecking);
        InstallLauncherUpdateCommand = new AsyncRelayCommand(
            InstallLauncherUpdateAsync,
            HandleUnexpectedLauncherUpdateError,
            CanInstallLauncherUpdate);
        DismissLauncherUpdateCommand = new RelayCommand(
            DismissLauncherUpdate,
            () => IsLauncherUpdateVisible &&
                  !IsLauncherUpdateBusy &&
                  !IsLauncherUpdateRequired);

        LoadDownloadHistory();

        _ = _telemetryService.RecordAsync(
            LauncherTelemetryEventType.LauncherStarted,
            LauncherTelemetryOutcome.Success);
        _ = InitializeAsync();
    }

    public ObservableCollection<ServerSummary> Servers { get; } = [];
    public ObservableCollection<ServerSummary> HomeAnnouncementServers { get; } = [];
    public ObservableCollection<ActivityServerItemViewModel> ActivityServers { get; } = [];
    public ObservableCollection<DownloadJobViewModel> DownloadHistory { get; } = [];
    public ActivityCalendarViewModel ActivityCalendar { get; }
    public IReadOnlyList<string> MemoryOptions { get; }
    public IReadOnlyList<string> StartupPageOptions { get; }
    public string LauncherVersionText { get; } = $"v{LauncherProductInfo.Version}";
    public bool HasHomeAnnouncements => HomeAnnouncementServers.Count > 0;
    public bool HasNoHomeAnnouncements => !HasHomeAnnouncements;
    public RelayCommand<ServerSummary> SelectServerCommand { get; }
    public AsyncRelayCommand PrimaryActionCommand { get; }
    public AsyncRelayCommand RepairCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand OpenClientDirectoryCommand { get; }
    public RelayCommand OpenSelectedProfileGameDirectoryCommand { get; }
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
    public AsyncRelayCommand<ActivityServerItemViewModel> PrepareActivityClientCommand { get; }
    public RelayCommand ResetLauncherSettingsCommand { get; }
    public RelayCommand CreateDiagnosticBundleCommand { get; }
    public RelayCommand OpenDiagnosticsDirectoryCommand { get; }
    public RelayCommand UseManagedJavaCommand { get; }
    public AsyncRelayCommand CheckLauncherUpdateCommand { get; }
    public AsyncRelayCommand InstallLauncherUpdateCommand { get; }
    public RelayCommand DismissLauncherUpdateCommand { get; }
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

            if (IsCatalogPageVisible)
            {
                StartActivityCatalogRefresh(refreshImmediately: true);
            }
            else
            {
                CancelActivityCatalogRefresh();
            }
        }
    }

    public bool IsServersPage
    {
        get => ActivePage == LauncherPage.Servers;
        set => SetNavigationPage(value, LauncherPage.Servers, nameof(IsServersPage));
    }

    public bool IsDownloadsPage
    {
        get => ActivePage == LauncherPage.Downloads;
        set => SetNavigationPage(value, LauncherPage.Downloads, nameof(IsDownloadsPage));
    }

    public bool IsActivitiesPage
    {
        get => ActivePage == LauncherPage.Activities;
        set => SetNavigationPage(value, LauncherPage.Activities, nameof(IsActivitiesPage));
    }

    public bool IsAccountPage
    {
        get => ActivePage == LauncherPage.Account;
        set => SetNavigationPage(value, LauncherPage.Account, nameof(IsAccountPage));
    }

    public bool IsSettingsPage
    {
        get => ActivePage == LauncherPage.Settings;
        set => SetNavigationPage(value, LauncherPage.Settings, nameof(IsSettingsPage));
    }
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
    public bool IsCatalogLoading => _isCatalogLoading;
    public bool HasCatalogLoadError => _hasCatalogLoadError;
    public bool IsCatalogStale => _isCatalogStale;
    public bool HasServerCatalogData => Servers.Count > 0;
    public bool IsCatalogStatusVisible =>
        IsCatalogLoading ||
        HasCatalogLoadError ||
        IsCatalogStale ||
        (_hasLoadedCatalog && !HasServerCatalogData);
    public bool IsActivityCatalogStateVisible =>
        !HasActivityServers &&
        (IsCatalogLoading || HasCatalogLoadError || _hasLoadedCatalog);
    public string CatalogStatusMessage
    {
        get => _catalogStatusMessage;
        private set => SetProperty(ref _catalogStatusMessage, value);
    }

    public string ActivityCatalogStateTitle => IsCatalogLoading
        ? "正在读取活动目录"
        : HasCatalogLoadError || IsCatalogStale
            ? "活动目录暂时不可用"
            : "当前没有活动服务器";

    public string ActivityCatalogStateMessage => IsCatalogLoading
        ? "正在同步活动开放状态和客户端档案，请稍候。"
        : HasCatalogLoadError || IsCatalogStale
            ? CatalogStatusMessage
            : "目录同步正常，但目前没有可展示的活动档案。";

    public bool IsActivityCalendarStatusVisible =>
        IsCatalogStale || IsActivityCatalogStateVisible;

    public string ActivityCalendarStatusTitle =>
        IsCatalogStale && HasActivityServers
            ? "活动状态可能不是最新"
            : ActivityCatalogStateTitle;

    public string ActivityCalendarStatusMessage => IsCatalogStale
        ? CatalogStatusMessage
        : ActivityCatalogStateMessage;

    public int CatalogAnnouncementRevision => _catalogAnnouncementRevision;
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
    public ImageSource? AccountSkinSource => _accountSkinSource;
    public bool HasAccountSkin => _accountSkinSource is not null;
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
    public string TopBarAccountSubtitle => _currentAccount is null
        ? "登录赫朝账户"
        : IsMinecraftLinked
            ? GetAccessTierText(_currentAccount.AccessTier)
            : "待绑定正版身份";
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
    public int AccountFormAnnouncementRevision => _accountFormAnnouncementRevision;
    public bool CanSubmitAccountForms => !IsAccountBusy;
    public bool IsRegistrationLegalAccepted
    {
        get => _isRegistrationLegalAccepted;
        set
        {
            if (SetProperty(ref _isRegistrationLegalAccepted, value))
            {
                OnPropertyChanged(nameof(CanSubmitRegistrationForm));
            }
        }
    }
    public bool CanSubmitRegistrationForm =>
        CanSubmitAccountForms && IsRegistrationLegalAccepted;
    public bool CanSendRegistrationCode =>
        CanSubmitAccountForms && !_isRegistrationCodeCooldownActive;
    public string RegistrationCodeActionText => _isRegistrationCodeCooldownActive
        ? "验证码已发送"
        : IsAccountBusy
            ? "请稍候"
            : "发送验证码";
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
            OnPropertyChanged(nameof(CanSubmitAccountForms));
            OnPropertyChanged(nameof(CanSubmitRegistrationForm));
            OnPropertyChanged(nameof(CanSendRegistrationCode));
            OnPropertyChanged(nameof(RegistrationCodeActionText));
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

            InvalidateClientContext();
            SetRollbackCandidate(null);
            _clientInstallPhase = null;
            _installStepFailed = false;
            NotifyProgressStepStatesChanged();
            OnPropertyChanged(nameof(SelectedServerStatusText));
            OnPropertyChanged(nameof(SelectedServerLoaderText));
            OnPropertyChanged(nameof(SelectedServerPlayerText));
            OnPropertyChanged(nameof(SelectedServerCategoryText));
            OnPropertyChanged(nameof(SelectedServerDescriptionText));
            OnPropertyChanged(nameof(SelectedServerVersionText));
            OnPropertyChanged(nameof(SelectedServerAccessText));
            OnPropertyChanged(nameof(HasSelectedServerSchedule));
            OnPropertyChanged(nameof(SelectedServerScheduleText));
            OnPropertyChanged(nameof(SelectedProfileDisplayName));
            OnPropertyChanged(nameof(SelectedProfileMetaText));
            OnPropertyChanged(nameof(SelectedProfileGameDirectory));
            OnPropertyChanged(nameof(SelectedProfileGameDirectoryDisplayText));
            NotifySelectedProfileJavaPropertiesChanged();
            OnPropertyChanged(nameof(IsSelectedServerOnline));
            OnPropertyChanged(nameof(CurrentPageTitle));
            PrimaryActionCommand.RaiseCanExecuteChanged();
            OpenSelectedProfileGameDirectoryCommand.RaiseCanExecuteChanged();
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
        : SelectedServer.CanJoin
            ? $"{GetAccessTierText(SelectedServer.MinimumTier)}可进入"
            : $"未获进入权限 · 最低{GetAccessTierText(SelectedServer.MinimumTier)}";
    public string PrimaryActionToolTip => SelectedServer is { CanJoin: false } server
        ? GetJoinAccessDeniedMessage(server)
        : PrimaryActionText;
    public bool HasSelectedServerSchedule =>
        SelectedServer is not null && IsActivityServer(SelectedServer);
    public string SelectedServerScheduleText => SelectedServer is null
        ? string.Empty
        : ServerCatalogPresentation.FormatSchedule(
            SelectedServer.OpensAt,
            SelectedServer.ClosesAt);
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

    public bool CanDeleteSelectedProfile =>
        _selectedProfileState != LocalProfileState.Missing &&
        !IsProgressActive &&
        !IsSelectedProfileRunning();

    public string DeleteProfileToolTip => _selectedProfileState == LocalProfileState.Missing
        ? "当前客户端尚未安装"
        : IsSelectedProfileRunning()
            ? "请先退出当前客户端"
            : IsProgressActive
                ? "请等待当前任务完成"
                : "删除客户端文件并保留玩家设置";

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
                SelectServerCommand.RaiseCanExecuteChanged();
                PrepareActivityClientCommand.RaiseCanExecuteChanged();
                RefreshCommand.RaiseCanExecuteChanged();
                InstallLauncherUpdateCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(CanSelectServer));
                OnPropertyChanged(nameof(CanChangeClientDirectory));
                ResetLauncherSettingsCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(CanRollbackSelectedProfile));
                OnPropertyChanged(nameof(RollbackProfileToolTip));
                OnPropertyChanged(nameof(CanDeleteSelectedProfile));
                OnPropertyChanged(nameof(DeleteProfileToolTip));

                if (!value && _catalogRefreshPending)
                {
                    _catalogRefreshPending = false;
                    _ = LoadCatalogAsync();
                }

                if (!value && _launcherUpdatePlan is not null)
                {
                    TryPresentLauncherUpdatePlan();
                }
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
            ShowToast($"已将游戏内存设为 {value}", ToastLevel.Success);
        }
    }

    public string ClientDirectory => _clientDirectory;

    public bool CanSelectServer => !IsProgressActive;

    public bool CanChangeClientDirectory =>
        !_isUiPreview &&
        !IsProgressActive &&
        _gameLauncherService.GetRunningGame() is null;

    public bool CanUseProfileJavaActions => !_isUiPreview;

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

    public string SelectedProfileGameDirectoryDisplayText =>
        string.IsNullOrWhiteSpace(SelectedServer?.ClientProfileId)
            ? "选择服务器后显示"
            : SelectedProfileGameDirectory;

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

    public bool UseSystemProxy
    {
        get => _useSystemProxy;
        set
        {
            if (SetProperty(ref _useSystemProxy, value))
            {
                SaveSettings();
                ShowToast("代理设置将在下次启动时生效");
            }
        }
    }

    public bool UseDarkMode
    {
        get => _useDarkMode;
        set
        {
            if (_useDarkMode == value)
            {
                return;
            }

            _themeService.Apply(value);
            _useDarkMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ThemeToggleToolTip));
            SaveSettings();
        }
    }

    public string ThemeToggleToolTip => UseDarkMode
        ? "切换到日间模式"
        : "切换到黑夜模式";

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
        set
        {
            if (!SetProperty(ref _isNotificationsOpen, value))
            {
                return;
            }

            if (value)
            {
                IsSettingsOpen = false;
            }
        }
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

    public ToastSeverity ToastSeverity
    {
        get => _toastSeverity;
        private set
        {
            if (SetProperty(ref _toastSeverity, value))
            {
                OnPropertyChanged(nameof(ToastIconKind));
                OnPropertyChanged(nameof(ToastAutomationStatus));
            }
        }
    }

    public IconParkKind ToastIconKind => _toastSeverity switch
    {
        ToastLevel.Success => IconParkKind.CheckOne,
        ToastLevel.Error => IconParkKind.Close,
        _ => IconParkKind.VolumeNotice
    };

    public string ToastAutomationStatus => _toastSeverity switch
    {
        ToastLevel.Success => "成功",
        ToastLevel.Error => "错误",
        _ => "提示"
    };

    public long ToastAnnouncementRevision =>
        Volatile.Read(ref _toastAnnouncementRevision);

    public ProgressStepState ProgressStepOneState => GetProgressStepState(0);

    public ProgressStepState ProgressStepTwoState => GetProgressStepState(1);

    public ProgressStepState ProgressStepThreeState => GetProgressStepState(2);

    public ProgressStepState ProgressStepFourState => GetProgressStepState(3);

    public string ProgressStepOneStatusText => GetProgressStepStatusText(0);

    public string ProgressStepTwoStatusText => GetProgressStepStatusText(1);

    public string ProgressStepThreeStatusText => GetProgressStepStatusText(2);

    public string ProgressStepFourStatusText => GetProgressStepStatusText(3);

    public bool IsLauncherUpdateVisible
    {
        get => _isLauncherUpdateVisible;
        private set
        {
            if (SetProperty(ref _isLauncherUpdateVisible, value))
            {
                InstallLauncherUpdateCommand.RaiseCanExecuteChanged();
                DismissLauncherUpdateCommand.RaiseCanExecuteChanged();
                CheckLauncherUpdateCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsLauncherUpdateBusy
    {
        get => _isLauncherUpdateBusy;
        private set
        {
            if (SetProperty(ref _isLauncherUpdateBusy, value))
            {
                InstallLauncherUpdateCommand.RaiseCanExecuteChanged();
                DismissLauncherUpdateCommand.RaiseCanExecuteChanged();
                CheckLauncherUpdateCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsLauncherUpdateChecking
    {
        get => _isLauncherUpdateChecking;
        private set
        {
            if (SetProperty(ref _isLauncherUpdateChecking, value))
            {
                CheckLauncherUpdateCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsLauncherUpdateRequired =>
        _launcherUpdatePlan?.IsRequired == true;

    public string LauncherUpdateTitle =>
        _launcherUpdatePlan is null
            ? "启动器更新"
            : $"赫朝启动器 {_launcherUpdatePlan.LatestVersion.ToString(3)}";

    public string LauncherUpdateSummary =>
        IsLauncherUpdateRequired
            ? "当前版本已停止支持，需要更新后继续使用。"
            : "新版本已经准备好，更新后会自动回到启动器。";

    public string LauncherUpdateReleaseNotes =>
        string.IsNullOrWhiteSpace(_launcherUpdatePlan?.ReleaseNotes)
            ? "本次更新包含稳定性与功能改进。"
            : _launcherUpdatePlan.ReleaseNotes;

    public string LauncherUpdateSizeText =>
        _launcherUpdatePlan is null
            ? string.Empty
            : FormatFileSize(_launcherUpdatePlan.InstallerBytes);

    public double LauncherUpdateProgress
    {
        get => _launcherUpdateProgress;
        private set => SetProperty(ref _launcherUpdateProgress, value);
    }

    public string LauncherUpdateStatus
    {
        get => _launcherUpdateStatus;
        private set => SetProperty(ref _launcherUpdateStatus, value);
    }

    private async Task InitializeAsync()
    {
        IsAccountBusy = true;
        try
        {
            SetCurrentAccount(await _authenticationService.TryRestoreAsync());
        }
        catch (Exception exception) when (
            exception is HttpRequestException or
            TaskCanceledException or
            LauncherApiException or
            IOException)
        {
            // A failed restore is not the same as a revoked session. Keep
            // the account already loaded by the API client and let the
            // catalog retry refresh the session when connectivity returns.
            var account = _authenticationService.CurrentAccount;
            SetCurrentAccount(account);
            _accountStatusHint = account is null
                ? "暂时无法验证登录状态，请稍后重试"
                : "网络暂时不可用，已保留登录状态";
            OnPropertyChanged(nameof(AccountStatusText));
        }
        finally
        {
            IsAccountBusy = false;
        }

        await TryImportPlayerGameSettingsAsync();
        await LoadCatalogAsync();
        if (IsCatalogPageVisible)
        {
            StartActivityCatalogRefresh(refreshImmediately: false);
        }
        await TryCheckLauncherUpdateAsync();
    }

    private async Task TryImportPlayerGameSettingsAsync()
    {
        try
        {
            await _playerGameSettingsService.ImportLatestAsync(ClientDirectory);
        }
        catch (PlayerGameSettingsException exception)
        {
            Trace.TraceWarning(
                "Unable to import shared player settings: {0}",
                exception.Message);
        }
    }

    private async Task TryCheckLauncherUpdateAsync(bool userInitiated = false)
    {
        if ((!userInitiated && _hasCheckedLauncherUpdate) ||
            !IsAuthenticated ||
            IsLauncherUpdateVisible ||
            IsLauncherUpdateBusy)
        {
            return;
        }

        var generation = Interlocked.Increment(ref _launcherUpdateCheckGeneration);
        IsLauncherUpdateChecking = true;
        try
        {
            var plan = await _launcherUpdateService.CheckAsync();
            if (Volatile.Read(ref _launcherUpdateCheckGeneration) != generation)
            {
                return;
            }

            _hasCheckedLauncherUpdate = true;
            if (plan is null)
            {
                if (userInitiated)
                {
                    ShowToast("当前已是最新版本", ToastLevel.Success);
                }

                return;
            }

            var wasLauncherUpdateRequired = IsLauncherUpdateRequired;
            _launcherUpdatePlan = plan;
            LauncherUpdateProgress = 0;
            OnPropertyChanged(nameof(IsLauncherUpdateRequired));
            OnPropertyChanged(nameof(LauncherUpdateTitle));
            OnPropertyChanged(nameof(LauncherUpdateSummary));
            OnPropertyChanged(nameof(LauncherUpdateReleaseNotes));
            OnPropertyChanged(nameof(LauncherUpdateSizeText));
            if (wasLauncherUpdateRequired != IsLauncherUpdateRequired)
            {
                PrimaryActionCommand.RaiseCanExecuteChanged();
                PrepareActivityClientCommand.RaiseCanExecuteChanged();
            }

            var previousInstallFailed =
                _launcherUpdateService.HasPreviousInstallFailure(plan);
            _launcherUpdateAutoInstallPending =
                !userInitiated && !previousInstallFailed;
            LauncherUpdateStatus = previousInstallFailed
                ? "上次更新失败或状态未知，本次不会循环重启；请确认后手动重试。"
                : plan.IsRequired
                    ? "必须更新后才能继续使用当前服务。"
                    : "发现新版本，准备自动下载更新。";

            TryPresentLauncherUpdatePlan();
        }
        catch (Exception exception) when (
            exception is HttpRequestException or
            TaskCanceledException or
            LauncherApiException or
            InvalidDataException or
            LauncherAuthenticationRequiredException)
        {
            if (Volatile.Read(ref _launcherUpdateCheckGeneration) == generation)
            {
                ShowToast("检查更新失败，请稍后重试", ToastLevel.Error);
                Trace.TraceWarning(
                    "Unable to check launcher update: {0}",
                    exception.Message);
            }
        }
        finally
        {
            if (Volatile.Read(ref _launcherUpdateCheckGeneration) == generation)
            {
                IsLauncherUpdateChecking = false;
            }
        }
    }

    private bool CanInstallLauncherUpdate()
    {
        if (_launcherUpdatePlan is null ||
            !IsLauncherUpdateVisible ||
            IsLauncherUpdateBusy ||
            IsProgressActive)
        {
            return false;
        }

        return _gameLauncherService.GetRunningGame() is null;
    }

    private void TryPresentLauncherUpdatePlan()
    {
        if (_launcherUpdatePlan is null)
        {
            return;
        }

        if (IsProgressActive || _gameLauncherService.GetRunningGame() is not null)
        {
            IsLauncherUpdateVisible = false;
            LauncherUpdateStatus =
                "当前任务或游戏结束后会继续准备启动器更新。";
            return;
        }

        IsLauncherUpdateVisible = true;
        InstallLauncherUpdateCommand.RaiseCanExecuteChanged();
        if (_launcherUpdateAutoInstallPending && CanInstallLauncherUpdate())
        {
            _launcherUpdateAutoInstallPending = false;
            LauncherUpdateStatus = "发现新版本，正在自动下载更新...";
            _ = InstallLauncherUpdateAsync();
        }
    }

    private async Task InstallLauncherUpdateAsync()
    {
        if (!CanInstallLauncherUpdate())
        {
            LauncherUpdateStatus =
                "请等待当前任务和游戏结束后再安装启动器更新。";
            return;
        }

        var plan = _launcherUpdatePlan!;
        IsLauncherUpdateBusy = true;
        LauncherUpdateStatus = "正在下载启动器更新…";
        var progress = new InlineProgress<LauncherUpdateDownloadProgress>(
            value =>
            {
                LauncherUpdateProgress = value.Percent;
                LauncherUpdateStatus =
                    $"正在下载 {FormatFileSize(value.BytesDownloaded)} / " +
                    $"{FormatFileSize(value.TotalBytes)}";
            });
        var updaterStarted =
            await _launcherUpdateService.DownloadAndLaunchUpdaterAsync(
                plan,
                progress);
        if (!updaterStarted)
        {
            throw new InvalidOperationException(
                "The launcher updater could not be started.");
        }

        LauncherUpdateProgress = 100;
        LauncherUpdateStatus = "更新已校验，正在重新启动…";
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void DismissLauncherUpdate()
    {
        if (IsLauncherUpdateBusy || IsLauncherUpdateRequired)
        {
            return;
        }

        IsLauncherUpdateVisible = false;
        _launcherUpdatePlan = null;
        _launcherUpdateAutoInstallPending = false;
    }

    private async Task<bool> LoadCatalogAsync(
        bool userInitiated = false,
        CancellationTokenSource? fallbackRetryOwner = null)
    {
        if (fallbackRetryOwner is null)
        {
            CancelCatalogFallbackRetry();
        }
        else
        {
            fallbackRetryOwner.Token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(
                    Volatile.Read(ref _catalogRetryCancellation),
                    fallbackRetryOwner))
            {
                return false;
            }
        }

        var generation = Interlocked.Increment(ref _catalogLoadGeneration);
        var accountId = _currentAccount?.UserId;
        var hadLoadedCatalog = _hasLoadedCatalog;
        using var cancellation = new CancellationTokenSource();
        var previousCancellation = Interlocked.Exchange(
            ref _catalogLoadCancellation,
            cancellation);
        previousCancellation?.Cancel();

        SetCatalogLoading(true);
        SetCatalogStatus(
            "正在刷新服务器目录...",
            hasError: false,
            isStale: false);
        try
        {
            var result = await _catalogClient.GetCatalogResultAsync(cancellation.Token);
            var snapshot = result.Snapshot;
            if (!IsCatalogLoadCurrent(generation, cancellation, accountId))
            {
                return false;
            }

            if (IsProgressActive)
            {
                _catalogRefreshPending = true;
                return false;
            }

            var nextClientProfiles = snapshot.ClientProfiles
                .GroupBy(profile => profile.Id, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.Last(),
                    StringComparer.Ordinal);
            var playerServers = snapshot.Servers
                .Where(IsPlayerServer)
                .ToArray();
            var activityItems = playerServers
                .Where(IsActivityServer)
                .Select(server => new ActivityServerItemViewModel(server))
                .ToArray();
            IReadOnlyDictionary<string, ActivityClientProfileStateCheck> activityStateChecks;
            while (true)
            {
                var activityDataRoot = ClientDirectory;
                activityStateChecks = await CheckActivityClientProfileStatesAsync(
                    activityItems,
                    nextClientProfiles,
                    activityDataRoot,
                    cancellation.Token);
                if (!IsCatalogLoadCurrent(generation, cancellation, accountId))
                {
                    return false;
                }

                if (IsProgressActive)
                {
                    _catalogRefreshPending = true;
                    return false;
                }

                if (AreDirectoriesSame(activityDataRoot, ClientDirectory))
                {
                    break;
                }
            }

            var selectedProfileId = SelectedServer?.ClientProfileId;
            var previousSelectedProfile = selectedProfileId is not null &&
                                          _clientProfiles.TryGetValue(
                                              selectedProfileId,
                                              out var previousProfile)
                ? previousProfile
                : null;
            var selectedProfileChanged = selectedProfileId is not null &&
                                         (!nextClientProfiles.TryGetValue(
                                              selectedProfileId,
                                              out var currentProfile) ||
                                          !AreClientProfilesEquivalent(
                                              previousSelectedProfile,
                                              currentProfile));

            _clientProfiles.Clear();
            foreach (var (profileId, profile) in nextClientProfiles)
            {
                _clientProfiles[profileId] = profile;
            }

            if (selectedProfileChanged)
            {
                InvalidateClientContext();
                _selectedProfileState = LocalProfileState.Missing;
                _selectedProfileStateChecked = false;
                SetRollbackCandidate(null);
            }
            OnPropertyChanged(nameof(LatestGameExitText));

            CancelActivityClientStateRefresh();
            _catalogPlayerServers.Clear();
            _catalogPlayerServers.AddRange(playerServers);
            ActivityServers.Clear();
            foreach (var item in activityItems)
            {
                ApplyActivityClientProfileStateCheck(
                    item,
                    activityStateChecks[item.Server.ClientProfileId]);
                ActivityServers.Add(item);
            }
            ActivityCalendar.ReplaceActivities(ActivityServers);
            ReplaceHomeServerCollection();
            OnPropertyChanged(nameof(ActivityServerCount));
            OnPropertyChanged(nameof(HasActivityServers));

            var selectedServer =
                Servers.FirstOrDefault(server => server.Id == _settings.SelectedServerId) ??
                Servers.FirstOrDefault();
            var selectedServerChanged = !Equals(SelectedServer, selectedServer);
            _suppressNextAutomaticProfileCheck =
                !_hasLoadedCatalog &&
                !CheckForUpdates &&
                selectedServer is not null;
            SelectedServer = selectedServer;
            if (selectedProfileChanged && !selectedServerChanged && selectedServer is not null)
            {
                OnPropertyChanged(nameof(SelectedProfileDisplayName));
                OnPropertyChanged(nameof(SelectedProfileMetaText));
                OnPropertyChanged(nameof(SelectedProfileGameDirectory));
                OnPropertyChanged(nameof(SelectedProfileGameDirectoryDisplayText));
                NotifySelectedProfileJavaPropertiesChanged();
                UpdateProgress = 0;
                ClientStatusText = "正在检查客户端";
                UpdatePrimaryActionForState();
                _ = RefreshClientStateAsync();
            }
            _hasLoadedCatalog = true;
            NotifyCatalogStateChanged();
            ScheduleCatalogBoundaryRefresh(snapshot.Servers);

            switch (result.Source)
            {
                case CatalogSource.Cache:
                    SetCatalogStatus(
                        "目录服务暂时不可用，当前显示上次成功数据。",
                        hasError: false,
                        isStale: true);
                    ScheduleCatalogFallbackRetry();
                    if (userInitiated || !hadLoadedCatalog)
                    {
                        ShowToast("目录服务暂时不可用，已显示上次成功数据");
                    }
                    break;
                case CatalogSource.BuiltIn:
                    SetCatalogStatus(
                        "目录服务暂时不可用，当前显示内置应急目录。",
                        hasError: false,
                        isStale: true);
                    ScheduleCatalogFallbackRetry();
                    if (userInitiated || !hadLoadedCatalog)
                    {
                        ShowToast("目录服务暂时不可用，已显示内置应急目录");
                    }
                    break;
                case CatalogSource.Live when userInitiated:
                    SetCatalogStatus(
                        "服务器目录已刷新。",
                        hasError: false,
                        isStale: false);
                    CancelCatalogFallbackRetry();
                    ShowToast("服务器状态已刷新", ToastLevel.Success);
                    break;
                case CatalogSource.Live:
                    SetCatalogStatus(
                        "服务器目录已同步。",
                        hasError: false,
                        isStale: false);
                    CancelCatalogFallbackRetry();
                    break;
            }

            return result.Source == CatalogSource.Live;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception) when (!IsCatalogLoadCurrent(generation, cancellation, accountId))
        {
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or JsonException)
        {
            SetCatalogStatus(
                "暂时无法加载服务器目录，请检查网络后重试。",
                hasError: true,
                isStale: HasServerCatalogData || HasActivityServers);
            ScheduleCatalogFallbackRetry();
            if (userInitiated || !hadLoadedCatalog)
            {
                ShowToast("暂时无法加载服务器目录", ToastLevel.Error);
            }
        }
        catch (LauncherAuthenticationRequiredException)
        {
            // This branch is reserved for an authoritative 401/invalid
            // session. Transient transport failures are handled above and do
            // not clear the account.
            SetCurrentAccount(_authenticationService.CurrentAccount);
            CancelActivityClientStateRefresh();
            _catalogPlayerServers.Clear();
            Servers.Clear();
            ActivityServers.Clear();
            ActivityCalendar.ReplaceActivities(ActivityServers);
            HomeAnnouncementServers.Clear();
            OnPropertyChanged(nameof(HasHomeAnnouncements));
            OnPropertyChanged(nameof(HasNoHomeAnnouncements));
            OnPropertyChanged(nameof(ActivityServerCount));
            OnPropertyChanged(nameof(HasActivityServers));
            SelectedServer = null;
            SetCatalogStatus(
                "登录状态已失效，请重新登录赫朝账号。",
                hasError: true,
                isStale: false);
            if (userInitiated || !hadLoadedCatalog)
            {
                ShowToast("请先登录赫朝账号");
            }
        }
        catch (LauncherApiException exception)
        {
            SetCatalogStatus(
                exception.ApiDetail ?? "目录服务暂时不可用，请稍后重试。",
                hasError: true,
                isStale: HasServerCatalogData || HasActivityServers);
            ScheduleCatalogFallbackRetry();
            if (userInitiated || !hadLoadedCatalog)
            {
                ShowToast(exception.ApiDetail ?? "目录服务暂时不可用", ToastLevel.Error);
            }
        }
        finally
        {
            if (ReferenceEquals(
                    Interlocked.CompareExchange(
                        ref _catalogLoadCancellation,
                        null,
                        cancellation),
                    cancellation))
            {
                SetCatalogLoading(false);
            }

            cancellation.Dispose();
        }

        return false;
    }

    private bool IsCatalogLoadCurrent(
        long generation,
        CancellationTokenSource cancellation,
        Guid? accountId) =>
        generation == Volatile.Read(ref _catalogLoadGeneration) &&
        ReferenceEquals(_catalogLoadCancellation, cancellation) &&
        _currentAccount?.UserId == accountId;

    private void SetCatalogLoading(bool value)
    {
        if (_isCatalogLoading == value)
        {
            return;
        }

        _isCatalogLoading = value;
        OnPropertyChanged(nameof(IsCatalogLoading));
        NotifyCatalogStateChanged();
        RefreshCommand.RaiseCanExecuteChanged();
    }

    private void SetCatalogStatus(
        string message,
        bool hasError,
        bool isStale)
    {
        CatalogStatusMessage = message;
        _hasCatalogLoadError = hasError;
        _isCatalogStale = isStale;
        NotifyCatalogStateChanged();
        _catalogAnnouncementRevision++;
        OnPropertyChanged(nameof(CatalogAnnouncementRevision));
    }

    private void NotifyCatalogStateChanged()
    {
        OnPropertyChanged(nameof(HasCatalogLoadError));
        OnPropertyChanged(nameof(IsCatalogStale));
        OnPropertyChanged(nameof(HasServerCatalogData));
        OnPropertyChanged(nameof(IsCatalogStatusVisible));
        OnPropertyChanged(nameof(IsActivityCatalogStateVisible));
        OnPropertyChanged(nameof(ActivityCatalogStateTitle));
        OnPropertyChanged(nameof(ActivityCatalogStateMessage));
        OnPropertyChanged(nameof(IsActivityCalendarStatusVisible));
        OnPropertyChanged(nameof(ActivityCalendarStatusTitle));
        OnPropertyChanged(nameof(ActivityCalendarStatusMessage));
    }

    private void InvalidateCatalogLoad()
    {
        Interlocked.Increment(ref _catalogLoadGeneration);
        Interlocked.Exchange(ref _catalogLoadCancellation, null)?.Cancel();
        Interlocked.Exchange(ref _catalogScheduleCancellation, null)?.Cancel();
        Interlocked.Exchange(ref _catalogRetryCancellation, null)?.Cancel();
        CancelActivityClientStateRefresh();
        SetCatalogLoading(false);
    }

    private void ScheduleCatalogFallbackRetry()
    {
        var cancellation = new CancellationTokenSource();
        Interlocked.Exchange(
            ref _catalogRetryCancellation,
            cancellation)?.Cancel();
        _ = RetryCatalogAfterFallbackAsync(cancellation);
    }

    private void CancelCatalogFallbackRetry()
    {
        Interlocked.Exchange(ref _catalogRetryCancellation, null)?.Cancel();
    }

    private void StartActivityCatalogRefresh(bool refreshImmediately)
    {
        CancelActivityCatalogRefresh();
        var cancellation = new CancellationTokenSource();
        _activityCatalogRefreshCancellation = cancellation;
        _ = RefreshActivityCatalogWhileVisibleAsync(
            cancellation,
            refreshImmediately);
    }

    private void CancelActivityCatalogRefresh()
    {
        var cancellation = Interlocked.Exchange(
            ref _activityCatalogRefreshCancellation,
            null);
        cancellation?.Cancel();
        cancellation?.Dispose();
    }

    private async Task RefreshActivityCatalogWhileVisibleAsync(
        CancellationTokenSource cancellation,
        bool refreshImmediately)
    {
        try
        {
            if (refreshImmediately)
            {
                await LoadCatalogAsync();
            }

            while (true)
            {
                await Task.Delay(
                    _activityCatalogRefreshInterval,
                    cancellation.Token);
                if (!IsCatalogPageVisible ||
                    !ReferenceEquals(
                        Volatile.Read(ref _activityCatalogRefreshCancellation),
                        cancellation))
                {
                    return;
                }

                await LoadCatalogAsync();
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Trace.TraceError(
                "活动日历自动刷新失败：{0}: {1}",
                exception.GetType().Name,
                exception.Message);
        }
        finally
        {
            if (ReferenceEquals(
                    Interlocked.CompareExchange(
                        ref _activityCatalogRefreshCancellation,
                        null,
                        cancellation),
                    cancellation))
            {
                cancellation.Dispose();
            }
        }
    }

    private bool IsCatalogPageVisible =>
        ActivePage is LauncherPage.Servers or LauncherPage.Activities;

    private async Task RetryCatalogAfterFallbackAsync(
        CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(
                _catalogFallbackRetryDelay,
                cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(
                    Volatile.Read(ref _catalogRetryCancellation),
                    cancellation))
            {
                return;
            }

            await LoadCatalogAsync(fallbackRetryOwner: cancellation);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Trace.TraceError(
                "服务器目录自动重试失败：{0}: {1}",
                exception.GetType().Name,
                exception.Message);
        }
        finally
        {
            Interlocked.CompareExchange(
                ref _catalogRetryCancellation,
                null,
                cancellation);
            cancellation.Dispose();
        }
    }

    private void ScheduleCatalogBoundaryRefresh(IEnumerable<ServerSummary> servers)
    {
        var now = DateTimeOffset.UtcNow;
        var nextBoundary = servers
            .Where(IsActivityServer)
            .SelectMany(server => new[] { server.OpensAt, server.ClosesAt })
            .Where(boundary => boundary is not null && boundary > now)
            .Select(boundary => boundary!.Value)
            .DefaultIfEmpty()
            .Min();
        if (nextBoundary == default)
        {
            Interlocked.Exchange(ref _catalogScheduleCancellation, null)?.Cancel();
            return;
        }

        var cancellation = new CancellationTokenSource();
        Interlocked.Exchange(ref _catalogScheduleCancellation, cancellation)?.Cancel();
        _ = RefreshCatalogAtBoundaryAsync(nextBoundary, cancellation);
    }

    private async Task RefreshCatalogAtBoundaryAsync(
        DateTimeOffset boundary,
        CancellationTokenSource cancellation)
    {
        try
        {
            while (true)
            {
                var delay = GetCatalogBoundaryDelaySlice(
                    boundary,
                    DateTimeOffset.UtcNow);
                if (delay <= TimeSpan.Zero)
                {
                    break;
                }

                await Task.Delay(delay, cancellation.Token);
            }

            await LoadCatalogAsync();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Trace.TraceError(
                "活动目录边界刷新失败：{0}: {1}",
                exception.GetType().Name,
                exception.Message);
        }
        finally
        {
            Interlocked.CompareExchange(
                ref _catalogScheduleCancellation,
                null,
                cancellation);
            cancellation.Dispose();
        }
    }

    internal static TimeSpan GetCatalogBoundaryDelaySlice(
        DateTimeOffset boundary,
        DateTimeOffset now)
    {
        var remaining = boundary - now + CatalogBoundaryGracePeriod;
        if (remaining <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return remaining > MaxCatalogBoundaryDelay
            ? MaxCatalogBoundaryDelay
            : remaining;
    }

    private async Task<IReadOnlyDictionary<string, ActivityClientProfileStateCheck>>
        CheckActivityClientProfileStatesAsync(
            IEnumerable<ActivityServerItemViewModel> activities,
            IReadOnlyDictionary<string, ClientProfileSummary> clientProfiles,
            string dataRoot,
            CancellationToken cancellationToken)
    {
        var profileIds = activities
            .Select(item => item.Server.ClientProfileId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var checks = await Task.WhenAll(profileIds.Select(async profileId =>
        {
            if (!clientProfiles.TryGetValue(profileId, out var profile))
            {
                return new KeyValuePair<string, ActivityClientProfileStateCheck>(
                    profileId,
                    new ActivityClientProfileStateCheck(
                        IsProfileAvailable: false,
                        IsChecked: true,
                        LocalProfileState.Missing));
            }

            try
            {
                var state = await _installationService.GetLocalStateAsync(
                    profile,
                    dataRoot,
                    cancellationToken);
                return new KeyValuePair<string, ActivityClientProfileStateCheck>(
                    profileId,
                    new ActivityClientProfileStateCheck(
                        IsProfileAvailable: true,
                        IsChecked: true,
                        state));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                Trace.TraceError(
                    "Activity client state check failed for profile {0}: {1}",
                    profileId,
                    exception);
                return new KeyValuePair<string, ActivityClientProfileStateCheck>(
                    profileId,
                    new ActivityClientProfileStateCheck(
                        IsProfileAvailable: true,
                        IsChecked: false,
                        LocalProfileState.Missing));
            }
        }));

        return checks.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.Ordinal);
    }

    private static void ApplyActivityClientProfileStateCheck(
        ActivityServerItemViewModel item,
        ActivityClientProfileStateCheck check)
    {
        if (!check.IsProfileAvailable)
        {
            item.MarkClientProfileUnavailable();
        }
        else if (!check.IsChecked)
        {
            item.MarkClientStateCheckFailed();
        }
        else
        {
            item.ApplyClientState(check.State);
        }
    }

    private void ResetAndRefreshActivityClientStates()
    {
        CancelActivityClientStateRefresh();
        foreach (var item in ActivityServers)
        {
            item.ResetClientState();
        }
        RebuildHomeServerList();

        if (ActivityServers.Count == 0)
        {
            return;
        }

        var generation = Interlocked.Increment(
            ref _activityClientStateRefreshGeneration);
        var cancellation = new CancellationTokenSource();
        Interlocked.Exchange(
            ref _activityClientStateRefreshCancellation,
            cancellation)?.Cancel();
        var activities = ActivityServers.ToArray();
        var clientProfiles = new Dictionary<string, ClientProfileSummary>(
            _clientProfiles,
            StringComparer.Ordinal);
        var dataRoot = ClientDirectory;
        _ = RefreshActivityClientStatesAsync(
            activities,
            clientProfiles,
            dataRoot,
            generation,
            cancellation);
    }

    private async Task RefreshActivityClientStatesAsync(
        IReadOnlyList<ActivityServerItemViewModel> activities,
        IReadOnlyDictionary<string, ClientProfileSummary> clientProfiles,
        string dataRoot,
        long generation,
        CancellationTokenSource cancellation)
    {
        try
        {
            var checks = await CheckActivityClientProfileStatesAsync(
                activities,
                clientProfiles,
                dataRoot,
                cancellation.Token);
            if (generation != Volatile.Read(
                    ref _activityClientStateRefreshGeneration) ||
                !ReferenceEquals(
                    _activityClientStateRefreshCancellation,
                    cancellation) ||
                !AreDirectoriesSame(dataRoot, ClientDirectory))
            {
                return;
            }

            foreach (var item in activities)
            {
                ApplyActivityClientProfileStateCheck(
                    item,
                    checks[item.Server.ClientProfileId]);
            }
            RebuildHomeServerList();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Trace.TraceError(
                "Activity client state refresh failed: {0}",
                exception);
        }
        finally
        {
            Interlocked.CompareExchange(
                ref _activityClientStateRefreshCancellation,
                null,
                cancellation);
            cancellation.Dispose();
        }
    }

    private void CancelActivityClientStateRefresh()
    {
        Interlocked.Increment(ref _activityClientStateRefreshGeneration);
        Interlocked.Exchange(
            ref _activityClientStateRefreshCancellation,
            null)?.Cancel();
    }

    private void UpdateActivityProfileState(
        string profileId,
        LocalProfileState state)
    {
        var visibilityChanged = false;
        foreach (var item in ActivityServers.Where(item =>
                     string.Equals(
                         item.Server.ClientProfileId,
                         profileId,
                         StringComparison.Ordinal)))
        {
            var wasInstalled = item.IsClientInstalled;
            item.ApplyClientState(state);
            visibilityChanged |= wasInstalled != item.IsClientInstalled;
        }

        if (visibilityChanged)
        {
            RebuildHomeServerList();
        }
    }

    private void RebuildHomeServerList(string? preferredServerId = null)
    {
        ReplaceHomeServerCollection();
        var targetServerId = preferredServerId ?? SelectedServer?.Id;
        var selectedServer = Servers.FirstOrDefault(server =>
                                 string.Equals(
                                     server.Id,
                                     targetServerId,
                                     StringComparison.Ordinal)) ??
                             Servers.FirstOrDefault(server =>
                                 string.Equals(
                                     server.Id,
                                     _settings.SelectedServerId,
                                     StringComparison.Ordinal)) ??
                             Servers.FirstOrDefault();
        SelectedServer = selectedServer;
    }

    private void ReplaceHomeServerCollection()
    {
        var installedActivityIds = ActivityServers
            .Where(item => item.IsClientInstalled)
            .Select(item => item.Id)
            .ToHashSet(StringComparer.Ordinal);
        var homeServers = _catalogPlayerServers
            .Where(server =>
                !IsActivityServer(server) ||
                installedActivityIds.Contains(server.Id))
            .ToArray();
        if (Servers.SequenceEqual(homeServers))
        {
            return;
        }

        Servers.Clear();
        foreach (var server in homeServers)
        {
            Servers.Add(server);
        }
        RebuildHomeAnnouncements();
        NotifyCatalogStateChanged();
    }

    private void RebuildHomeAnnouncements()
    {
        HomeAnnouncementServers.Clear();
        foreach (var server in Servers
                     .Where(server => !string.IsNullOrWhiteSpace(server.Announcement))
                     .Take(4))
        {
            HomeAnnouncementServers.Add(server);
        }

        OnPropertyChanged(nameof(HasHomeAnnouncements));
        OnPropertyChanged(nameof(HasNoHomeAnnouncements));
    }

    private void RestoreServerSelectionAfterActivityPreparation(
        ServerSummary? previousServer,
        ActivityServerItemViewModel item)
    {
        if (item.IsClientInstalled)
        {
            return;
        }

        SelectedServer = Servers.FirstOrDefault(server =>
                             string.Equals(
                                 server.Id,
                                 previousServer?.Id,
                                 StringComparison.Ordinal)) ??
                         Servers.FirstOrDefault();
    }

    private bool CanPrepareActivityClient(ActivityServerItemViewModel? item) =>
        item is not null &&
        item.CanPrepareClient &&
        IsAuthenticated &&
        !IsLauncherUpdateRequired &&
        CanSelectServer;

    private readonly record struct ActivityClientProfileStateCheck(
        bool IsProfileAvailable,
        bool IsChecked,
        LocalProfileState State);

    private void SelectServer(ServerSummary? server)
    {
        if (server is null || !CanSelectServer)
        {
            return;
        }

        SelectedServer = server;
        ActivePage = LauncherPage.Servers;
        CloseOverlays();
        SaveSettings();
    }

    private async Task PrepareActivityClientAsync(ActivityServerItemViewModel? item)
    {
        if (item is null || !CanPrepareActivityClient(item))
        {
            return;
        }

        if (!_clientProfiles.ContainsKey(item.Server.ClientProfileId))
        {
            item.MarkClientProfileUnavailable();
            ShowToast("该活动客户端尚未发布", ToastLevel.Error);
            return;
        }

        CancelActivityClientStateRefresh();
        var previousServer = SelectedServer;
        _suppressNextAutomaticProfileCheck = true;
        SelectedServer = item.Server;
        if (!await RefreshClientStateAsync())
        {
            RestoreServerSelectionAfterActivityPreparation(previousServer, item);
            ShowToast("客户端状态检查未完成，请重试", ToastLevel.Error);
            return;
        }

        if (_selectedProfileState == LocalProfileState.Ready)
        {
            UpdateActivityProfileState(
                item.Server.ClientProfileId,
                LocalProfileState.Ready);
            RebuildHomeServerList(item.Id);
            ActivePage = LauncherPage.Servers;
            ShowToast($"{item.Name} 客户端已准备", ToastLevel.Success);
            return;
        }

        var wasInstalled = item.IsClientInstalled;
        if (await InstallSelectedProfileAsync(isRepair: false))
        {
            UpdateActivityProfileState(
                item.Server.ClientProfileId,
                LocalProfileState.Ready);
            RebuildHomeServerList(item.Id);
            ShowToast($"{item.Name} 已加入服务器主页", ToastLevel.Success);
            return;
        }

        if (!wasInstalled)
        {
            RestoreServerSelectionAfterActivityPreparation(previousServer, item);
        }
    }

    private async Task StartPrimaryActionAsync()
    {
        if (_isUiPreview)
        {
            ShowToast("UI 预览不会启动游戏");
            return;
        }

        if (IsLauncherUpdateRequired)
        {
            IsLauncherUpdateVisible = true;
            ShowToast("当前版本已停止支持，请先完成启动器更新", ToastLevel.Error);
            return;
        }

        var selectedServer = SelectedServer;
        var dataRoot = ClientDirectory;
        var clientContextGeneration = Volatile.Read(ref _clientContextGeneration);
        if (IsProgressActive || selectedServer is null)
        {
            return;
        }

        if (!IsAuthenticated)
        {
            ActivePage = LauncherPage.Account;
            ShowToast("请先注册或登录赫朝账号");
            return;
        }

        if (IsActivityServer(selectedServer))
        {
            if (!await RefreshActivityServerBeforeActionAsync(
                    selectedServer.Id,
                    dataRoot,
                    clientContextGeneration))
            {
                return;
            }

            selectedServer = SelectedServer!;
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

            if (!IsClientContextCurrent(
                    selectedServer.Id,
                    dataRoot,
                    clientContextGeneration))
            {
                return;
            }
        }

        if (_selectedProfileState != LocalProfileState.Ready)
        {
            if (!await InstallSelectedProfileAsync(isRepair: false))
            {
                return;
            }

            if (!IsClientContextCurrent(
                    selectedServer.Id,
                    dataRoot,
                    clientContextGeneration))
            {
                return;
            }
        }

        if (!selectedServer.CanJoin)
        {
            ShowToast(GetJoinAccessDeniedMessage(selectedServer), ToastLevel.Error);
            return;
        }

        if (selectedServer.Status != ServerStatus.Online)
        {
            ShowToast(selectedServer.Status == ServerStatus.Maintenance
                ? "服务器正在维护，客户端已保留"
                : "服务器暂未开放，客户端已保留");
            return;
        }

        if (!IsMinecraftLinked)
        {
            ActivePage = LauncherPage.Account;
            ShowToast("客户端已就绪，请绑定 Minecraft 正版身份");
            return;
        }

        await LaunchSelectedServerAsync();
    }

    private async Task<bool> RefreshActivityServerBeforeActionAsync(
        string serverId,
        string dataRoot,
        long clientContextGeneration)
    {
        var appliedLiveCatalog = await LoadCatalogAsync();
        if (appliedLiveCatalog &&
            IsClientContextCurrent(
                serverId,
                dataRoot,
                clientContextGeneration))
        {
            return true;
        }

        ShowToast("无法确认活动服当前状态，请刷新后重试", ToastLevel.Error);
        return false;
    }

    private async Task<bool> InstallSelectedProfileAsync(bool isRepair)
    {
        var selectedServer = SelectedServer;
        var dataRoot = ClientDirectory;
        var clientContextGeneration = Volatile.Read(ref _clientContextGeneration);
        var keepObjectCache = KeepDownloadsAfterClose;
        if (IsProgressActive || selectedServer is null ||
            !_clientProfiles.TryGetValue(selectedServer.ClientProfileId, out var profile))
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
                value => DispatchToUi(() =>
                {
                    if (IsClientContextCurrent(
                            selectedServer.Id,
                            dataRoot,
                            clientContextGeneration))
                    {
                        ApplyInstallProgress(value);
                    }
                }));
            await _installationService.InstallAsync(
                profile,
                new ClientInstallationOptions(dataRoot, keepObjectCache),
                progress,
                _activeInstallCancellation.Token);
            if (IsClientContextCurrent(
                    selectedServer.Id,
                    dataRoot,
                    clientContextGeneration))
            {
                _selectedProfileState = LocalProfileState.Ready;
                _selectedProfileStateChecked = true;
                await RefreshRollbackCandidateAsync(
                    profile,
                    selectedServer.Id,
                    dataRoot,
                    clientContextGeneration);
                if (IsClientContextCurrent(
                        selectedServer.Id,
                        dataRoot,
                        clientContextGeneration))
                {
                    NotifySelectedProfileJavaPropertiesChanged();
                    CreateDiagnosticBundleCommand.RaiseCanExecuteChanged();
                    UpdateProgress = 100;
                    ClientStatusText = "客户端已就绪";
                    UpdatePrimaryActionForState();
                }
            }

            ShowToast(
                isRepair ? "客户端修复完成" : "客户端安装完成",
                ToastLevel.Success);
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
            ShowToast("该客户端档案尚未发布下载清单", ToastLevel.Error);
        }
        catch (LauncherApiException exception)
        {
            telemetryFailure = LauncherTelemetryFailureCode.ApiUnavailable;
            ClientStatusText = "下载服务不可用";
            ShowToast(exception.ApiDetail ?? "客户端分发服务暂时不可用", ToastLevel.Error);
        }
        catch (ManifestSignatureException)
        {
            telemetryFailure = LauncherTelemetryFailureCode.SignatureInvalid;
            ClientStatusText = "清单签名无效";
            ShowToast("客户端清单未通过签名验证，安装已停止", ToastLevel.Error);
        }
        catch (Exception exception) when (exception is ManifestIntegrityException or ClientManifestMismatchException)
        {
            telemetryFailure = LauncherTelemetryFailureCode.IntegrityFailed;
            ClientStatusText = "文件校验失败";
            ShowToast("下载内容与发布清单不一致，安装已停止", ToastLevel.Error);
        }
        catch (InsufficientDiskSpaceException)
        {
            telemetryFailure = LauncherTelemetryFailureCode.InsufficientDiskSpace;
            ClientStatusText = "磁盘空间不足";
            ShowToast("游戏数据目录所在磁盘空间不足", ToastLevel.Error);
        }
        catch (ProfileInstallInProgressException)
        {
            telemetryFailure = LauncherTelemetryFailureCode.InstallBusy;
            ClientStatusText = "安装正在进行";
            ShowToast("另一个启动器窗口正在安装这个客户端", ToastLevel.Error);
        }
        catch (ProfileJavaRuntimeException)
        {
            telemetryFailure = LauncherTelemetryFailureCode.RuntimePreparationFailed;
            ClientStatusText = "Java 安装未完成";
            ShowToast(
                "客户端文件已校验，但配套 Java 安装失败；重新修复会继续下载",
                ToastLevel.Error);
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
            ShowToast("客户端安装中断，重新操作会从已下载位置继续", ToastLevel.Error);
        }
        catch (Exception exception)
        {
            Trace.TraceError(
                "Unexpected client installation failure: {0}",
                exception);
            ClientStatusText = "安装已安全停止";
            completionMessage = "启动器遇到未预期的安装错误";
            ShowToast("安装已安全停止，客户端当前版本没有被替换", ToastLevel.Error);
        }
        finally
        {
            var completedBytes = ActiveDownload?.CompletedBytes;
            completionMessage ??= succeeded ? null : ClientStatusText;
            CompleteActiveDownload(completionStatus, completionMessage);
            IsProgressActive = false;
            RecordTelemetryInBackground(() => _telemetryService.RecordAsync(
                isRepair
                    ? LauncherTelemetryEventType.Repair
                    : LauncherTelemetryEventType.Install,
                telemetryOutcome,
                telemetryFailure,
                profile.Id,
                profile.Version,
                telemetryClock.Elapsed,
                completedBytes));
            _isInstallingClient = false;
            _activeInstallCancellation?.Dispose();
            _activeInstallCancellation = null;
            CancelDownloadCommand.RaiseCanExecuteChanged();
            var targetStillSelected = IsClientContextCurrent(
                selectedServer.Id,
                dataRoot,
                clientContextGeneration);
            if (succeeded)
            {
                UpdateActivityProfileState(profile.Id, LocalProfileState.Ready);
            }
            if (!targetStillSelected)
            {
                _clientInstallPhase = null;
                _installStepFailed = false;
            }
            else if (succeeded)
            {
                _clientInstallPhase = ClientInstallPhase.Complete;
                _installStepFailed = false;
            }
            else if (completionStatus == DownloadJobStatus.Failed)
            {
                _installStepFailed = true;
            }
            else
            {
                _clientInstallPhase = null;
                _installStepFailed = false;
            }
            NotifyProgressStepStatesChanged();
            if (targetStillSelected)
            {
                UpdatePrimaryActionForState();
            }
            else
            {
                await RefreshClientStateAsync();
            }
        }

        return succeeded;
    }

    public async Task<bool> DeleteSelectedProfileAsync()
    {
        var selectedServer = SelectedServer;
        if (selectedServer is null ||
            !_clientProfiles.TryGetValue(selectedServer.ClientProfileId, out var profile))
        {
            ShowToast("当前服务器没有可删除的客户端档案");
            return false;
        }

        if (_selectedProfileState == LocalProfileState.Missing)
        {
            ShowToast("当前客户端尚未安装");
            return false;
        }

        if (IsProgressActive)
        {
            return false;
        }

        if (_gameLauncherService.IsProfileRunning(profile.Id))
        {
            ShowToast("请先退出当前客户端再删除");
            return false;
        }

        var dataRoot = ClientDirectory;
        var clientContextGeneration = Volatile.Read(ref _clientContextGeneration);
        var deleted = false;
        IsProgressActive = true;
        UpdateProgress = 10;
        ClientStatusText = "正在保留个人游戏设置";
        PrimaryActionText = "正在删除";
        try
        {
            await _playerGameSettingsService.ImportLatestAsync(dataRoot);
            UpdateProgress = 35;
            ClientStatusText = "正在删除客户端文件";
            await _installationService.DeleteAsync(profile, dataRoot);

            deleted = true;
            if (!IsClientContextCurrent(
                    selectedServer.Id,
                    dataRoot,
                    clientContextGeneration))
            {
                ShowToast("客户端已删除，当前目录或服务器选择已变化");
                return true;
            }

            _selectedProfileState = LocalProfileState.Missing;
            _selectedProfileStateChecked = true;
            SetRollbackCandidate(null);
            NotifySelectedProfileJavaPropertiesChanged();
            CreateDiagnosticBundleCommand.RaiseCanExecuteChanged();
            UpdateProgress = 0;
            ClientStatusText = "客户端已删除，个人设置已保留";
            ShowToast(
                $"已删除 {profile.DisplayName}，可随时重新安装",
                ToastLevel.Success);
        }
        catch (ProfileInstallInProgressException)
        {
            ClientStatusText = "客户端正在被其他任务使用";
            ShowToast("另一个启动器窗口正在安装、回滚或删除这个客户端");
        }
        catch (PlayerGameSettingsException)
        {
            ClientStatusText = "个人游戏设置尚未安全保存";
            ShowToast("删除已取消，未能先保存灵敏度和按键设置", ToastLevel.Error);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ClientStatusText = "客户端删除失败";
            ShowToast("部分文件仍被占用，客户端没有被标记为已删除", ToastLevel.Error);
        }
        finally
        {
            IsProgressActive = false;
            if (deleted)
            {
                UpdateActivityProfileState(profile.Id, LocalProfileState.Missing);
            }
            OnPropertyChanged(nameof(CanDeleteSelectedProfile));
            OnPropertyChanged(nameof(DeleteProfileToolTip));
            UpdatePrimaryActionForState();
        }

        return deleted;
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

        var dataRoot = ClientDirectory;
        var clientContextGeneration = Volatile.Read(ref _clientContextGeneration);
        var activatedVersion = _rollbackCandidate.Version;
        var telemetryClock = Stopwatch.StartNew();
        var telemetryOutcome = LauncherTelemetryOutcome.Failure;
        var telemetryFailure = LauncherTelemetryFailureCode.Unexpected;
        LocalProfileState? activityProfileState = null;
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
                dataRoot,
                progress);
            activityProfileState = string.Equals(
                    activatedState.Version,
                    profile.Version,
                    StringComparison.Ordinal)
                ? LocalProfileState.Ready
                : LocalProfileState.UpdateRequired;

            if (!IsClientContextCurrent(
                    selectedServer.Id,
                    dataRoot,
                    clientContextGeneration))
            {
                telemetryOutcome = LauncherTelemetryOutcome.Success;
                telemetryFailure = LauncherTelemetryFailureCode.None;
                ShowToast("客户端已回滚，当前目录或服务器选择已变化");
                return true;
            }
            switched = true;
            _selectedProfileState = activityProfileState.Value;
            _selectedProfileStateChecked = true;
            UpdateProgress = 100;
            ClientStatusText = $"已回滚到 v{activatedState.Version}";
            NotifySelectedProfileJavaPropertiesChanged();
            ShowToast(
                $"客户端已回滚到 v{activatedState.Version}，存档与设置已保留",
                ToastLevel.Success);
            telemetryOutcome = LauncherTelemetryOutcome.Success;
            telemetryFailure = LauncherTelemetryFailureCode.None;
            return true;
        }
        catch (ProfileRollbackRuntimeException exception)
        {
            telemetryFailure = LauncherTelemetryFailureCode.RuntimePreparationFailed;
            switched = true;
            activityProfileState = LocalProfileState.UpdateRequired;
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
            ShowToast("上一版本不存在或未通过完整性检查", ToastLevel.Error);
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
            ShowToast("客户端回滚已取消，当前版本保持不变", ToastLevel.Error);
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
            ShowToast("回滚已安全停止，当前客户端版本没有被替换", ToastLevel.Error);
        }
        finally
        {
            IsProgressActive = false;
            if (activityProfileState is { } localState)
            {
                UpdateActivityProfileState(profile.Id, localState);
            }
            RecordTelemetryInBackground(() => _telemetryService.RecordAsync(
                LauncherTelemetryEventType.Rollback,
                telemetryOutcome,
                telemetryFailure,
                profile.Id,
                activatedVersion,
                telemetryClock.Elapsed));
            await RefreshRollbackCandidateAsync(
                profile,
                selectedServer.Id,
                dataRoot,
                clientContextGeneration);
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
        var dataRoot = ClientDirectory;
        var clientContextGeneration = Volatile.Read(ref _clientContextGeneration);
        var memoryMb = ParseMemoryInMiB(SelectedMemory);
        var customJavaPath = GetSelectedProfileCustomJavaPath();
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
        var progress = new InlineProgress<MinecraftLaunchProgress>(
            value => DispatchToUi(() =>
            {
                if (IsClientContextCurrent(
                        selectedServer.Id,
                        dataRoot,
                        clientContextGeneration))
                {
                    ApplyLaunchProgress(value);
                }
            }));

        try
        {
            var launchSession = await GetMinecraftLaunchSessionWithRefreshAsync();
            SetCurrentAccount(_authenticationService.CurrentAccount);
            var runningGame = _gameLauncherService.GetRunningGame();
            if (runningGame is not null)
            {
                ClientStatusText = "正在安全关闭当前游戏";
                PrimaryActionText = "正在切换";
                var stopProgress = new InlineProgress<MinecraftStopProgress>(
                    value => DispatchToUi(() =>
                    {
                        if (IsClientContextCurrent(
                                selectedServer.Id,
                                dataRoot,
                                clientContextGeneration))
                        {
                            ApplyStopProgress(value);
                        }
                    }));
                await _gameLauncherService.StopRunningGameAsync(
                    TimeSpan.FromSeconds(15),
                    stopProgress);
                await _playerGameSettingsService.CaptureProfileAsync(
                    runningGame.DataRoot ?? dataRoot,
                    runningGame.ProfileId);
                _runningServerId = null;
                OnPropertyChanged(nameof(CanRollbackSelectedProfile));
                OnPropertyChanged(nameof(RollbackProfileToolTip));
                OnPropertyChanged(nameof(CanDeleteSelectedProfile));
                OnPropertyChanged(nameof(DeleteProfileToolTip));
            }

            await _playerGameSettingsService.ApplyToProfileAsync(
                dataRoot,
                selectedServer.ClientProfileId);
            await _gameLauncherService.LaunchAsync(
                new MinecraftLaunchRequest(
                    dataRoot,
                    selectedServer.ClientProfileId,
                    memoryMb,
                    launchSession,
                    customJavaPath,
                    selectedServer.Id),
                progress,
                async cancellationToken =>
                {
                    await _authenticationService.PrepareVelocityLaunchAsync(
                        selectedServer.Id,
                        cancellationToken);
                });
            _runningServerId = selectedServer.Id;
            OnPropertyChanged(nameof(CanChangeClientDirectory));
            ResetLauncherSettingsCommand.RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(CanRollbackSelectedProfile));
            OnPropertyChanged(nameof(RollbackProfileToolTip));
            OnPropertyChanged(nameof(CanDeleteSelectedProfile));
            OnPropertyChanged(nameof(DeleteProfileToolTip));
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
            ShowToast(
                "当前游戏未能退出，已取消切换且没有申请新服授权",
                ToastLevel.Error);
        }
        catch (PlayerGameSettingsException)
        {
            telemetryFailure = LauncherTelemetryFailureCode.IoFailure;
            ClientStatusText = "个人游戏设置同步失败";
            ShowToast(
                "无法安全同步灵敏度和按键设置，游戏没有启动",
                ToastLevel.Error);
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
            ShowToast("无法连接赫朝授权服务，请稍后再试", ToastLevel.Error);
        }
        finally
        {
            IsProgressActive = false;
            RecordTelemetryInBackground(() => _telemetryService.RecordAsync(
                LauncherTelemetryEventType.Launch,
                telemetryOutcome,
                telemetryFailure,
                selectedServer.ClientProfileId,
                profileVersion,
                telemetryClock.Elapsed));
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
        _clientInstallPhase = progress.Phase;
        _installStepFailed = false;
        NotifyProgressStepStatesChanged();
        UpdateProgress = progress.Percent;
        ActiveDownload?.Update(
            progress.Phase,
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
        _clientInstallPhase = ClientInstallPhase.Checking;
        _installStepFailed = false;
        NotifyProgressStepStatesChanged();
        ActiveDownload = new DownloadJobViewModel(
            Guid.NewGuid(),
            profile.Id,
            isRepair ? $"修复 · {profile.DisplayName}" : profile.DisplayName,
            profile.Version,
            DateTimeOffset.UtcNow,
            DownloadJobStatus.Running,
            0,
            profile.DownloadBytes,
            string.Empty,
            phase: ClientInstallPhase.Checking);
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
        var previousHistory = DownloadHistory.ToArray();
        DownloadHistory.Clear();
        if (!PersistDownloadHistory())
        {
            foreach (var download in previousHistory)
            {
                DownloadHistory.Add(download);
            }

            NotifyDownloadHistoryChanged();
            ShowToast("无法写入下载历史，记录未清空", ToastLevel.Error);
            return;
        }

        NotifyDownloadHistoryChanged();
        ShowToast("下载历史已清空", ToastLevel.Success);
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

    private bool PersistDownloadHistory()
    {
        try
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
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                ArgumentException or NotSupportedException)
        {
            Trace.TraceWarning(
                "Unable to persist download history: {0}",
                exception.Message);
            return false;
        }
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
        _ = RecordGameExitAsync(record, eventArgs.ExitKind, eventArgs.DataRoot);
    }

    private async Task RecordGameExitAsync(
        GameExitRecord record,
        MinecraftProcessExitKind exitKind,
        string? dataRoot)
    {
        try
        {
            await _playerGameSettingsService.CaptureProfileAsync(
                dataRoot ?? ClientDirectory,
                record.ProfileId);
        }
        catch (PlayerGameSettingsException exception)
        {
            Trace.TraceWarning(
                "Unable to capture shared player settings after game exit: {0}",
                exception.Message);
        }

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
        RecordTelemetryInBackground(() => _telemetryService.RecordAsync(
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
            record.ExitedAt - record.StartedAt));

        DispatchToUi(() =>
        {
            _latestGameExit = record;
            OnPropertyChanged(nameof(LatestGameExitText));
            OnPropertyChanged(nameof(CanRollbackSelectedProfile));
            OnPropertyChanged(nameof(RollbackProfileToolTip));
            OnPropertyChanged(nameof(CanDeleteSelectedProfile));
            OnPropertyChanged(nameof(DeleteProfileToolTip));
            OnPropertyChanged(nameof(CanChangeClientDirectory));
            var runningGame = _gameLauncherService.GetRunningGame();
            _runningServerId = runningGame?.ServerId;
            ResetLauncherSettingsCommand.RaiseCanExecuteChanged();
            InstallLauncherUpdateCommand.RaiseCanExecuteChanged();
            if (runningGame is null && _launcherUpdatePlan is not null)
            {
                TryPresentLauncherUpdatePlan();
            }
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
        !_isUiPreview &&
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
            ShowToast(
                "无法生成诊断包，请先确认客户端已安装且日志可读取",
                ToastLevel.Error);
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
            ShowToast(
                "诊断包已安全上传，管理员下载会写入审计记录",
                ToastLevel.Success);
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
            ShowToast("诊断上传失败，请检查网络后重试", ToastLevel.Error);
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
        if (_isUiPreview)
        {
            ShowToast("UI 预览不会打开或创建真实目录");
            return;
        }

        try
        {
            Directory.CreateDirectory(_gameDiagnosticsService.DiagnosticsDirectory);
            OpenDirectory(_gameDiagnosticsService.DiagnosticsDirectory);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            System.ComponentModel.Win32Exception)
        {
            ShowToast("暂时无法打开诊断目录", ToastLevel.Error);
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

    private void RecordTelemetryInBackground(Func<Task> operation)
    {
        _ = RecordTelemetryWithTimeoutAsync(operation);
    }

    private static async Task RecordTelemetryWithTimeoutAsync(
        Func<Task> operation)
    {
        try
        {
            await operation().WaitAsync(TimeSpan.FromSeconds(3));
        }
        catch (Exception exception) when (
            exception is TimeoutException or
            IOException or
            UnauthorizedAccessException or
            HttpRequestException or
            TaskCanceledException)
        {
            Trace.TraceWarning(
                "Launcher telemetry was deferred or dropped: {0}",
                exception.Message);
        }
        catch (Exception exception)
        {
            Trace.TraceWarning(
                "Launcher telemetry failed: {0}",
                exception.Message);
        }
    }

    private async Task<bool> RefreshClientStateAsync()
    {
        var selectedServer = SelectedServer;
        if (selectedServer is null || IsProgressActive ||
            !_clientProfiles.TryGetValue(selectedServer.ClientProfileId, out var profile))
        {
            return false;
        }

        var dataRoot = ClientDirectory;
        var generation = Interlocked.Increment(ref _clientStateRefreshGeneration);
        var clientContextGeneration = Volatile.Read(ref _clientContextGeneration);
        try
        {
            var stateTask = _installationService.GetLocalStateAsync(
                profile,
                dataRoot);
            var rollbackTask = _installationService.GetRollbackCandidateAsync(
                profile,
                dataRoot);
            await Task.WhenAll(stateTask, rollbackTask);
            var state = await stateTask;
            var rollbackCandidate = await rollbackTask;
            if (!IsClientStateRefreshCurrent(
                    generation,
                    selectedServer.Id,
                    dataRoot,
                    clientContextGeneration))
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
            UpdateActivityProfileState(profile.Id, state);
            if (!IsClientContextCurrent(
                    selectedServer.Id,
                    dataRoot,
                    clientContextGeneration))
            {
                return true;
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
            if (IsClientStateRefreshCurrent(
                    generation,
                    selectedServer.Id,
                    dataRoot,
                    clientContextGeneration))
            {
                _selectedProfileStateChecked = false;
                UpdateProgress = 0;
                ClientStatusText = "客户端检查未完成";
                UpdatePrimaryActionForState();
            }
            return false;
        }
    }

    private bool IsClientStateRefreshCurrent(
        long generation,
        string serverId,
        string dataRoot,
        long clientContextGeneration) =>
        generation == Volatile.Read(ref _clientStateRefreshGeneration) &&
        !IsProgressActive &&
        IsClientContextCurrent(serverId, dataRoot, clientContextGeneration);

    private async Task RefreshRollbackCandidateAsync(
        ClientProfileSummary profile,
        string? selectedServerId,
        string? dataRoot = null,
        long? clientContextGeneration = null)
    {
        var targetDataRoot = dataRoot ?? ClientDirectory;
        try
        {
            var candidate = await _installationService.GetRollbackCandidateAsync(
                profile,
                targetDataRoot);
            if (IsClientContextCurrent(
                    selectedServerId,
                    targetDataRoot,
                    clientContextGeneration))
            {
                SetRollbackCandidate(candidate);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            if (IsClientContextCurrent(
                    selectedServerId,
                    targetDataRoot,
                    clientContextGeneration))
            {
                SetRollbackCandidate(null);
            }
        }
    }

    private void InvalidateClientContext()
    {
        Interlocked.Increment(ref _clientContextGeneration);
        Interlocked.Increment(ref _clientStateRefreshGeneration);
    }

    private bool IsClientContextCurrent(
        string? serverId,
        string dataRoot,
        long? expectedGeneration = null) =>
        (!expectedGeneration.HasValue ||
         Volatile.Read(ref _clientContextGeneration) == expectedGeneration.Value) &&
        string.Equals(SelectedServer?.Id, serverId, StringComparison.Ordinal) &&
        AreDirectoriesSame(ClientDirectory, dataRoot);

    private static bool AreDirectoriesSame(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(Environment.ExpandEnvironmentVariables(left)),
                Path.GetFullPath(Environment.ExpandEnvironmentVariables(right)),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void OpenClientDirectory()
    {
        if (_isUiPreview)
        {
            ShowToast("UI 预览不会打开或创建真实目录");
            return;
        }

        try
        {
            var expandedPath = Environment.ExpandEnvironmentVariables(ClientDirectory);
            Directory.CreateDirectory(expandedPath);
            Process.Start(new ProcessStartInfo(expandedPath) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            ShowToast("暂时无法打开游戏数据目录", ToastLevel.Error);
        }
    }

    private void OpenSelectedProfileGameDirectory()
    {
        if (_isUiPreview)
        {
            ShowToast("UI 预览不会打开或创建真实目录");
            return;
        }

        try
        {
            var gameDirectory = SelectedProfileGameDirectory;
            Directory.CreateDirectory(gameDirectory);
            Process.Start(new ProcessStartInfo(gameDirectory) { UseShellExecute = true });
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            System.ComponentModel.Win32Exception)
        {
            ShowToast("暂时无法打开当前客户端游戏目录", ToastLevel.Error);
        }
    }

    public void UpdateClientDirectory(string path)
    {
        if (!CanChangeClientDirectory)
        {
            ShowToast(_gameLauncherService.GetRunningGame() is not null
                ? "请先退出正在运行的游戏再更改目录"
                : "当前任务完成后才能更改目录");
            return;
        }

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
        InvalidateClientContext();
        _selectedProfileState = LocalProfileState.Missing;
        _selectedProfileStateChecked = false;
        OnPropertyChanged(nameof(ClientDirectory));
        OnPropertyChanged(nameof(SelectedProfileGameDirectory));
        OnPropertyChanged(nameof(SelectedProfileGameDirectoryDisplayText));
        NotifySelectedProfileJavaPropertiesChanged();
        CreateDiagnosticBundleCommand.RaiseCanExecuteChanged();
        UpdateProgress = 0;
        ClientStatusText = "正在检查客户端";
        UpdatePrimaryActionForState();
        SaveSettings();
        ResetAndRefreshActivityClientStates();
        _ = TryImportPlayerGameSettingsAsync();
        _ = RefreshClientStateAsync();
        ShowToast("游戏数据目录已更新", ToastLevel.Success);
    }

    private void ResetLauncherSettings()
    {
        SelectedMemory = "6 GB";
        _clientDirectory = JsonLauncherSettingsStore.DefaultClientDataDirectory;
        InvalidateClientContext();
        _profileJavaPaths.Clear();
        _selectedProfileState = LocalProfileState.Missing;
        _selectedProfileStateChecked = false;
        OnPropertyChanged(nameof(ClientDirectory));
        OnPropertyChanged(nameof(SelectedProfileGameDirectory));
        OnPropertyChanged(nameof(SelectedProfileGameDirectoryDisplayText));
        NotifySelectedProfileJavaPropertiesChanged();
        CreateDiagnosticBundleCommand.RaiseCanExecuteChanged();
        UpdateProgress = 0;
        ClientStatusText = "正在检查客户端";
        UpdatePrimaryActionForState();
        CheckForUpdates = true;
        KeepDownloadsAfterClose = true;
        CloseLauncherAfterGameStart = false;
        OpenDownloadsWhenInstalling = true;
        _useSystemProxy = false;
        OnPropertyChanged(nameof(UseSystemProxy));
        UseDarkMode = true;
        SelectedStartupPage = "服务器";
        SaveSettings();
        ResetAndRefreshActivityClientStates();
        _ = TryImportPlayerGameSettingsAsync();
        _ = RefreshClientStateAsync();
        ShowToast("启动器设置已恢复默认", ToastLevel.Success);
    }

    private bool CanUseSelectedServer()
    {
        if (IsLauncherUpdateRequired || IsProgressActive || SelectedServer is null)
        {
            return false;
        }

        return !IsAuthenticated ||
               !_selectedProfileStateChecked ||
               _selectedProfileState != LocalProfileState.Ready ||
               (SelectedServer.CanJoin &&
                SelectedServer.Status == ServerStatus.Online);
    }

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
            await TryCheckLauncherUpdateAsync();
            ShowToast($"欢迎回来，{account.DisplayName}", ToastLevel.Success);
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
        string code,
        bool legalAccepted)
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

        if (!legalAccepted)
        {
            SetAccountFormStatus(
                "请先勾选并同意用户协议、隐私政策与社区规则。",
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
                code.Trim(),
                legalAccepted);
            SetCurrentAccount(account);
            SetAccountFormStatus(string.Empty, isError: false);
            await LoadCatalogAsync(userInitiated: true);
            await TryCheckLauncherUpdateAsync();
            ShowToast($"赫朝账号 @{account.Username} 已创建，并已同步社区");
            return true;
        }
        catch (ForumRegistrationException exception)
        {
            SetAccountFormStatus(exception.Detail, isError: true);
            return false;
        }
        catch (RegistrationLoginFailedException exception)
        {
            Trace.TraceWarning(
                "Hechao account registration completed but automatic login failed: {0}",
                exception.InnerException?.GetType().Name ?? exception.GetType().Name);
            SetAccountFormStatus(
                "赫朝账号已经创建，但自动登录失败。请在左侧使用邮箱或用户名登录。",
                isError: false);
            ShowToast("账号已创建，请重新登录", ToastLevel.Success);
            return true;
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
        catch (Exception exception)
        {
            Trace.TraceError(
                "Unexpected Hechao account registration failure: {0}",
                exception);
            SetAccountFormStatus(
                "账号请求未完成，请检查填写内容后重试。",
                isError: true);
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
            StartRegistrationCodeCooldown();
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
        catch (Exception exception)
        {
            Trace.TraceError(
                "Unexpected registration-code request failure: {0}",
                exception);
            SetAccountFormStatus(
                "验证码请求未完成，请检查邮箱后重试。",
                isError: true);
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
            ShowToast(
                $"已绑定 Minecraft 玩家 {account.MinecraftName}",
                ToastLevel.Success);
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
        ShowToast(
            "操作已安全停止，请重试；现有客户端文件未被替换",
            ToastLevel.Error);
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

    private void HandleUnexpectedLauncherUpdateError(Exception exception)
    {
        Trace.TraceError(
            "Launcher self-update failed without replacing the current version: {0}",
            exception);
        IsLauncherUpdateBusy = false;
        LauncherUpdateStatus =
            "更新未完成，当前版本仍可继续使用。请稍后重试。";
        ShowToast("启动器更新失败，当前版本未被替换", ToastLevel.Error);
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
            ShowToast("已退出赫朝账号", ToastLevel.Success);
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
            ShowToast(
                $"已退出所有设备，共撤销 {deviceCount} 个启动器会话",
                ToastLevel.Success);
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
            ShowToast("管理后台已在浏览器中打开", ToastLevel.Success);
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
            ShowToast("暂时无法打开管理后台", ToastLevel.Error);
        }
        finally
        {
            IsAdminConsoleBusy = false;
        }
    }

    private void SetCurrentAccount(HechaoAccount? account)
    {
        var accountChanged = _currentAccount?.UserId != account?.UserId;
        if (accountChanged)
        {
            InvalidateClientContext();
        }
        var keepCurrentSkin =
            _accountSkinSource is not null &&
            _currentAccount?.MinecraftUuid is Guid currentMinecraftUuid &&
            account?.MinecraftUuid == currentMinecraftUuid;
        var skinRevision = Interlocked.Increment(ref _accountSkinRevision);
        _currentAccount = account;
        if (accountChanged)
        {
            InvalidateCatalogLoad();
        }

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
        OnPropertyChanged(nameof(TopBarAccountSubtitle));
        OnPropertyChanged(nameof(MinecraftIdentityText));
        OnPropertyChanged(nameof(MinecraftLinkStatusText));
        OnPropertyChanged(nameof(AccountActionGlyph));
        OnPropertyChanged(nameof(AccountActionTooltip));
        OnPropertyChanged(nameof(CanUploadDiagnosticBundle));
        if (!keepCurrentSkin)
        {
            SetAccountSkinSource(null);
        }
        if (!keepCurrentSkin &&
            account?.MinecraftUuid is Guid minecraftUuid)
        {
            _ = LoadAccountSkinAsync(minecraftUuid, skinRevision);
        }
        PrimaryActionCommand.RaiseCanExecuteChanged();
        PrepareActivityClientCommand.RaiseCanExecuteChanged();
        LogoutAccountCommand.RaiseCanExecuteChanged();
        LinkMinecraftCommand.RaiseCanExecuteChanged();
        UnlinkMinecraftCommand.RaiseCanExecuteChanged();
        LogoutAllDevicesCommand.RaiseCanExecuteChanged();
        OpenAdminConsoleCommand.RaiseCanExecuteChanged();
        CheckLauncherUpdateCommand.RaiseCanExecuteChanged();
        UpdatePrimaryActionForState();
    }

    private async Task LoadAccountSkinAsync(
        Guid minecraftUuid,
        long revision)
    {
        try
        {
            var skin = await _minecraftSkinService.GetSkinAsync(minecraftUuid);
            if (skin is null)
            {
                return;
            }

            var image = CreateFrozenSkinImage(skin.PngBytes);
            void Apply()
            {
                if (revision == Interlocked.Read(ref _accountSkinRevision) &&
                    _currentAccount?.MinecraftUuid == minecraftUuid)
                {
                    SetAccountSkinSource(image);
                }
            }

            if (_uiContext is not null &&
                SynchronizationContext.Current != _uiContext)
            {
                _uiContext.Post(_ => Apply(), null);
            }
            else
            {
                Apply();
            }
        }
        catch (Exception exception) when (
            exception is IOException or NotSupportedException or
                FileFormatException or ArgumentException)
        {
            // A bad cache entry falls back to the built-in local avatar.
        }
    }

    private void SetAccountSkinSource(ImageSource? image)
    {
        if (ReferenceEquals(_accountSkinSource, image))
        {
            return;
        }

        _accountSkinSource = image;
        OnPropertyChanged(nameof(AccountSkinSource));
        OnPropertyChanged(nameof(HasAccountSkin));
    }

    private static ImageSource CreateFrozenSkinImage(byte[] pngBytes)
    {
        using var stream = new MemoryStream(pngBytes, writable: false);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
        image.StreamSource = stream;
        image.EndInit();
        if (image.PixelWidth != 64 ||
            image.PixelHeight is not (32 or 64))
        {
            throw new InvalidDataException(
                "The Minecraft skin image has unsupported dimensions.");
        }

        image.Freeze();
        return image;
    }

    private static string FormatFileSize(long bytes) =>
        bytes >= 1024 * 1024
            ? $"{bytes / (1024d * 1024d):0.0} MiB"
            : $"{Math.Max(1, bytes / 1024d):0.0} KiB";

    private void ClearAuthenticatedState()
    {
        SetCurrentAccount(null);
        CancelActivityClientStateRefresh();
        _catalogPlayerServers.Clear();
        Servers.Clear();
        ActivityServers.Clear();
        ActivityCalendar.ReplaceActivities(ActivityServers);
        HomeAnnouncementServers.Clear();
        OnPropertyChanged(nameof(HasHomeAnnouncements));
        OnPropertyChanged(nameof(HasNoHomeAnnouncements));
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
                        : SelectedServer?.CanJoin == false
                            ? "称号权限不足"
                        : SelectedServer?.Status != ServerStatus.Online
                            ? GetUnavailableServerActionText()
                            : !IsMinecraftLinked
                                ? "绑定正版身份"
                                : GetLaunchActionText();
        OnPropertyChanged(nameof(PrimaryActionGlyph));
        OnPropertyChanged(nameof(PrimaryActionToolTip));
        PrimaryActionCommand.RaiseCanExecuteChanged();
    }

    private static string GetJoinAccessDeniedMessage(ServerSummary server) =>
        $"当前账号暂未获得“{server.Name}”的进入权限（最低称号：{GetAccessTierText(server.MinimumTier)}），你仍可提前准备客户端。";

    private string GetUnavailableServerActionText() =>
        SelectedServer?.Status == ServerStatus.Maintenance
            ? "维护中"
            : "暂未开放";

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
        ServerCatalogPresentation.IsPlayerServer(server);

    private static bool IsActivityServer(ServerSummary server) =>
        ServerCatalogPresentation.IsActivityServer(server);

    private static bool AreClientProfilesEquivalent(
        ClientProfileSummary? left,
        ClientProfileSummary? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return string.Equals(left.Id, right.Id, StringComparison.Ordinal) &&
               string.Equals(left.Version, right.Version, StringComparison.Ordinal) &&
               string.Equals(left.Sha256, right.Sha256, StringComparison.OrdinalIgnoreCase) &&
               left.DownloadBytes == right.DownloadBytes;
    }

    private void StartRegistrationCodeCooldown()
    {
        Interlocked.Exchange(
            ref _registrationCodeCooldownCancellation,
            null)?.Cancel();

        var cancellation = new CancellationTokenSource();
        _registrationCodeCooldownCancellation = cancellation;
        SetRegistrationCodeCooldownActive(true);
        _ = EndRegistrationCodeCooldownAsync(cancellation);
    }

    private async Task EndRegistrationCodeCooldownAsync(
        CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(RegistrationCodeCooldown, cancellation.Token);
            DispatchToUi(() =>
            {
                if (ReferenceEquals(_registrationCodeCooldownCancellation, cancellation))
                {
                    _registrationCodeCooldownCancellation = null;
                    SetRegistrationCodeCooldownActive(false);
                }
            });
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // A newer successful request owns the cooldown.
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    private void SetRegistrationCodeCooldownActive(bool value)
    {
        if (_isRegistrationCodeCooldownActive == value)
        {
            return;
        }

        _isRegistrationCodeCooldownActive = value;
        OnPropertyChanged(nameof(CanSendRegistrationCode));
        OnPropertyChanged(nameof(RegistrationCodeActionText));
    }

    private void SetAccountFormStatus(string message, bool isError)
    {
        AccountFormMessage = message;
        IsAccountFormError = isError;
        _accountFormAnnouncementRevision++;
        OnPropertyChanged(nameof(AccountFormAnnouncementRevision));
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

    private void SetNavigationPage(
        bool isSelected,
        LauncherPage page,
        string propertyName)
    {
        if (isSelected)
        {
            ActivePage = page;
            return;
        }

        if (ActivePage == page)
        {
            OnPropertyChanged(propertyName);
        }
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

    private ProgressStepState GetProgressStepState(int stepIndex)
    {
        if (_clientInstallPhase is null)
        {
            return ProgressStepState.Pending;
        }

        var currentStep = _clientInstallPhase switch
        {
            ClientInstallPhase.Checking => 0,
            ClientInstallPhase.Downloading => 1,
            ClientInstallPhase.Staging or ClientInstallPhase.Switching => 2,
            ClientInstallPhase.PreparingRuntime => 3,
            ClientInstallPhase.Complete => 4,
            _ => 0
        };
        if (currentStep == 4 || stepIndex < currentStep)
        {
            return ProgressStepState.Complete;
        }

        if (stepIndex > currentStep)
        {
            return ProgressStepState.Pending;
        }

        return _installStepFailed
            ? ProgressStepState.Failed
            : ProgressStepState.Current;
    }

    private void NotifyProgressStepStatesChanged()
    {
        OnPropertyChanged(nameof(ProgressStepOneState));
        OnPropertyChanged(nameof(ProgressStepTwoState));
        OnPropertyChanged(nameof(ProgressStepThreeState));
        OnPropertyChanged(nameof(ProgressStepFourState));
        OnPropertyChanged(nameof(ProgressStepOneStatusText));
        OnPropertyChanged(nameof(ProgressStepTwoStatusText));
        OnPropertyChanged(nameof(ProgressStepThreeStatusText));
        OnPropertyChanged(nameof(ProgressStepFourStatusText));
    }

    private string GetProgressStepStatusText(int stepIndex) =>
        GetProgressStepState(stepIndex) switch
        {
            ProgressStepState.Current => "进行中",
            ProgressStepState.Complete => "已完成",
            ProgressStepState.Failed => "失败",
            _ => "等待"
        };

    private async void ShowToast(
        string message,
        ToastSeverity severity = ToastLevel.Info)
    {
        var generation = Interlocked.Increment(ref _toastGeneration);
        ToastMessage = message;
        ToastSeverity = severity;
        IsToastVisible = true;
        Interlocked.Increment(ref _toastAnnouncementRevision);
        OnPropertyChanged(nameof(ToastAnnouncementRevision));
        await Task.Delay(4000);
        if (generation == Volatile.Read(ref _toastGeneration))
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
                StringComparer.Ordinal),
            UseSystemProxy,
            UseDarkMode);
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
        ShowToast(
            $"当前客户端已恢复自动 Java {GetSelectedProfileJavaMajorVersion()}",
            ToastLevel.Success);
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
        OnPropertyChanged(nameof(CanDeleteSelectedProfile));
        OnPropertyChanged(nameof(DeleteProfileToolTip));
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

using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Media.Imaging;
using Hechao.Contracts;
using Hechao.Distribution;
using Hechao.Launcher.Infrastructure;
using Hechao.Launcher.Services;
using Hechao.Launcher.ViewModels;

namespace Hechao.Launcher.Mac.ViewModels;

public enum MacLauncherSection
{
    Home,
    Downloads,
    Activities,
    Account,
    Settings
}

public sealed class LauncherMacViewModel : ObservableObject
{
    private readonly ILauncherAuthenticationService _authentication;
    private readonly IServerCatalogClient _catalog;
    private readonly ILauncherSettingsStore _settingsStore;
    private readonly IClientInstallationService _installation;
    private readonly IMinecraftGameLauncherService _gameLauncher;
    private readonly IDownloadHistoryStore _downloadHistory;
    private readonly IGameDiagnosticsService _diagnostics;
    private readonly IPlayerGameSettingsService _playerSettings;
    private readonly IMinecraftSkinService _skinService;
    private readonly Dictionary<string, ClientProfileSummary> _profiles =
        new(StringComparer.Ordinal);
    private LauncherSettings _settings;
    private CancellationTokenSource? _operationCancellation;
    private MacLauncherSection _activeSection = MacLauncherSection.Home;
    private MacServerItemViewModel? _selectedServer;
    private LocalProfileState _clientState = LocalProfileState.Missing;
    private HechaoAccount? _account;
    private bool _isBusy;
    private bool _isInitialized;
    private bool _isRegistering;
    private bool _isDeleteArmed;
    private bool _hasError;
    private string _statusMessage = "正在准备赫朝启动器…";
    private string _busyText = string.Empty;
    private double _operationProgress;
    private string _loginIdentity = string.Empty;
    private string _loginPassword = string.Empty;
    private string _registerUsername = string.Empty;
    private string _registerDisplayName = string.Empty;
    private string _registerEmail = string.Empty;
    private string _registerCode = string.Empty;
    private string _registerPassword = string.Empty;
    private string _selectedMemory;
    private string _clientDirectory;
    private bool _useSystemProxy;
    private Bitmap? _accountSkin;

    public LauncherMacViewModel(
        ILauncherAuthenticationService authentication,
        IServerCatalogClient catalog,
        ILauncherSettingsStore settingsStore,
        IClientInstallationService installation,
        IMinecraftGameLauncherService gameLauncher,
        IDownloadHistoryStore downloadHistory,
        IGameDiagnosticsService diagnostics,
        IPlayerGameSettingsService playerSettings,
        IMinecraftSkinService skinService)
    {
        _authentication = authentication;
        _catalog = catalog;
        _settingsStore = settingsStore;
        _installation = installation;
        _gameLauncher = gameLauncher;
        _downloadHistory = downloadHistory;
        _diagnostics = diagnostics;
        _playerSettings = playerSettings;
        _skinService = skinService;
        _settings = settingsStore.Load();
        _selectedMemory = MemoryOptions.Contains(_settings.Memory)
            ? _settings.Memory
            : "6 GB";
        _clientDirectory = string.IsNullOrWhiteSpace(_settings.ClientDirectory)
            ? JsonLauncherSettingsStore.DefaultClientDataDirectory
            : _settings.ClientDirectory;
        _useSystemProxy = _settings.UseSystemProxy;

        NavigateCommand = new RelayCommand<string>(Navigate);
        SelectServerCommand = new AsyncRelayCommand<MacServerItemViewModel>(
            SelectServerAsync,
            HandleCommandException,
            _ => !IsBusy);
        RefreshCatalogCommand = new AsyncRelayCommand(
            RefreshCatalogAsync,
            HandleCommandException,
            () => IsAuthenticated && !IsBusy);
        LoginCommand = new AsyncRelayCommand(
            LoginAsync,
            HandleCommandException,
            () => !IsBusy);
        SendRegistrationCodeCommand = new AsyncRelayCommand(
            SendRegistrationCodeAsync,
            HandleCommandException,
            () => !IsBusy);
        RegisterCommand = new AsyncRelayCommand(
            RegisterAsync,
            HandleCommandException,
            () => !IsBusy);
        ToggleRegistrationCommand = new RelayCommand(
            () => IsRegistering = !IsRegistering,
            () => !IsBusy);
        LinkMinecraftCommand = new AsyncRelayCommand(
            LinkMinecraftAsync,
            HandleCommandException,
            () => IsAuthenticated && !IsBusy);
        LogoutCommand = new AsyncRelayCommand(
            LogoutAsync,
            HandleCommandException,
            () => IsAuthenticated && !IsBusy);
        PrimaryActionCommand = new AsyncRelayCommand(
            RunPrimaryActionAsync,
            HandleCommandException,
            CanRunPrimaryAction);
        StopGameCommand = new AsyncRelayCommand(
            StopGameAsync,
            HandleCommandException,
            () => IsGameRunning && !IsBusy);
        RepairCommand = new AsyncRelayCommand(
            RepairAsync,
            HandleCommandException,
            () => SelectedProfile is not null && !IsBusy && !IsGameRunning);
        DeleteProfileCommand = new AsyncRelayCommand(
            DeleteProfileAsync,
            HandleCommandException,
            () => SelectedProfile is not null && !IsBusy && !IsGameRunning);
        CancelOperationCommand = new RelayCommand(
            () => _operationCancellation?.Cancel(),
            () => IsBusy && _operationCancellation is not null);
        OpenGameDirectoryCommand = new RelayCommand(OpenGameDirectory);
        OpenForumCommand = new RelayCommand(
            () => OpenExternal("https://hechao.world/"));
        CreateDiagnosticCommand = new AsyncRelayCommand(
            CreateDiagnosticAsync,
            HandleCommandException,
            () => SelectedProfile is not null && !IsBusy);

        foreach (var record in _downloadHistory.Load())
        {
            Downloads.Add(new DownloadJobViewModel(
                record.Id,
                record.ProfileId,
                record.DisplayName,
                record.Version,
                record.StartedAt,
                record.Status,
                record.CompletedBytes,
                record.TotalBytes,
                record.CurrentFile,
                record.CompletedAt,
                record.FailureMessage));
        }

        _gameLauncher.ProcessExited += GameLauncherOnProcessExited;
    }

    public ObservableCollection<MacServerItemViewModel> Servers { get; } = [];
    public ObservableCollection<MacServerItemViewModel> Activities { get; } = [];
    public ObservableCollection<DownloadJobViewModel> Downloads { get; } = [];
    public IReadOnlyList<string> MemoryOptions { get; } =
        ["4 GB", "6 GB", "8 GB", "10 GB", "12 GB", "16 GB"];
    public bool HasDownloads => Downloads.Count > 0;
    public bool HasNoDownloads => Downloads.Count == 0;
    public string DownloadCountText => $"{Downloads.Count} 条任务记录";

    public RelayCommand<string> NavigateCommand { get; }
    public AsyncRelayCommand<MacServerItemViewModel> SelectServerCommand { get; }
    public AsyncRelayCommand RefreshCatalogCommand { get; }
    public AsyncRelayCommand LoginCommand { get; }
    public AsyncRelayCommand SendRegistrationCodeCommand { get; }
    public AsyncRelayCommand RegisterCommand { get; }
    public RelayCommand ToggleRegistrationCommand { get; }
    public AsyncRelayCommand LinkMinecraftCommand { get; }
    public AsyncRelayCommand LogoutCommand { get; }
    public AsyncRelayCommand PrimaryActionCommand { get; }
    public AsyncRelayCommand StopGameCommand { get; }
    public AsyncRelayCommand RepairCommand { get; }
    public AsyncRelayCommand DeleteProfileCommand { get; }
    public RelayCommand CancelOperationCommand { get; }
    public RelayCommand OpenGameDirectoryCommand { get; }
    public RelayCommand OpenForumCommand { get; }
    public AsyncRelayCommand CreateDiagnosticCommand { get; }

    public MacLauncherSection ActiveSection
    {
        get => _activeSection;
        private set
        {
            if (SetProperty(ref _activeSection, value))
            {
                OnPropertyChanged(nameof(IsHome));
                OnPropertyChanged(nameof(IsDownloads));
                OnPropertyChanged(nameof(IsActivities));
                OnPropertyChanged(nameof(IsAccount));
                OnPropertyChanged(nameof(IsSettings));
            }
        }
    }

    public bool IsHome => ActiveSection == MacLauncherSection.Home;
    public bool IsDownloads => ActiveSection == MacLauncherSection.Downloads;
    public bool IsActivities => ActiveSection == MacLauncherSection.Activities;
    public bool IsAccount => ActiveSection == MacLauncherSection.Account;
    public bool IsSettings => ActiveSection == MacLauncherSection.Settings;
    public string LauncherVersionText => "M4 · v0.15.8";

    public MacServerItemViewModel? SelectedServer
    {
        get => _selectedServer;
        private set
        {
            var previous = _selectedServer;
            if (SetProperty(ref _selectedServer, value))
            {
                if (previous is not null)
                {
                    previous.IsSelected = false;
                }
                if (value is not null)
                {
                    value.IsSelected = true;
                }
                _isDeleteArmed = false;
                OnPropertyChanged(nameof(HasSelectedServer));
                OnPropertyChanged(nameof(SelectedProfile));
                OnPropertyChanged(nameof(SelectedServerName));
                OnPropertyChanged(nameof(SelectedServerShortName));
                OnPropertyChanged(nameof(SelectedServerAnnouncement));
                OnPropertyChanged(nameof(SelectedServerStatusText));
                OnPropertyChanged(nameof(SelectedServerStatusColor));
                OnPropertyChanged(nameof(SelectedServerPopulationText));
                OnPropertyChanged(nameof(SelectedServerRuntimeText));
                OnPropertyChanged(nameof(SelectedServerScheduleText));
                OnPropertyChanged(nameof(SelectedProfileText));
                OnPropertyChanged(nameof(ManagedJavaDetailText));
                OnPropertyChanged(nameof(PrimaryActionText));
                OnPropertyChanged(nameof(DeleteActionText));
            }
        }
    }

    public bool HasSelectedServer => SelectedServer is not null;
    public ClientProfileSummary? SelectedProfile =>
        SelectedServer is not null &&
        _profiles.TryGetValue(SelectedServer.Server.ClientProfileId, out var profile)
            ? profile
            : null;
    public string SelectedServerName => SelectedServer?.Name ?? "选择一个服务器";
    public string SelectedServerShortName => SelectedServer?.ShortName ?? "赫";
    public string SelectedServerAnnouncement => SelectedServer?.Announcement ??
        "登录后从目录中选择服务器。";
    public string SelectedServerStatusText => SelectedServer?.StatusText ?? "未选择";
    public string SelectedServerStatusColor => SelectedServer?.StatusColor ?? "#979A94";
    public string SelectedServerPopulationText => SelectedServer?.PopulationText ?? "--/-- 人";
    public string SelectedServerRuntimeText => SelectedServer?.RuntimeText ?? "Minecraft 版本待同步";
    public string SelectedServerScheduleText => SelectedServer?.ScheduleText ?? "开放时间待同步";
    public string SelectedProfileText => SelectedProfile is { } profile
        ? $"{profile.DisplayName} · {profile.Version}"
        : "客户端档案待同步";

    public HechaoAccount? Account
    {
        get => _account;
        private set
        {
            if (SetProperty(ref _account, value))
            {
                OnPropertyChanged(nameof(IsAuthenticated));
                OnPropertyChanged(nameof(IsSignedOut));
                OnPropertyChanged(nameof(IsMinecraftLinked));
                OnPropertyChanged(nameof(NeedsMinecraftLink));
                OnPropertyChanged(nameof(AccountDisplayName));
                OnPropertyChanged(nameof(AccountDetailText));
                RaiseCommandStates();
            }
        }
    }

    public bool IsAuthenticated => Account is not null;
    public bool IsSignedOut => Account is null;
    public bool IsMinecraftLinked => Account?.IsMinecraftLinked == true;
    public bool NeedsMinecraftLink => Account is not null && !Account.IsMinecraftLinked;
    public string AccountDisplayName => Account?.DisplayName ?? "未登录";
    public string AccountDetailText => Account is null
        ? "登录后同步服务器权限与整合包"
        : Account.IsMinecraftLinked
            ? $"{Account.MinecraftName} · {Account.AccessTier}"
            : $"@{Account.Username} · 尚未绑定 Minecraft";
    public Bitmap? AccountSkin
    {
        get => _accountSkin;
        private set
        {
            if (SetProperty(ref _accountSkin, value))
            {
                OnPropertyChanged(nameof(HasAccountSkin));
            }
        }
    }
    public bool HasAccountSkin => AccountSkin is not null;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RaiseCommandStates();
                OnPropertyChanged(nameof(CanCancelOperation));
            }
        }
    }

    public bool IsInitialized
    {
        get => _isInitialized;
        private set => SetProperty(ref _isInitialized, value);
    }

    public bool IsRegistering
    {
        get => _isRegistering;
        set
        {
            if (SetProperty(ref _isRegistering, value))
            {
                OnPropertyChanged(nameof(IsLoginMode));
            }
        }
    }
    public bool IsLoginMode => !IsRegistering;

    public bool HasError
    {
        get => _hasError;
        private set => SetProperty(ref _hasError, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string BusyText
    {
        get => _busyText;
        private set => SetProperty(ref _busyText, value);
    }

    public double OperationProgress
    {
        get => _operationProgress;
        private set => SetProperty(ref _operationProgress, Math.Clamp(value, 0, 100));
    }

    public bool CanCancelOperation => IsBusy && _operationCancellation is not null;
    public bool IsGameRunning => _gameLauncher.GetRunningGame() is not null;
    public string RunningGameText => _gameLauncher.GetRunningGame() is { } running
        ? $"Minecraft 正在运行 · PID {running.ProcessId}"
        : "Minecraft 未运行";
    public string ClientStateText => _clientState switch
    {
        LocalProfileState.Ready => "客户端和 ARM64 Java 已就绪",
        LocalProfileState.UpdateRequired => "客户端需要更新或补齐 Java",
        _ => "尚未安装此客户端"
    };
    public string ManagedJavaStateText => _clientState switch
    {
        LocalProfileState.Ready => "ARM64 Java 已校验",
        LocalProfileState.UpdateRequired => "ARM64 Java 需要补齐",
        _ => "ARM64 Java 将自动准备"
    };
    public string ManagedJavaDetailText => _clientState switch
    {
        LocalProfileState.Ready => $"{GetExpectedJavaText()} · 检测通过",
        LocalProfileState.UpdateRequired =>
            $"{GetExpectedJavaText()} · 缺失或版本不匹配，点击修复",
        _ => $"{GetExpectedJavaText()} · 安装时下载并校验"
    };
    public string PrimaryActionText => IsGameRunning
        ? "游戏运行中"
        : _clientState switch
        {
            LocalProfileState.Ready => SelectedServer?.Server.CanJoin == false
                ? "暂不可进服"
                : "启动游戏",
            LocalProfileState.UpdateRequired => "更新客户端",
            _ => "安装客户端"
        };
    public string DeleteActionText => _isDeleteArmed
        ? "再次点击确认删除"
        : "删除客户端";

    public string LoginIdentity
    {
        get => _loginIdentity;
        set => SetProperty(ref _loginIdentity, value);
    }

    public string LoginPassword
    {
        get => _loginPassword;
        set => SetProperty(ref _loginPassword, value);
    }

    public string RegisterUsername
    {
        get => _registerUsername;
        set => SetProperty(ref _registerUsername, value);
    }

    public string RegisterDisplayName
    {
        get => _registerDisplayName;
        set => SetProperty(ref _registerDisplayName, value);
    }

    public string RegisterEmail
    {
        get => _registerEmail;
        set => SetProperty(ref _registerEmail, value);
    }

    public string RegisterCode
    {
        get => _registerCode;
        set => SetProperty(ref _registerCode, value);
    }

    public string RegisterPassword
    {
        get => _registerPassword;
        set => SetProperty(ref _registerPassword, value);
    }

    public string SelectedMemory
    {
        get => _selectedMemory;
        set
        {
            if (SetProperty(ref _selectedMemory, value))
            {
                SaveSettings();
            }
        }
    }

    public string ClientDirectory
    {
        get => _clientDirectory;
        private set
        {
            if (SetProperty(ref _clientDirectory, value))
            {
                SaveSettings();
                _ = RefreshSelectedClientStateAsync();
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
                SetStatus("代理设置将在下次启动赫朝启动器时生效。", false);
            }
        }
    }

    public void SetClientDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || IsBusy || IsGameRunning)
        {
            return;
        }

        ClientDirectory = Path.GetFullPath(path);
    }

    public async Task InitializeAsync()
    {
        if (IsInitialized)
        {
            return;
        }

        await RunBusyAsync("正在恢复赫朝会话", async cancellationToken =>
        {
            Account = await _authentication.TryRestoreAsync(cancellationToken);
            if (Account is not null)
            {
                await RefreshCatalogCoreAsync(cancellationToken);
                await RefreshSkinAsync(cancellationToken);
                SetStatus("账号与服务器目录已同步。", false);
            }
            else
            {
                ActiveSection = MacLauncherSection.Account;
                SetStatus("请先登录赫朝账号。", false);
            }
        });
        IsInitialized = true;
    }

    private void Navigate(string? value)
    {
        if (Enum.TryParse<MacLauncherSection>(value, ignoreCase: true, out var section))
        {
            ActiveSection = section;
        }
    }

    private async Task SelectServerAsync(MacServerItemViewModel? server)
    {
        if (server is null)
        {
            return;
        }

        SelectedServer = server;
        ActiveSection = server.IsActivity
            ? MacLauncherSection.Activities
            : MacLauncherSection.Home;
        await RefreshSelectedClientStateAsync();
    }

    private Task RefreshCatalogAsync() =>
        RunBusyAsync("正在同步服务器目录", RefreshCatalogCoreAsync);

    private async Task RefreshCatalogCoreAsync(CancellationToken cancellationToken)
    {
        var result = await _catalog.GetCatalogResultAsync(cancellationToken);
        _profiles.Clear();
        foreach (var profile in result.Snapshot.ClientProfiles)
        {
            _profiles[profile.Id] = profile;
        }

        var items = result.Snapshot.Servers
            .Where(server => !string.Equals(
                server.Id,
                "lobby",
                StringComparison.OrdinalIgnoreCase))
            .Select(server => new MacServerItemViewModel(server))
            .ToArray();
        Servers.Clear();
        Activities.Clear();
        foreach (var item in items)
        {
            if (item.IsActivity)
            {
                Activities.Add(item);
            }
            else
            {
                Servers.Add(item);
            }
        }

        var selectedId = SelectedServer?.Id ?? _settings.SelectedServerId;
        SelectedServer = items.FirstOrDefault(item =>
                             string.Equals(item.Id, selectedId, StringComparison.Ordinal)) ??
                         items.FirstOrDefault(item => !item.IsActivity) ??
                         items.FirstOrDefault();
        if (SelectedServer is not null)
        {
            _settings = _settings with { SelectedServerId = SelectedServer.Id };
            _settingsStore.Save(_settings);
        }

        await RefreshSelectedClientStateAsync(cancellationToken);
        SetStatus(result.Source == CatalogSource.Live
            ? "服务器目录已更新。"
            : "网络目录不可用，当前显示最近一次可用目录。", false);
    }

    private async Task LoginAsync()
    {
        if (string.IsNullOrWhiteSpace(LoginIdentity) ||
            string.IsNullOrWhiteSpace(LoginPassword))
        {
            SetStatus("请输入赫朝账号和密码。", true);
            return;
        }

        await RunBusyAsync("正在登录赫朝账号", async cancellationToken =>
        {
            Account = await _authentication.LoginAsync(
                LoginIdentity.Trim(),
                LoginPassword,
                cancellationToken);
            LoginPassword = string.Empty;
            await RefreshCatalogCoreAsync(cancellationToken);
            await RefreshSkinAsync(cancellationToken);
            ActiveSection = MacLauncherSection.Home;
            SetStatus("已登录赫朝账号。", false);
        });
    }

    private async Task SendRegistrationCodeAsync()
    {
        if (string.IsNullOrWhiteSpace(RegisterEmail))
        {
            SetStatus("请输入用于注册的邮箱。", true);
            return;
        }

        await RunBusyAsync("正在发送验证码", async cancellationToken =>
        {
            await _authentication.SendRegistrationCodeAsync(
                RegisterEmail.Trim(),
                cancellationToken);
            SetStatus("验证码已发送，请检查邮箱。", false);
        });
    }

    private async Task RegisterAsync()
    {
        if (new[]
            {
                RegisterUsername,
                RegisterDisplayName,
                RegisterEmail,
                RegisterCode,
                RegisterPassword
            }.Any(string.IsNullOrWhiteSpace))
        {
            SetStatus("请填写完整注册信息。", true);
            return;
        }

        await RunBusyAsync("正在创建赫朝账号", async cancellationToken =>
        {
            Account = await _authentication.RegisterAsync(
                RegisterUsername.Trim(),
                RegisterDisplayName.Trim(),
                RegisterPassword,
                RegisterEmail.Trim(),
                RegisterCode.Trim(),
                cancellationToken);
            RegisterPassword = string.Empty;
            await RefreshCatalogCoreAsync(cancellationToken);
            ActiveSection = MacLauncherSection.Home;
            SetStatus("赫朝账号已创建并登录。", false);
        });
    }

    private async Task LinkMinecraftAsync()
    {
        await RunBusyAsync("正在打开 Microsoft 正版验证", async cancellationToken =>
        {
            Account = await _authentication.LinkMinecraftAsync(cancellationToken);
            await RefreshCatalogCoreAsync(cancellationToken);
            await RefreshSkinAsync(cancellationToken);
            SetStatus($"已绑定 Minecraft 账号 {Account.MinecraftName}。", false);
        });
    }

    private async Task LogoutAsync()
    {
        await RunBusyAsync("正在退出账号", async cancellationToken =>
        {
            await _authentication.LogoutAsync(cancellationToken);
            Account = null;
            AccountSkin?.Dispose();
            AccountSkin = null;
            Servers.Clear();
            Activities.Clear();
            SelectedServer = null;
            ActiveSection = MacLauncherSection.Account;
            SetStatus("已安全退出赫朝账号。", false);
        });
    }

    private async Task RunPrimaryActionAsync()
    {
        var server = SelectedServer ??
            throw new InvalidOperationException("请先选择服务器。");
        var profile = SelectedProfile ??
            throw new InvalidOperationException("服务器没有可用的客户端档案。");
        if (_clientState != LocalProfileState.Ready)
        {
            await InstallSelectedProfileAsync(profile);
            return;
        }

        if (!server.Server.CanJoin)
        {
            SetStatus("客户端可以提前准备，但当前账号暂未获得此活动的进服权限。", true);
            return;
        }

        await RunBusyAsync("正在准备 Minecraft", async cancellationToken =>
        {
            var session = await GetLaunchSessionWithInteractiveFallbackAsync(
                cancellationToken);
            await _playerSettings.ImportLatestAsync(ClientDirectory, cancellationToken);
            await _playerSettings.ApplyToProfileAsync(
                ClientDirectory,
                profile.Id,
                cancellationToken);
            var progress = new Progress<MinecraftLaunchProgress>(value =>
            {
                OperationProgress = value.Percent;
                BusyText = value.Phase switch
                {
                    MinecraftLaunchPhase.LoadingProfile => "正在读取客户端档案",
                    MinecraftLaunchPhase.PreparingRuntime => "正在补齐 macOS ARM64 运行依赖",
                    MinecraftLaunchPhase.BuildingProcess => "正在生成启动参数",
                    MinecraftLaunchPhase.Authorizing => "正在取得进服授权",
                    _ => "正在启动 Minecraft"
                };
            });
            var result = await _gameLauncher.LaunchAsync(
                new MinecraftLaunchRequest(
                    ClientDirectory,
                    profile.Id,
                    ParseMemoryMb(SelectedMemory),
                    session,
                    GetCustomJavaPath(profile.Id),
                    server.Id),
                progress,
                token => AuthorizeLaunchAsync(server.Id, token),
                cancellationToken);
            SetStatus($"Minecraft 已启动，进程 PID {result.ProcessId}。", false);
            OnGameStateChanged();
        });
    }

    private async Task<MinecraftLaunchSession> GetLaunchSessionWithInteractiveFallbackAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            return await _authentication.GetMinecraftLaunchSessionAsync(cancellationToken);
        }
        catch (MicrosoftReauthenticationRequiredException)
        {
            return await _authentication.RefreshMinecraftLaunchSessionAsync(cancellationToken);
        }
    }

    private async Task AuthorizeLaunchAsync(
        string serverId,
        CancellationToken cancellationToken)
    {
        _ = await _authentication.PrepareVelocityLaunchAsync(
            serverId,
            cancellationToken);
    }

    private async Task InstallSelectedProfileAsync(ClientProfileSummary profile)
    {
        await RunBusyAsync("正在准备客户端", async cancellationToken =>
        {
            var job = new DownloadJobViewModel(
                Guid.NewGuid(),
                profile.Id,
                profile.DisplayName,
                profile.Version,
                DateTimeOffset.UtcNow,
                DownloadJobStatus.Running,
                0,
                Math.Max(0, profile.DownloadBytes),
                string.Empty);
            Downloads.Insert(0, job);
            OnPropertyChanged(nameof(HasDownloads));
            OnPropertyChanged(nameof(HasNoDownloads));
            OnPropertyChanged(nameof(DownloadCountText));
            ActiveSection = MacLauncherSection.Downloads;
            var progress = new Progress<ClientInstallProgress>(value =>
            {
                job.Update(
                    value.Percent,
                    value.CompletedBytes,
                    value.TotalBytes,
                    value.CurrentPath);
                OperationProgress = value.Percent;
                BusyText = value.Phase switch
                {
                    ClientInstallPhase.Checking => "正在校验签名清单",
                    ClientInstallPhase.Downloading => "正在下载整合包",
                    ClientInstallPhase.Staging => "正在构建独立客户端档案",
                    ClientInstallPhase.Switching => "正在原子切换客户端版本",
                    ClientInstallPhase.PreparingRuntime => "正在准备 ARM64 Java",
                    _ => "客户端已准备完成"
                };
            });

            try
            {
                await _installation.InstallAsync(
                    profile,
                    new ClientInstallationOptions(
                        ClientDirectory,
                        _settings.KeepDownloadsAfterClose),
                    progress,
                    cancellationToken);
                job.Finish(DownloadJobStatus.Completed);
                _clientState = LocalProfileState.Ready;
                SetStatus($"{profile.DisplayName} 已安装完成。", false);
            }
            catch (OperationCanceledException)
            {
                job.Finish(DownloadJobStatus.Canceled);
                throw;
            }
            catch (Exception exception)
            {
                job.Finish(DownloadJobStatus.Failed, ToUserMessage(exception));
                throw;
            }
            finally
            {
                SaveDownloadHistory();
                OnClientStateChanged();
            }
        });
    }

    private async Task RepairAsync()
    {
        var profile = SelectedProfile ??
            throw new InvalidOperationException("请先选择客户端档案。");
        await InstallSelectedProfileAsync(profile);
    }

    private async Task DeleteProfileAsync()
    {
        var profile = SelectedProfile ??
            throw new InvalidOperationException("请先选择客户端档案。");
        if (!_isDeleteArmed)
        {
            _isDeleteArmed = true;
            OnPropertyChanged(nameof(DeleteActionText));
            SetStatus("再次点击“确认删除”才会移除此档案；共享下载对象不会删除。", true);
            return;
        }

        _isDeleteArmed = false;
        await RunBusyAsync("正在删除客户端档案", async cancellationToken =>
        {
            var deleted = await _installation.DeleteAsync(
                profile,
                ClientDirectory,
                cancellationToken);
            await RefreshSelectedClientStateAsync(cancellationToken);
            SetStatus(deleted ? "客户端档案已删除。" : "本地没有可删除的客户端档案。", false);
        });
        OnPropertyChanged(nameof(DeleteActionText));
    }

    private async Task StopGameAsync()
    {
        await RunBusyAsync("正在安全退出 Minecraft", async cancellationToken =>
        {
            var progress = new Progress<MinecraftStopProgress>(value =>
            {
                BusyText = value.Phase switch
                {
                    MinecraftStopPhase.RequestingExit => "正在请求游戏保存并退出",
                    MinecraftStopPhase.WaitingForExit => "正在等待 Minecraft 退出",
                    MinecraftStopPhase.ForcingExit => "正常退出超时，正在结束进程",
                    _ => "Minecraft 已退出"
                };
            });
            var result = await _gameLauncher.StopRunningGameAsync(
                TimeSpan.FromSeconds(20),
                progress,
                cancellationToken);
            SetStatus(result.Outcome == MinecraftStopOutcome.NotRunning
                ? "Minecraft 当前没有运行。"
                : "Minecraft 已退出。", false);
            OnGameStateChanged();
        });
    }

    private async Task CreateDiagnosticAsync()
    {
        var profile = SelectedProfile ??
            throw new InvalidOperationException("请先选择客户端档案。");
        await RunBusyAsync("正在生成脱敏诊断包", async cancellationToken =>
        {
            var result = await _diagnostics.CreateBundleAsync(
                new GameDiagnosticBundleRequest(
                    ClientDirectory,
                    profile.Id,
                    _diagnostics.LoadLatestExit(),
                    Array.Empty<string>()),
                cancellationToken);
            SetStatus($"诊断包已保存：{result.BundlePath}", false);
            OpenPath(Path.GetDirectoryName(result.BundlePath)!);
        });
    }

    private async Task RefreshSelectedClientStateAsync(
        CancellationToken cancellationToken = default)
    {
        var profile = SelectedProfile;
        _clientState = profile is null
            ? LocalProfileState.Missing
            : await _installation.GetLocalStateAsync(
                profile,
                ClientDirectory,
                cancellationToken);
        OnClientStateChanged();
    }

    private async Task RefreshSkinAsync(CancellationToken cancellationToken)
    {
        if (Account?.MinecraftUuid is not { } uuid)
        {
            AccountSkin?.Dispose();
            AccountSkin = null;
            return;
        }

        var image = await _skinService.GetSkinAsync(uuid, cancellationToken);
        if (image is null)
        {
            return;
        }

        await using var stream = new MemoryStream(image.PngBytes, writable: false);
        var bitmap = new Bitmap(stream);
        AccountSkin?.Dispose();
        AccountSkin = bitmap;
    }

    private async Task RunBusyAsync(
        string message,
        Func<CancellationToken, Task> operation)
    {
        if (IsBusy)
        {
            return;
        }

        using var cancellation = new CancellationTokenSource();
        _operationCancellation = cancellation;
        IsBusy = true;
        BusyText = message;
        OperationProgress = 0;
        HasError = false;
        try
        {
            await operation(cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            SetStatus("操作已取消，可稍后继续。", false);
        }
        finally
        {
            _operationCancellation = null;
            IsBusy = false;
            BusyText = string.Empty;
            OperationProgress = 0;
        }
    }

    private void HandleCommandException(Exception exception)
    {
        SetStatus(ToUserMessage(exception), true);
    }

    private static string ToUserMessage(Exception exception) => exception switch
    {
        LauncherApiException api when !string.IsNullOrWhiteSpace(api.Message) => api.Message,
        LauncherAuthenticationRequiredException => "赫朝会话已过期，请重新登录。",
        MicrosoftAuthenticationNotConfiguredException => "Microsoft 正版验证尚未配置。",
        MicrosoftSignInCanceledException => "已取消 Microsoft 正版验证。",
        MinecraftIdentityLinkRequiredException => "请先在账户页绑定 Minecraft Java 正版账号。",
        MinecraftAlreadyRunningException => "已有 Minecraft 正在运行，请先退出游戏。",
        MinecraftLaunchException launch => $"Minecraft 启动失败：{launch.Message}",
        HttpRequestException => "网络连接失败，请检查网络后重试。",
        IOException io when !string.IsNullOrWhiteSpace(io.Message) => io.Message,
        _ when !string.IsNullOrWhiteSpace(exception.Message) => exception.Message,
        _ => "操作未完成，请重试或生成诊断包。"
    };

    private void SetStatus(string message, bool isError)
    {
        StatusMessage = message;
        HasError = isError;
    }

    private void SaveSettings()
    {
        _settings = _settings with
        {
            Memory = SelectedMemory,
            ClientDirectory = ClientDirectory,
            UseSystemProxy = UseSystemProxy
        };
        _settingsStore.Save(_settings);
    }

    private void SaveDownloadHistory()
    {
        _downloadHistory.Save(Downloads.Select(job => new DownloadHistoryRecord(
            job.Id,
            job.ProfileId,
            job.DisplayName,
            job.Version,
            job.StartedAt,
            job.CompletedAt,
            job.Status,
            job.CompletedBytes,
            job.TotalBytes,
            job.CurrentFile,
            job.FailureMessage)));
    }

    private string? GetCustomJavaPath(string profileId) =>
        _settings.ProfileJavaPaths is not null &&
        _settings.ProfileJavaPaths.TryGetValue(profileId, out var path) &&
        !string.IsNullOrWhiteSpace(path)
            ? path
            : null;

    private bool CanRunPrimaryAction() =>
        SelectedProfile is not null &&
        IsAuthenticated &&
        IsMinecraftLinked &&
        !IsBusy &&
        !IsGameRunning;

    private static int ParseMemoryMb(string value)
    {
        var number = value.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        return int.TryParse(number, out var gigabytes)
            ? checked(gigabytes * 1024)
            : 6144;
    }

    private string GetExpectedJavaText()
    {
        var value = SelectedServer?.Server.MinecraftVersion;
        if (!Version.TryParse(value, out var minecraftVersion))
        {
            return "目标版本由签名档案确定";
        }

        var javaMajorVersion = minecraftVersion >= new Version(1, 20, 5)
            ? 21
            : minecraftVersion >= new Version(1, 18)
                ? 17
                : minecraftVersion >= new Version(1, 17)
                    ? 16
                    : 8;
        return $"目标 Java {javaMajorVersion}（由签名档案复核）";
    }

    private void RaiseCommandStates()
    {
        SelectServerCommand.RaiseCanExecuteChanged();
        RefreshCatalogCommand.RaiseCanExecuteChanged();
        LoginCommand.RaiseCanExecuteChanged();
        SendRegistrationCodeCommand.RaiseCanExecuteChanged();
        RegisterCommand.RaiseCanExecuteChanged();
        ToggleRegistrationCommand.RaiseCanExecuteChanged();
        LinkMinecraftCommand.RaiseCanExecuteChanged();
        LogoutCommand.RaiseCanExecuteChanged();
        PrimaryActionCommand.RaiseCanExecuteChanged();
        StopGameCommand.RaiseCanExecuteChanged();
        RepairCommand.RaiseCanExecuteChanged();
        DeleteProfileCommand.RaiseCanExecuteChanged();
        CancelOperationCommand.RaiseCanExecuteChanged();
        CreateDiagnosticCommand.RaiseCanExecuteChanged();
    }

    private void OnClientStateChanged()
    {
        OnPropertyChanged(nameof(ClientStateText));
        OnPropertyChanged(nameof(ManagedJavaStateText));
        OnPropertyChanged(nameof(ManagedJavaDetailText));
        OnPropertyChanged(nameof(PrimaryActionText));
        RaiseCommandStates();
    }

    private void OnGameStateChanged()
    {
        OnPropertyChanged(nameof(IsGameRunning));
        OnPropertyChanged(nameof(RunningGameText));
        OnPropertyChanged(nameof(PrimaryActionText));
        RaiseCommandStates();
    }

    private async void GameLauncherOnProcessExited(
        object? sender,
        MinecraftProcessExitedEventArgs args)
    {
        try
        {
            await _diagnostics.RecordExitAsync(new GameExitRecord(
                Guid.NewGuid(),
                args.ProfileId,
                args.ProcessId,
                args.ExitCode,
                args.StartedAt,
                args.ExitedAt));
            if (!string.IsNullOrWhiteSpace(args.DataRoot))
            {
                await _playerSettings.CaptureProfileAsync(
                    args.DataRoot,
                    args.ProfileId);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            SetStatus("Minecraft 已退出，但启动器无法保存诊断或共享设置。", true);
        }
        finally
        {
            OnGameStateChanged();
        }
    }

    private void OpenGameDirectory()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        var layout = new ClientStorageLayout(ClientDirectory);
        var path = layout.GetProfileGameDirectory(SelectedProfile.Id);
        Directory.CreateDirectory(path);
        OpenPath(path);
    }

    private static void OpenExternal(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    private static void OpenPath(string path) =>
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
}

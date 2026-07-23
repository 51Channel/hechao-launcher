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
    private readonly Dictionary<string, ClientProfileSummary> _clientProfiles = new(StringComparer.Ordinal);
    private LauncherSettings _settings;
    private ServerSummary? _selectedServer;
    private LocalProfileState _selectedProfileState = LocalProfileState.Missing;
    private double _updateProgress;
    private string _clientStatusText = "正在检查客户端";
    private string _primaryActionText = "安装客户端";
    private bool _isProgressActive;
    private bool _isNotificationsOpen;
    private bool _isSettingsOpen;
    private bool _isToastVisible;
    private string _toastMessage = string.Empty;
    private string _selectedMemory;
    private readonly string _clientDirectory;
    private bool _checkForUpdates;
    private bool _keepDownloadsAfterClose;
    private bool _isCatalogLoading;
    private AuthenticatedPlayer? _currentPlayer;
    private bool _isAccountBusy;

    public MainWindowViewModel(
        IServerCatalogClient catalogClient,
        ILauncherAuthenticationService authenticationService,
        ILauncherSettingsStore settingsStore,
        IClientInstallationService installationService,
        IMinecraftGameLauncherService gameLauncherService)
    {
        _catalogClient = catalogClient;
        _authenticationService = authenticationService;
        _settingsStore = settingsStore;
        _installationService = installationService;
        _gameLauncherService = gameLauncherService;
        _settings = settingsStore.Load();

        MemoryOptions = ["2 GB", "4 GB", "6 GB", "8 GB", "12 GB", "16 GB"];
        _selectedMemory = MemoryOptions.Contains(_settings.Memory) ? _settings.Memory : "6 GB";
        _clientDirectory = string.IsNullOrWhiteSpace(_settings.ClientDirectory)
            ? "%AppData%\\Hechao\\instances"
            : _settings.ClientDirectory;
        _checkForUpdates = _settings.CheckForUpdates;
        _keepDownloadsAfterClose = _settings.KeepDownloadsAfterClose;

        SelectServerCommand = new RelayCommand<ServerSummary>(SelectServer);
        PrimaryActionCommand = new RelayCommand(StartPrimaryAction, CanUseSelectedServer);
        RepairCommand = new RelayCommand(StartRepair, () => !IsProgressActive);
        RefreshCommand = new RelayCommand(() => _ = LoadCatalogAsync(userInitiated: true), () => !_isCatalogLoading);
        OpenClientDirectoryCommand = new RelayCommand(OpenClientDirectory);
        ToggleNotificationsCommand = new RelayCommand(ToggleNotifications);
        ToggleSettingsCommand = new RelayCommand(ToggleSettings);
        CloseOverlaysCommand = new RelayCommand(CloseOverlays);
        AccountActionCommand = new RelayCommand(StartAccountAction, () => !IsAccountBusy);

        _ = InitializeAsync();
    }

    public ObservableCollection<ServerSummary> Servers { get; } = [];
    public IReadOnlyList<string> MemoryOptions { get; }
    public string LauncherVersionText { get; } = $"v{LauncherProductInfo.Version}";
    public RelayCommand<ServerSummary> SelectServerCommand { get; }
    public RelayCommand PrimaryActionCommand { get; }
    public RelayCommand RepairCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand OpenClientDirectoryCommand { get; }
    public RelayCommand ToggleNotificationsCommand { get; }
    public RelayCommand ToggleSettingsCommand { get; }
    public RelayCommand CloseOverlaysCommand { get; }
    public RelayCommand AccountActionCommand { get; }

    public bool IsAuthenticated => _currentPlayer is not null;
    public string AccountDisplayName => _currentPlayer?.MinecraftName ?? "访客";
    public string AccountStatusText => IsAccountBusy
        ? "正在验证 Microsoft 账号"
        : IsAuthenticated
            ? "Microsoft 正版账号"
            : "尚未登录";
    public string AccountAccessText => _currentPlayer is null
        ? "LuckPerms 待登录"
        : $"{GetAccessTierText(_currentPlayer.AccessTier)} · {_currentPlayer.LuckPermsPrimaryGroup}";
    public string AccountActionGlyph => IsAuthenticated ? "\uE8AC" : "\uE77B";
    public string AccountActionTooltip => IsAuthenticated ? "退出 Microsoft 账号" : "Microsoft 正版登录";

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

            OnPropertyChanged(nameof(SelectedServerStatusText));
            OnPropertyChanged(nameof(SelectedServerLoaderText));
            OnPropertyChanged(nameof(SelectedServerPlayerText));
            OnPropertyChanged(nameof(IsSelectedServerOnline));
            PrimaryActionCommand.RaiseCanExecuteChanged();
            if (value is not null)
            {
                SaveSettings();
                _ = RefreshClientStateAsync();
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
    public bool IsSelectedServerOnline => SelectedServer?.Status == ServerStatus.Online;

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

    public bool IsProgressActive
    {
        get => _isProgressActive;
        private set
        {
            if (SetProperty(ref _isProgressActive, value))
            {
                RepairCommand.RaiseCanExecuteChanged();
                PrimaryActionCommand.RaiseCanExecuteChanged();
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

    public bool CheckForUpdates
    {
        get => _checkForUpdates;
        set
        {
            if (SetProperty(ref _checkForUpdates, value))
            {
                SaveSettings();
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
            SetCurrentPlayer(await _authenticationService.TryRestoreAsync());
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or LauncherApiException)
        {
            SetCurrentPlayer(null);
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

            Servers.Clear();
            foreach (var server in snapshot.Servers)
            {
                Servers.Add(server);
            }

            SelectedServer = Servers.FirstOrDefault(server => server.Id == _settings.SelectedServerId) ?? Servers.FirstOrDefault();

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
            SelectedServer = null;
            ShowToast("请先使用 Microsoft 正版账号登录");
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
        CloseOverlays();
        SaveSettings();
    }

    private async void StartPrimaryAction()
    {
        if (IsProgressActive || SelectedServer is null)
        {
            return;
        }

        if (_selectedProfileState != LocalProfileState.Ready)
        {
            if (!await InstallSelectedProfileAsync(isRepair: false))
            {
                return;
            }
        }

        await LaunchSelectedServerAsync();
    }

    private async void StartRepair()
    {
        await InstallSelectedProfileAsync(isRepair: true);
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
        var progress = new Progress<ClientInstallProgress>(ApplyInstallProgress);
        var succeeded = false;

        try
        {
            await _installationService.InstallAsync(
                profile,
                new ClientInstallationOptions(ClientDirectory, KeepDownloadsAfterClose),
                progress);
            _selectedProfileState = LocalProfileState.Ready;
            UpdateProgress = 100;
            ClientStatusText = "客户端已就绪";
            PrimaryActionText = "进入服务器";
            ShowToast(isRepair ? "客户端修复完成" : "客户端安装完成");
            succeeded = true;
        }
        catch (LauncherAuthenticationRequiredException)
        {
            ClientStatusText = "需要登录后下载";
            ShowToast("请先使用 Microsoft 正版账号登录");
        }
        catch (LauncherApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            ClientStatusText = "客户端尚未发布";
            ShowToast("该客户端档案尚未发布下载清单");
        }
        catch (LauncherApiException exception)
        {
            ClientStatusText = "下载服务不可用";
            ShowToast(exception.ApiDetail ?? "客户端分发服务暂时不可用");
        }
        catch (ManifestSignatureException)
        {
            ClientStatusText = "清单签名无效";
            ShowToast("客户端清单未通过签名验证，安装已停止");
        }
        catch (Exception exception) when (exception is ManifestIntegrityException or ClientManifestMismatchException)
        {
            ClientStatusText = "文件校验失败";
            ShowToast("下载内容与发布清单不一致，安装已停止");
        }
        catch (InsufficientDiskSpaceException)
        {
            ClientStatusText = "磁盘空间不足";
            ShowToast("客户端目录所在磁盘空间不足");
        }
        catch (ProfileInstallInProgressException)
        {
            ClientStatusText = "安装正在进行";
            ShowToast("另一个启动器窗口正在安装这个客户端");
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            ClientStatusText = "安装未完成";
            ShowToast("客户端安装中断，重新操作会从已下载位置继续");
        }
        finally
        {
            IsProgressActive = false;
            if (_selectedProfileState != LocalProfileState.Ready)
            {
                PrimaryActionText = _selectedProfileState == LocalProfileState.UpdateRequired
                    ? "更新并进入"
                    : "安装客户端";
            }
        }

        return succeeded;
    }

    private async Task LaunchSelectedServerAsync()
    {
        if (IsProgressActive || SelectedServer is null)
        {
            return;
        }

        var selectedServer = SelectedServer;
        IsProgressActive = true;
        UpdateProgress = 0;
        ClientStatusText = "正在准备正版游戏会话";
        PrimaryActionText = "正在启动";
        var progress = new Progress<MinecraftLaunchProgress>(ApplyLaunchProgress);

        try
        {
            var launchSession = await _authenticationService.GetMinecraftLaunchSessionAsync();
            SetCurrentPlayer(_authenticationService.CurrentPlayer);
            await _gameLauncherService.LaunchAsync(
                new MinecraftLaunchRequest(
                    ClientDirectory,
                    selectedServer.ClientProfileId,
                    ParseMemoryInMiB(SelectedMemory),
                    launchSession),
                progress,
                async cancellationToken =>
                {
                    await _authenticationService.PrepareVelocityLaunchAsync(
                        selectedServer.Id,
                        cancellationToken);
                });
            UpdateProgress = 100;
            ClientStatusText = "游戏已启动";
            ShowToast($"正在进入 {selectedServer.Name}");
        }
        catch (LauncherAuthenticationRequiredException)
        {
            ClientStatusText = "需要登录后启动";
            ShowToast("请先使用 Microsoft 正版账号登录");
        }
        catch (MicrosoftReauthenticationRequiredException)
        {
            ClientStatusText = "游戏登录已过期";
            ShowToast("请退出账号后重新登录，以刷新游戏凭据");
        }
        catch (MicrosoftSignInFailedException)
        {
            ClientStatusText = "Microsoft 登录失败";
            ShowToast("Microsoft 登录失败，请稍后重试");
        }
        catch (MinecraftSignInException exception)
        {
            ClientStatusText = "正版身份验证失败";
            ShowToast(GetMinecraftSignInError(exception.Failure));
        }
        catch (MinecraftLaunchSessionExpiredException)
        {
            ClientStatusText = "游戏登录已过期";
            ShowToast("Minecraft 游戏凭据已过期，请重新启动");
        }
        catch (LauncherApiException exception)
        {
            ClientStatusText = "进服授权失败";
            ShowToast(exception.ApiDetail ?? "暂时无法取得服务器进入权限");
        }
        catch (MinecraftAlreadyRunningException)
        {
            ClientStatusText = "游戏正在运行";
            ShowToast("这个客户端已经在运行");
        }
        catch (MinecraftLaunchException exception)
        {
            ClientStatusText = exception.Failure switch
            {
                MinecraftLaunchFailure.InvalidProfile => "客户端启动信息无效",
                MinecraftLaunchFailure.RuntimePreparation => "Java 21 准备失败",
                MinecraftLaunchFailure.ProcessCreation => "无法生成游戏进程",
                _ => "游戏启动失败"
            };
            ShowToast(exception.Failure == MinecraftLaunchFailure.InvalidProfile
                ? "客户端不完整，请先修复客户端"
                : "游戏启动未完成，请检查网络或使用客户端修复");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            ClientStatusText = "进服授权服务不可用";
            ShowToast("无法连接赫朝授权服务，请稍后再试");
        }
        finally
        {
            IsProgressActive = false;
            PrimaryActionText = "进入服务器";
        }
    }

    private void ApplyInstallProgress(ClientInstallProgress progress)
    {
        UpdateProgress = progress.Percent;
        ClientStatusText = progress.Phase switch
        {
            ClientInstallPhase.Checking => "正在检查本地文件",
            ClientInstallPhase.Downloading => string.IsNullOrWhiteSpace(progress.CurrentPath)
                ? "正在下载客户端"
                : $"正在下载 {Path.GetFileName(progress.CurrentPath)}",
            ClientInstallPhase.Staging => "正在准备客户端",
            ClientInstallPhase.Switching => "正在切换客户端版本",
            ClientInstallPhase.Complete => "客户端已就绪",
            _ => ClientStatusText
        };
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

    private async Task RefreshClientStateAsync()
    {
        var selectedServer = SelectedServer;
        if (selectedServer is null || IsProgressActive ||
            !_clientProfiles.TryGetValue(selectedServer.ClientProfileId, out var profile))
        {
            return;
        }

        var state = await _installationService.GetLocalStateAsync(profile, ClientDirectory);
        if (SelectedServer?.Id != selectedServer.Id || IsProgressActive)
        {
            return;
        }

        _selectedProfileState = state;
        switch (state)
        {
            case LocalProfileState.Ready:
                UpdateProgress = 100;
                ClientStatusText = "客户端已就绪";
                PrimaryActionText = "进入服务器";
                break;
            case LocalProfileState.UpdateRequired:
                UpdateProgress = 0;
                ClientStatusText = "发现新版本";
                PrimaryActionText = "更新并进入";
                break;
            default:
                UpdateProgress = 0;
                ClientStatusText = "尚未安装";
                PrimaryActionText = "安装客户端";
                break;
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
            ShowToast("暂时无法打开客户端目录");
        }
    }

    private bool CanUseSelectedServer() =>
        !IsProgressActive && IsAuthenticated && SelectedServer?.Status == ServerStatus.Online;

    private async void StartAccountAction()
    {
        if (IsAccountBusy)
        {
            return;
        }

        IsAccountBusy = true;
        try
        {
            if (IsAuthenticated)
            {
                await _authenticationService.LogoutAsync();
                SetCurrentPlayer(null);
                ShowToast("已退出 Microsoft 账号");
            }
            else
            {
                SetCurrentPlayer(await _authenticationService.SignInAsync());
                ShowToast($"已登录为 {AccountDisplayName}");
            }

            await LoadCatalogAsync(userInitiated: true);
        }
        catch (MicrosoftAuthenticationNotConfiguredException)
        {
            ShowToast("Microsoft 登录应用尚未完成注册");
        }
        catch (MicrosoftSignInCanceledException)
        {
            ShowToast("已取消 Microsoft 登录");
        }
        catch (MicrosoftSignInFailedException)
        {
            ShowToast("Microsoft 登录失败，请稍后重试");
        }
        catch (MinecraftSignInException exception)
        {
            ShowToast(GetMinecraftSignInError(exception.Failure));
        }
        catch (LauncherApiException exception)
        {
            ShowToast(exception.ApiDetail ?? "账号验证失败");
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            ShowToast("登录服务暂时不可用");
        }
        finally
        {
            IsAccountBusy = false;
        }
    }

    private void SetCurrentPlayer(AuthenticatedPlayer? player)
    {
        _currentPlayer = player;
        OnPropertyChanged(nameof(IsAuthenticated));
        OnPropertyChanged(nameof(AccountDisplayName));
        OnPropertyChanged(nameof(AccountStatusText));
        OnPropertyChanged(nameof(AccountAccessText));
        OnPropertyChanged(nameof(AccountActionGlyph));
        OnPropertyChanged(nameof(AccountActionTooltip));
        PrimaryActionCommand.RaiseCanExecuteChanged();
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

    private static int ParseMemoryInMiB(string value)
    {
        var firstPart = value.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return int.TryParse(firstPart, out var gibibytes)
            ? checked(gibibytes * 1024)
            : 6 * 1024;
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
            KeepDownloadsAfterClose);
        _settingsStore.Save(_settings);
    }
}

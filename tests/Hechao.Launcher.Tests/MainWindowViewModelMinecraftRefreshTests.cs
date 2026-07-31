using Hechao.Contracts;
using Hechao.Distribution;
using Hechao.Launcher.Services;
using Hechao.Launcher.ViewModels;

namespace Hechao.Launcher.Tests;

public sealed class MainWindowViewModelMinecraftRefreshTests
{
    [Fact]
    public async Task EnterServer_RefreshesExpiredMinecraftSessionAndContinuesLaunch()
    {
        var authentication = new StubAuthenticationService();
        var gameLauncher = new StubGameLauncherService();
        var viewModel = CreateViewModel(authentication, gameLauncher);
        await WaitUntilAsync(() =>
            viewModel.SelectedServer is not null &&
            viewModel.ClientStatusText == "客户端已就绪");

        await viewModel.PrimaryActionCommand.ExecuteAsync();

        Assert.Equal(1, authentication.SilentSessionRequestCount);
        Assert.Equal(1, authentication.InteractiveSessionRequestCount);
        Assert.Equal(1, authentication.VelocityGrantRequestCount);
        Assert.Equal(1, gameLauncher.LaunchRequestCount);
        Assert.False(viewModel.IsMicrosoftSignInVisible);
        Assert.Equal("游戏已启动", viewModel.ClientStatusText);
    }

    [Fact]
    public async Task GameExit_RefreshesSelectedProfileStatusAndAction()
    {
        var authentication = new StubAuthenticationService();
        var gameLauncher = new StubGameLauncherService();
        var viewModel = CreateViewModel(authentication, gameLauncher);
        await WaitUntilAsync(() =>
            viewModel.SelectedServer is not null &&
            viewModel.ClientStatusText == "客户端已就绪");

        await viewModel.PrimaryActionCommand.ExecuteAsync();
        Assert.Equal("游戏已启动", viewModel.ClientStatusText);

        gameLauncher.RaiseProcessExited(exitCode: 0);

        await WaitUntilAsync(() => viewModel.ClientStatusText == "游戏已退出");
        Assert.Equal("进入服务器", viewModel.PrimaryActionText);
    }

    [Fact]
    public async Task EnterServer_WhenInteractiveRefreshIsCanceled_DoesNotLaunchOrCrash()
    {
        var authentication = new StubAuthenticationService
        {
            InteractiveSessionFailure = new MicrosoftSignInCanceledException()
        };
        var gameLauncher = new StubGameLauncherService();
        var viewModel = CreateViewModel(authentication, gameLauncher);
        await WaitUntilAsync(() =>
            viewModel.SelectedServer is not null &&
            viewModel.ClientStatusText == "客户端已就绪");

        await viewModel.PrimaryActionCommand.ExecuteAsync();

        Assert.Equal(1, authentication.InteractiveSessionRequestCount);
        Assert.Equal(0, gameLauncher.LaunchRequestCount);
        Assert.False(viewModel.IsMicrosoftSignInVisible);
        Assert.Equal("已取消正版认证", viewModel.ClientStatusText);
        Assert.Contains("已取消 Microsoft 正版认证", viewModel.ToastMessage);
    }

    [Fact]
    public async Task EnterServer_WhenInteractiveAccountDoesNotMatch_ShowsBoundPlayer()
    {
        var authentication = new StubAuthenticationService
        {
            InteractiveSessionFailure = new MicrosoftAccountMismatchException(
                "HechaoTester",
                "AnotherPlayer")
        };
        var gameLauncher = new StubGameLauncherService();
        var viewModel = CreateViewModel(authentication, gameLauncher);
        await WaitUntilAsync(() =>
            viewModel.SelectedServer is not null &&
            viewModel.ClientStatusText == "客户端已就绪");

        await viewModel.PrimaryActionCommand.ExecuteAsync();

        Assert.Equal(0, gameLauncher.LaunchRequestCount);
        Assert.False(viewModel.IsMicrosoftSignInVisible);
        Assert.Equal(LauncherPage.Account, viewModel.ActivePage);
        Assert.Equal("Microsoft 账号不匹配", viewModel.ClientStatusText);
        Assert.Contains("HechaoTester", viewModel.ToastMessage);
        Assert.Contains("AnotherPlayer", viewModel.ToastMessage);
    }

    [Fact]
    public async Task RollbackVersion_ActivatesCandidateAndOffersCatalogUpdate()
    {
        var authentication = new StubAuthenticationService();
        var gameLauncher = new StubGameLauncherService();
        var installation = new StubInstallationService
        {
            RollbackCandidate = CreateInstalledState("0.9.0")
        };
        var viewModel = CreateViewModel(
            authentication,
            gameLauncher,
            installation);
        await WaitUntilAsync(() =>
            viewModel.SelectedServer is not null &&
            viewModel.ClientStatusText == "客户端已就绪" &&
            viewModel.CanRollbackSelectedProfile);

        var result = await viewModel.RollbackSelectedProfileAsync();

        Assert.True(result);
        Assert.Equal(1, installation.RollbackRequestCount);
        Assert.Equal("已回滚到 v0.9.0", viewModel.ClientStatusText);
        Assert.Equal("更新客户端", viewModel.PrimaryActionText);
        Assert.Equal("1.0.5", viewModel.RollbackCandidateVersion);
    }

    [Fact]
    public async Task RollbackVersion_IsDisabledWhileProfileIsRunning()
    {
        var authentication = new StubAuthenticationService();
        var gameLauncher = new StubGameLauncherService
        {
            ProfileRunning = true
        };
        var installation = new StubInstallationService
        {
            RollbackCandidate = CreateInstalledState("0.9.0")
        };
        var viewModel = CreateViewModel(
            authentication,
            gameLauncher,
            installation);
        await WaitUntilAsync(() =>
            viewModel.SelectedServer is not null &&
            viewModel.ClientStatusText == "客户端已就绪");

        Assert.False(viewModel.CanRollbackSelectedProfile);
        Assert.Contains("先退出", viewModel.RollbackProfileToolTip);
        Assert.False(await viewModel.RollbackSelectedProfileAsync());
        Assert.Equal(0, installation.RollbackRequestCount);
    }

    [Fact]
    public async Task StartupUpdateCheckDisabled_DefersScanUntilPrimaryAction()
    {
        var authentication = new StubAuthenticationService();
        var gameLauncher = new StubGameLauncherService();
        var installation = new StubInstallationService();
        var viewModel = CreateViewModel(
            authentication,
            gameLauncher,
            installation,
            new LauncherSettings(CheckForUpdates: false));
        await WaitUntilAsync(() =>
            viewModel.SelectedServer is not null &&
            viewModel.ClientStatusText == "启动检查已关闭");

        Assert.Equal(0, installation.LocalStateRequestCount);
        Assert.Equal("检查客户端", viewModel.PrimaryActionText);

        await viewModel.PrimaryActionCommand.ExecuteAsync();

        Assert.Equal(1, installation.LocalStateRequestCount);
        Assert.Equal(1, gameLauncher.LaunchRequestCount);
        Assert.Equal("游戏已启动", viewModel.ClientStatusText);
    }

    [Fact]
    public async Task EnablingStartupUpdateCheck_RunsDeferredScanImmediately()
    {
        var authentication = new StubAuthenticationService();
        var gameLauncher = new StubGameLauncherService();
        var installation = new StubInstallationService();
        var viewModel = CreateViewModel(
            authentication,
            gameLauncher,
            installation,
            new LauncherSettings(CheckForUpdates: false));
        await WaitUntilAsync(() =>
            viewModel.SelectedServer is not null &&
            viewModel.ClientStatusText == "启动检查已关闭");

        viewModel.CheckForUpdates = true;

        await WaitUntilAsync(() =>
            installation.LocalStateRequestCount == 1 &&
            viewModel.ClientStatusText == "客户端已就绪");
        Assert.Equal("进入服务器", viewModel.PrimaryActionText);
    }

    [Fact]
    public async Task SwitchingServer_StopsCurrentGameBeforeRequestingTargetGrant()
    {
        var operations = new List<string>();
        var authentication = new StubAuthenticationService
        {
            OperationLog = operations
        };
        var gameLauncher = new StubGameLauncherService
        {
            ProfileRunning = true,
            RunningServerId = "survival2",
            OperationLog = operations
        };
        var viewModel = CreateViewModel(authentication, gameLauncher);
        await WaitUntilAsync(() =>
            viewModel.SelectedServer?.Id == "survival2" &&
            viewModel.ClientStatusText == "客户端已就绪");
        viewModel.SelectServerCommand.Execute(
            viewModel.Servers.Single(server => server.Id == "activity"));

        Assert.Equal("切换服务器", viewModel.PrimaryActionText);
        await viewModel.PrimaryActionCommand.ExecuteAsync();

        Assert.Equal(1, gameLauncher.StopRequestCount);
        Assert.Equal("activity", gameLauncher.LastLaunchRequest?.ServerId);
        Assert.True(
            operations.IndexOf("stop") < operations.IndexOf("grant:activity"));
        Assert.True(
            operations.IndexOf("grant:activity") < operations.IndexOf("start:activity"));
        Assert.Equal("游戏已启动", viewModel.ClientStatusText);
        Assert.Equal("重新连接", viewModel.PrimaryActionText);
    }

    [Fact]
    public async Task SwitchingServer_WhenCurrentGameCannotStop_DoesNotGrantOrLaunch()
    {
        var authentication = new StubAuthenticationService();
        var gameLauncher = new StubGameLauncherService
        {
            ProfileRunning = true,
            RunningServerId = "survival2",
            StopFailure = new MinecraftProcessStopException("test")
        };
        var viewModel = CreateViewModel(authentication, gameLauncher);
        await WaitUntilAsync(() =>
            viewModel.SelectedServer?.Id == "survival2" &&
            viewModel.ClientStatusText == "客户端已就绪");
        viewModel.SelectServerCommand.Execute(
            viewModel.Servers.Single(server => server.Id == "activity"));

        await viewModel.PrimaryActionCommand.ExecuteAsync();

        Assert.Equal(1, gameLauncher.StopRequestCount);
        Assert.Equal(0, authentication.VelocityGrantRequestCount);
        Assert.Equal(0, gameLauncher.LaunchRequestCount);
        Assert.Equal("无法安全关闭当前游戏", viewModel.ClientStatusText);
    }

    [Fact]
    public async Task Catalog_NeverDisplaysInfrastructureLobby()
    {
        var viewModel = CreateViewModel(
            new StubAuthenticationService(),
            new StubGameLauncherService());

        await WaitUntilAsync(() => viewModel.SelectedServer is not null);

        Assert.DoesNotContain(
            viewModel.Servers,
            server => string.Equals(
                server.Id,
                "lobby",
                StringComparison.OrdinalIgnoreCase));
        Assert.Equal("survival2", viewModel.SelectedServer?.Id);
    }

    [Fact]
    public async Task Startup_WithAvailableLauncherUpdate_DownloadsAutomatically()
    {
        var updateService = new StubLauncherUpdateService
        {
            Plan = new LauncherUpdatePlan(
                new Version(0, 13, 7),
                new Version(0, 14, 0),
                new Version(0, 12, 3),
                64 * 1024 * 1024,
                new string('a', 64),
                DateTimeOffset.UtcNow,
                "自动更新测试",
                new Uri("https://download.hechao.world/launcher.exe"))
        };

        var viewModel = CreateViewModel(
            new StubAuthenticationService(),
            new StubGameLauncherService(),
            launcherUpdateService: updateService);

        await WaitUntilAsync(() => updateService.DownloadRequestCount == 1);

        Assert.True(viewModel.IsLauncherUpdateVisible);
        Assert.Equal(100, viewModel.LauncherUpdateProgress);
        Assert.Contains("重新启动", viewModel.LauncherUpdateStatus);
    }

    [Fact]
    public async Task DeleteClient_PreservesSettingsBeforeRemovingProfile()
    {
        var operationLog = new List<string>();
        var installation = new StubInstallationService
        {
            OperationLog = operationLog
        };
        var playerSettings = new StubPlayerGameSettingsService
        {
            OperationLog = operationLog
        };
        var viewModel = CreateViewModel(
            new StubAuthenticationService(),
            new StubGameLauncherService(),
            installation,
            playerGameSettingsService: playerSettings);
        await WaitUntilAsync(() =>
            viewModel.SelectedServer is not null &&
            viewModel.ClientStatusText == "客户端已就绪");
        operationLog.Clear();

        var deleted = await viewModel.DeleteSelectedProfileAsync();

        Assert.True(deleted);
        Assert.Equal(["settings:import", "delete"], operationLog);
        Assert.Equal(1, installation.DeleteRequestCount);
        Assert.Equal("安装客户端", viewModel.PrimaryActionText);
        Assert.False(viewModel.CanDeleteSelectedProfile);
    }

    [Fact]
    public async Task EnterServer_AppliesSharedSettingsBeforeStartingGame()
    {
        var operationLog = new List<string>();
        var authentication = new StubAuthenticationService
        {
            OperationLog = operationLog
        };
        var gameLauncher = new StubGameLauncherService
        {
            OperationLog = operationLog
        };
        var playerSettings = new StubPlayerGameSettingsService
        {
            OperationLog = operationLog
        };
        var viewModel = CreateViewModel(
            authentication,
            gameLauncher,
            playerGameSettingsService: playerSettings);
        await WaitUntilAsync(() =>
            viewModel.SelectedServer is not null &&
            viewModel.ClientStatusText == "客户端已就绪");
        operationLog.Clear();

        await viewModel.PrimaryActionCommand.ExecuteAsync();

        Assert.Equal(
            ["settings:apply:base-1.21.11", "grant:survival2", "start:survival2"],
            operationLog);
    }

    private static MainWindowViewModel CreateViewModel(
        StubAuthenticationService authentication,
        StubGameLauncherService gameLauncher,
        StubInstallationService? installation = null,
        LauncherSettings? settings = null,
        ILauncherUpdateService? launcherUpdateService = null,
        IPlayerGameSettingsService? playerGameSettingsService = null)
    {
        return new MainWindowViewModel(
            new StubCatalogClient(),
            authentication,
            new StubSettingsStore(settings ?? new LauncherSettings()),
            installation ?? new StubInstallationService(),
            gameLauncher,
            new StubDownloadHistoryStore(),
            new StubGameDiagnosticsService(),
            new StubDiagnosticUploadService(),
            launcherUpdateService: launcherUpdateService,
            playerGameSettingsService: playerGameSettingsService);
    }

    private static InstalledProfileState CreateInstalledState(string version) =>
        new(
            ClientStorageLayout.CurrentStorageSchemaVersion,
            "base-1.21.11",
            version,
            new string('a', 64),
            "release-test",
            DateTimeOffset.UtcNow);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class StubCatalogClient : IServerCatalogClient
    {
        public Task<LauncherCatalogSnapshot> GetCatalogAsync(
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<ServerSummary> servers =
            [
                new(
                    "lobby",
                    "大厅",
                    "厅",
                    "厅",
                    ServerStatus.Online,
                    0,
                    200,
                    "1.21.11",
                    ModLoaderKind.Paper,
                    AccessTier.Member,
                    "base-1.21.11"),
                new(
                    "survival2",
                    "天域生存",
                    "域",
                    "域",
                    ServerStatus.Online,
                    0,
                    100,
                    "1.21.11",
                    ModLoaderKind.Paper,
                    AccessTier.Member,
                    "base-1.21.11"),
                new(
                    "activity",
                    "活动服",
                    "活",
                    "活",
                    ServerStatus.Online,
                    0,
                    30,
                    "1.21.11",
                    ModLoaderKind.NeoForge,
                    AccessTier.Participant,
                    "base-1.21.11")
            ];
            IReadOnlyList<ClientProfileSummary> profiles =
            [
                new(
                    "base-1.21.11",
                    "基础客户端",
                    "1.0.5",
                    1,
                    string.Empty,
                    DateTimeOffset.UtcNow)
            ];
            return Task.FromResult(
                new LauncherCatalogSnapshot(DateTimeOffset.UtcNow, servers, profiles));
        }
    }

    private sealed class StubAuthenticationService : ILauncherAuthenticationService
    {
        private static readonly Guid MinecraftUuid =
            Guid.Parse("12345678-1234-1234-1234-123456789abc");

        public HechaoAccount? CurrentAccount { get; } = new(
            Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            "tester",
            "测试玩家",
            null,
            MinecraftUuid,
            "HechaoTester",
            "default",
            AccessTier.Member,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        public AuthenticatedPlayer? CurrentPlayer => null;
        public Exception? InteractiveSessionFailure { get; init; }
        public int SilentSessionRequestCount { get; private set; }
        public int InteractiveSessionRequestCount { get; private set; }
        public int VelocityGrantRequestCount { get; private set; }
        public List<string>? OperationLog { get; init; }

        public Task<HechaoAccount?> TryRestoreAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CurrentAccount);

        public Task<MinecraftLaunchSession> GetMinecraftLaunchSessionAsync(
            CancellationToken cancellationToken = default)
        {
            SilentSessionRequestCount++;
            throw new MicrosoftReauthenticationRequiredException();
        }

        public Task<MinecraftLaunchSession> RefreshMinecraftLaunchSessionAsync(
            CancellationToken cancellationToken = default)
        {
            InteractiveSessionRequestCount++;
            if (InteractiveSessionFailure is not null)
            {
                throw InteractiveSessionFailure;
            }

            return Task.FromResult(new MinecraftLaunchSession(
                "HechaoTester",
                MinecraftUuid,
                "minecraft-access-token",
                DateTimeOffset.UtcNow.AddMinutes(30),
                "123456789"));
        }

        public Task<VelocityLaunchGrantResponse> PrepareVelocityLaunchAsync(
            string serverId,
            CancellationToken cancellationToken = default)
        {
            VelocityGrantRequestCount++;
            OperationLog?.Add($"grant:{serverId}");
            return Task.FromResult(new VelocityLaunchGrantResponse(
                Guid.NewGuid(),
                serverId,
                DateTimeOffset.UtcNow.AddMinutes(1)));
        }

        public Task SendRegistrationCodeAsync(
            string email,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<HechaoAccount> RegisterAsync(
            string username,
            string displayName,
            string password,
            string email,
            string code,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<HechaoAccount> LoginAsync(
            string usernameOrEmail,
            string password,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<HechaoAccount> LinkMinecraftAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task UnlinkMinecraftAsync(
            string currentPassword,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AdminBrowserTicketResponse> CreateAdminBrowserTicketAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task LogoutAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<SessionRevocationResponse> LogoutAllDevicesAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StubSettingsStore : ILauncherSettingsStore
    {
        private readonly LauncherSettings _settings;

        public StubSettingsStore(LauncherSettings settings)
        {
            _settings = settings;
        }

        public LauncherSettings Load() => _settings;

        public void Save(LauncherSettings settings)
        {
        }
    }

    private sealed class StubDiagnosticUploadService : IGameDiagnosticUploadService
    {
        public Task<DiagnosticUploadReceipt> UploadAsync(
            GameDiagnosticBundleResult bundle,
            string profileId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StubLauncherUpdateService : ILauncherUpdateService
    {
        public LauncherUpdatePlan? Plan { get; init; }
        public int DownloadRequestCount { get; private set; }

        public Task<LauncherUpdatePlan?> CheckAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Plan);

        public Task<bool> DownloadAndLaunchUpdaterAsync(
            LauncherUpdatePlan plan,
            IProgress<LauncherUpdateDownloadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            DownloadRequestCount++;
            progress?.Report(new LauncherUpdateDownloadProgress(
                plan.InstallerBytes,
                plan.InstallerBytes));
            return Task.FromResult(true);
        }
    }

    private sealed class StubPlayerGameSettingsService : IPlayerGameSettingsService
    {
        public List<string>? OperationLog { get; init; }

        public Task ImportLatestAsync(
            string dataRoot,
            CancellationToken cancellationToken = default)
        {
            OperationLog?.Add("settings:import");
            return Task.CompletedTask;
        }

        public Task CaptureProfileAsync(
            string dataRoot,
            string profileId,
            CancellationToken cancellationToken = default)
        {
            OperationLog?.Add($"settings:capture:{profileId}");
            return Task.CompletedTask;
        }

        public Task ApplyToProfileAsync(
            string dataRoot,
            string profileId,
            CancellationToken cancellationToken = default)
        {
            OperationLog?.Add($"settings:apply:{profileId}");
            return Task.CompletedTask;
        }
    }

    private sealed class StubInstallationService : IClientInstallationService
    {
        public InstalledProfileState? RollbackCandidate { get; set; }
        public int LocalStateRequestCount { get; private set; }
        public int RollbackRequestCount { get; private set; }
        public int DeleteRequestCount { get; private set; }
        public List<string>? OperationLog { get; init; }

        public Task<LocalProfileState> GetLocalStateAsync(
            ClientProfileSummary profile,
            string dataRoot,
            CancellationToken cancellationToken = default)
        {
            LocalStateRequestCount++;
            return Task.FromResult(LocalProfileState.Ready);
        }

        public Task<InstalledProfileState?> GetRollbackCandidateAsync(
            ClientProfileSummary profile,
            string dataRoot,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(RollbackCandidate);

        public Task InstallAsync(
            ClientProfileSummary profile,
            ClientInstallationOptions options,
            IProgress<ClientInstallProgress>? progress = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> DeleteAsync(
            ClientProfileSummary profile,
            string dataRoot,
            CancellationToken cancellationToken = default)
        {
            DeleteRequestCount++;
            OperationLog?.Add("delete");
            return Task.FromResult(true);
        }

        public Task<InstalledProfileState> RollbackAsync(
            ClientProfileSummary profile,
            string dataRoot,
            IProgress<ClientInstallProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            RollbackRequestCount++;
            var activated = RollbackCandidate ??
                throw new ProfileRollbackUnavailableException(profile.Id);
            RollbackCandidate = CreateInstalledState(profile.Version);
            progress?.Report(new ClientInstallProgress(
                ClientInstallPhase.Complete,
                100,
                string.Empty,
                0,
                0));
            return Task.FromResult(activated);
        }
    }

    private sealed class StubGameLauncherService : IMinecraftGameLauncherService
    {
        public event EventHandler<MinecraftProcessExitedEventArgs>? ProcessExited;

        public int LaunchRequestCount { get; private set; }
        public int StopRequestCount { get; private set; }
        public bool ProfileRunning { get; set; }
        public string? RunningServerId { get; set; }
        public Exception? StopFailure { get; init; }
        public List<string>? OperationLog { get; init; }
        public MinecraftLaunchRequest? LastLaunchRequest { get; private set; }

        public bool IsProfileRunning(string profileId) => ProfileRunning;

        public MinecraftRunningGame? GetRunningGame() =>
            ProfileRunning
                ? new MinecraftRunningGame(
                    "base-1.21.11",
                    RunningServerId,
                    1234,
                    DateTimeOffset.UtcNow.AddMinutes(-1))
                : null;

        public Task<MinecraftStopResult> StopRunningGameAsync(
            TimeSpan gracefulTimeout,
            IProgress<MinecraftStopProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            StopRequestCount++;
            OperationLog?.Add("stop");
            if (StopFailure is not null)
            {
                throw StopFailure;
            }

            progress?.Report(new MinecraftStopProgress(MinecraftStopPhase.Complete));
            ProfileRunning = false;
            RunningServerId = null;
            return Task.FromResult(
                new MinecraftStopResult(MinecraftStopOutcome.Graceful));
        }

        public async Task<MinecraftLaunchResult> LaunchAsync(
            MinecraftLaunchRequest request,
            IProgress<MinecraftLaunchProgress>? progress = null,
            Func<CancellationToken, Task>? beforeStart = null,
            CancellationToken cancellationToken = default)
        {
            LaunchRequestCount++;
            LastLaunchRequest = request;
            if (beforeStart is not null)
            {
                await beforeStart(cancellationToken);
            }

            ProfileRunning = true;
            RunningServerId = request.ServerId;
            OperationLog?.Add($"start:{request.ServerId}");
            return new MinecraftLaunchResult(1234);
        }

        public void RaiseProcessExited(
            int? exitCode,
            MinecraftProcessExitKind exitKind = MinecraftProcessExitKind.Natural)
        {
            ProfileRunning = false;
            RunningServerId = null;
            var exitedAt = DateTimeOffset.UtcNow;
            ProcessExited?.Invoke(
                this,
                new MinecraftProcessExitedEventArgs(
                    "base-1.21.11",
                    1234,
                    exitCode,
                    exitedAt.AddMinutes(-1),
                    exitedAt,
                    exitKind));
        }
    }

    private sealed class StubDownloadHistoryStore : IDownloadHistoryStore
    {
        public IReadOnlyList<DownloadHistoryRecord> Load() => [];

        public void Save(IEnumerable<DownloadHistoryRecord> records)
        {
        }
    }

    private sealed class StubGameDiagnosticsService : IGameDiagnosticsService
    {
        public string DiagnosticsDirectory => Path.GetTempPath();

        public GameExitRecord? LoadLatestExit() => null;

        public Task RecordExitAsync(
            GameExitRecord record,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<GameDiagnosticBundleResult> CreateBundleAsync(
            GameDiagnosticBundleRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}

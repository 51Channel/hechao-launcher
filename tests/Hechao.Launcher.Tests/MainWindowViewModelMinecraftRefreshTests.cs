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

    private static MainWindowViewModel CreateViewModel(
        StubAuthenticationService authentication,
        StubGameLauncherService gameLauncher,
        StubInstallationService? installation = null)
    {
        return new MainWindowViewModel(
            new StubCatalogClient(),
            authentication,
            new StubSettingsStore(),
            installation ?? new StubInstallationService(),
            gameLauncher,
            new StubDownloadHistoryStore(),
            new StubGameDiagnosticsService(),
            new StubDiagnosticUploadService());
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
        public LauncherSettings Load() => new();

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

    private sealed class StubInstallationService : IClientInstallationService
    {
        public InstalledProfileState? RollbackCandidate { get; set; }
        public int RollbackRequestCount { get; private set; }

        public Task<LocalProfileState> GetLocalStateAsync(
            ClientProfileSummary profile,
            string dataRoot,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(LocalProfileState.Ready);

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
        public event EventHandler<MinecraftProcessExitedEventArgs>? ProcessExited
        {
            add { }
            remove { }
        }

        public int LaunchRequestCount { get; private set; }
        public bool ProfileRunning { get; init; }

        public bool IsProfileRunning(string profileId) => ProfileRunning;

        public async Task<MinecraftLaunchResult> LaunchAsync(
            MinecraftLaunchRequest request,
            IProgress<MinecraftLaunchProgress>? progress = null,
            Func<CancellationToken, Task>? beforeStart = null,
            CancellationToken cancellationToken = default)
        {
            LaunchRequestCount++;
            if (beforeStart is not null)
            {
                await beforeStart(cancellationToken);
            }

            return new MinecraftLaunchResult(1234);
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

using Hechao.Contracts;
using Hechao.Distribution;
using Hechao.Launcher.Mac.ViewModels;
using Hechao.Launcher.Services;
using Hechao.Launcher.ViewModels;

namespace Hechao.Launcher.Mac.Tests;

internal sealed class TestLauncherFixture
{
    public FakeAuthenticationService Authentication { get; } = new();
    public FakeInstallationService Installation { get; } = new();
    public FakeGameLauncherService GameLauncher { get; } = new();

    public LauncherMacViewModel CreateViewModel() => new(
        Authentication,
        new FakeCatalogClient(),
        new FakeSettingsStore(),
        Installation,
        GameLauncher,
        new FakeDownloadHistoryStore(),
        new FakeDiagnosticsService(),
        NullPlayerGameSettingsService.Instance,
        new FakeSkinService());

    internal static HechaoAccount CreateAccount() => new(
        Guid.Parse("4c51ed0d-cadf-4b94-8738-a21cae9c6e33"),
        "tester",
        "测试玩家",
        "tester@example.com",
        Guid.Parse("7b4fb4cc-25e1-4aca-913d-9dfd1df96801"),
        "TestPlayer",
        "member",
        AccessTier.Member,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow);

    internal sealed class FakeAuthenticationService : ILauncherAuthenticationService
    {
        private HechaoAccount? _account = CreateAccount();
        public int AuthorizationCount { get; private set; }
        public HechaoAccount? CurrentAccount => _account;
        public AuthenticatedPlayer? CurrentPlayer => null;

        public Task<HechaoAccount?> TryRestoreAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_account);

        public Task SendRegistrationCodeAsync(string email, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<HechaoAccount> RegisterAsync(
            string username,
            string displayName,
            string password,
            string email,
            string code,
            CancellationToken cancellationToken = default) => Task.FromResult(_account!);

        public Task<HechaoAccount> LoginAsync(
            string usernameOrEmail,
            string password,
            CancellationToken cancellationToken = default) => Task.FromResult(_account!);

        public Task<HechaoAccount> LinkMinecraftAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_account!);

        public Task UnlinkMinecraftAsync(string currentPassword, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<MinecraftLaunchSession> GetMinecraftLaunchSessionAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(new MinecraftLaunchSession(
                "TestPlayer",
                _account!.MinecraftUuid!.Value,
                "minecraft-access-token",
                DateTimeOffset.UtcNow.AddHours(1),
                "xuid"));

        public Task<MinecraftLaunchSession> RefreshMinecraftLaunchSessionAsync(
            CancellationToken cancellationToken = default) => GetMinecraftLaunchSessionAsync(cancellationToken);

        public Task<VelocityLaunchGrantResponse> PrepareVelocityLaunchAsync(
            string serverId,
            CancellationToken cancellationToken = default)
        {
            AuthorizationCount++;
            return Task.FromResult(new VelocityLaunchGrantResponse(
                Guid.NewGuid(), serverId, DateTimeOffset.UtcNow.AddMinutes(2)));
        }

        public Task<AdminBrowserTicketResponse> CreateAdminBrowserTicketAsync(
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task LogoutAsync(CancellationToken cancellationToken = default)
        {
            _account = null;
            return Task.CompletedTask;
        }

        public Task<SessionRevocationResponse> LogoutAllDevicesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new SessionRevocationResponse(1, 0));
    }

    internal sealed class FakeCatalogClient : IServerCatalogClient
    {
        public Task<LauncherCatalogSnapshot> GetCatalogAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(new LauncherCatalogSnapshot(
                DateTimeOffset.UtcNow,
                [new ServerSummary(
                    "survival2",
                    "赫朝生存二服 · 演示",
                    "生存",
                    "S",
                    ServerStatus.Online,
                    18,
                    80,
                    "1.21.11",
                    ModLoaderKind.Fabric,
                    AccessTier.Member,
                    "survival2-1.21.11",
                    "演示数据：稳定运行中的长期模组生存世界。",
                    CatalogSection: ServerCatalogSection.Permanent)],
                [new ClientProfileSummary(
                    "survival2-1.21.11",
                    "生存二服整合包",
                    "2026.08.15",
                    1024,
                    new string('a', 64),
                    DateTimeOffset.UtcNow)]));
    }

    internal sealed class FakeSettingsStore : ILauncherSettingsStore
    {
        private LauncherSettings _settings = new(
            ClientDirectory: "/Users/tester/Library/Application Support/Hechao/GameData");
        public LauncherSettings Load() => _settings;
        public void Save(LauncherSettings settings) => _settings = settings;
    }

    internal sealed class FakeInstallationService : IClientInstallationService
    {
        private LocalProfileState _state = LocalProfileState.Missing;
        private TaskCompletionSource? _installStarted;
        private TaskCompletionSource? _continueInstall;
        public bool FailNextInstall { get; set; }
        public int InstallCount { get; private set; }

        public void PauseNextInstall()
        {
            _installStarted = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _continueInstall = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public Task WaitForInstallAsync() =>
            _installStarted?.Task ??
            throw new InvalidOperationException("Install is not paused.");

        public void ResumeInstall() => _continueInstall?.TrySetResult();

        public Task<LocalProfileState> GetLocalStateAsync(
            ClientProfileSummary profile,
            string dataRoot,
            CancellationToken cancellationToken = default) => Task.FromResult(_state);

        public Task<InstalledProfileState?> GetRollbackCandidateAsync(
            ClientProfileSummary profile,
            string dataRoot,
            CancellationToken cancellationToken = default) => Task.FromResult<InstalledProfileState?>(null);

        public async Task InstallAsync(
            ClientProfileSummary profile,
            ClientInstallationOptions options,
            IProgress<ClientInstallProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            InstallCount++;
            progress?.Report(new ClientInstallProgress(
                ClientInstallPhase.Downloading, 42, "mods/example.jar", 430, profile.DownloadBytes));
            _installStarted?.TrySetResult();
            if (_continueInstall is not null)
            {
                await _continueInstall.Task.WaitAsync(cancellationToken);
            }
            if (FailNextInstall)
            {
                FailNextInstall = false;
                throw new IOException("演示下载失败：签名对象暂时不可用。");
            }
            progress?.Report(new ClientInstallProgress(
                ClientInstallPhase.Complete, 100, "完成", profile.DownloadBytes, profile.DownloadBytes));
            _state = LocalProfileState.Ready;
        }

        public Task<bool> DeleteAsync(
            ClientProfileSummary profile,
            string dataRoot,
            CancellationToken cancellationToken = default)
        {
            _state = LocalProfileState.Missing;
            return Task.FromResult(true);
        }

        public Task<InstalledProfileState> RollbackAsync(
            ClientProfileSummary profile,
            string dataRoot,
            IProgress<ClientInstallProgress>? progress = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    internal sealed class FakeGameLauncherService : IMinecraftGameLauncherService
    {
        private MinecraftRunningGame? _running;
        public int LaunchCount { get; private set; }
        public event EventHandler<MinecraftProcessExitedEventArgs>? ProcessExited
        {
            add { }
            remove { }
        }
        public bool IsProfileRunning(string profileId) => _running?.ProfileId == profileId;
        public MinecraftRunningGame? GetRunningGame() => _running;

        public async Task<MinecraftLaunchResult> LaunchAsync(
            MinecraftLaunchRequest request,
            IProgress<MinecraftLaunchProgress>? progress = null,
            Func<CancellationToken, Task>? beforeStart = null,
            CancellationToken cancellationToken = default)
        {
            if (beforeStart is not null)
            {
                await beforeStart(cancellationToken);
            }
            LaunchCount++;
            _running = new MinecraftRunningGame(
                request.ProfileId, request.ServerId, 4242, DateTimeOffset.UtcNow, request.DataRoot);
            return new MinecraftLaunchResult(4242);
        }

        public Task<MinecraftStopResult> StopRunningGameAsync(
            TimeSpan gracefulTimeout,
            IProgress<MinecraftStopProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            _running = null;
            return Task.FromResult(new MinecraftStopResult(MinecraftStopOutcome.Graceful));
        }
    }

    private sealed class FakeDownloadHistoryStore : IDownloadHistoryStore
    {
        public IReadOnlyList<DownloadHistoryRecord> Load() => [];
        public void Save(IEnumerable<DownloadHistoryRecord> records)
        {
        }
    }

    private sealed class FakeDiagnosticsService : IGameDiagnosticsService
    {
        public string DiagnosticsDirectory => Path.GetTempPath();
        public GameExitRecord? LoadLatestExit() => null;
        public Task RecordExitAsync(GameExitRecord record, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
        public Task<GameDiagnosticBundleResult> CreateBundleAsync(
            GameDiagnosticBundleRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeSkinService : IMinecraftSkinService
    {
        public Task<MinecraftSkinImage?> GetSkinAsync(
            Guid minecraftUuid,
            CancellationToken cancellationToken = default) => Task.FromResult<MinecraftSkinImage?>(null);
    }
}

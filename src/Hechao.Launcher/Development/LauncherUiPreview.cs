#if DEBUG
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Hechao.Contracts;
using Hechao.Distribution;
using Hechao.Launcher.Services;
using Hechao.Launcher.ViewModels;

namespace Hechao.Launcher.Development;

internal static class LauncherUiPreview
{
    private const string ScreenshotArgumentPrefix = "--ui-preview-screenshot=";
    private const string ScreenshotSizeArgumentPrefix = "--ui-preview-size=";
    private const string PreviewDataRoot = @"D:\Hechao\Launcher-UI-Preview\GameData";
    private const string PreviewDiagnosticsRoot = @"D:\Hechao\Launcher-UI-Preview\Diagnostics";

    public static bool TryGetRequestedTheme(
        IEnumerable<string> arguments,
        out bool useDarkMode)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        foreach (var argument in arguments)
        {
            if (string.Equals(
                    argument,
                    "--ui-preview=dark",
                    StringComparison.OrdinalIgnoreCase))
            {
                useDarkMode = true;
                return true;
            }

            if (string.Equals(
                    argument,
                    "--ui-preview=light",
                    StringComparison.OrdinalIgnoreCase))
            {
                useDarkMode = false;
                return true;
            }
        }

        useDarkMode = true;
        return false;
    }

    public static bool TryGetScreenshotRequest(
        IEnumerable<string> arguments,
        out ScreenshotRequest request)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        string? outputPath = null;
        var width = 1500;
        var height = 860;
        foreach (var argument in arguments)
        {
            if (argument.StartsWith(
                    ScreenshotArgumentPrefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                outputPath = argument[ScreenshotArgumentPrefix.Length..].Trim();
                continue;
            }

            if (!argument.StartsWith(
                    ScreenshotSizeArgumentPrefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var dimensions = argument[ScreenshotSizeArgumentPrefix.Length..]
                .Split('x', 2, StringSplitOptions.TrimEntries);
            if (dimensions.Length != 2 ||
                !int.TryParse(dimensions[0], out width) ||
                !int.TryParse(dimensions[1], out height))
            {
                throw new ArgumentException(
                    "UI preview size must use the form WIDTHxHEIGHT.",
                    nameof(arguments));
            }
        }

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            request = null!;
            return false;
        }

        if (width is < 1060 or > 3840 || height is < 640 or > 2160)
        {
            throw new ArgumentOutOfRangeException(
                nameof(arguments),
                "UI preview screenshots must be between 1060x640 and 3840x2160.");
        }

        var normalizedPath = Path.GetFullPath(outputPath);
        if (!string.Equals(
                Path.GetExtension(normalizedPath),
                ".png",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "UI preview screenshots must use a .png output path.",
                nameof(arguments));
        }

        request = new ScreenshotRequest(normalizedPath, width, height);
        return true;
    }

    public static async Task CaptureWhenReadyAsync(
        Window window,
        MainWindowViewModel viewModel,
        ScreenshotRequest request)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(request);

        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while ((viewModel.IsCatalogLoading ||
                viewModel.SelectedServer is null ||
                string.Equals(
                    viewModel.PrimaryActionText,
                    "检查客户端",
                    StringComparison.Ordinal)) &&
               DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        if (viewModel.IsCatalogLoading || viewModel.SelectedServer is null)
        {
            throw new TimeoutException("The preview catalog did not finish loading.");
        }

        await window.Dispatcher.InvokeAsync(
            window.UpdateLayout,
            DispatcherPriority.ContextIdle);
        await Task.Delay(100);
        window.UpdateLayout();

        if (Math.Abs(window.ActualWidth - request.Width) > 1 ||
            Math.Abs(window.ActualHeight - request.Height) > 1)
        {
            throw new InvalidOperationException(
                $"Preview window arranged at {window.ActualWidth:0.#}x" +
                $"{window.ActualHeight:0.#}, expected {request.Width}x{request.Height}.");
        }

        var bitmap = new RenderTargetBitmap(
            request.Width,
            request.Height,
            96,
            96,
            PixelFormats.Pbgra32);
        bitmap.Render(window);

        var outputDirectory = Path.GetDirectoryName(request.OutputPath) ??
            throw new InvalidOperationException("The screenshot output directory is missing.");
        Directory.CreateDirectory(outputDirectory);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new FileStream(
            request.OutputPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None);
        encoder.Save(stream);
    }

    public static MainWindowViewModel CreateViewModel(
        bool useDarkMode,
        ILauncherThemeService themeService)
    {
        ArgumentNullException.ThrowIfNull(themeService);

        var settings = new LauncherSettings(
            SelectedServerId: "skyrealm",
            Memory: "8 GB",
            ClientDirectory: PreviewDataRoot,
            CheckForUpdates: true,
            KeepDownloadsAfterClose: true,
            CloseLauncherAfterGameStart: false,
            OpenDownloadsWhenInstalling: true,
            StartupPage: "服务器",
            UseSystemProxy: false,
            UseDarkMode: useDarkMode);

        return new MainWindowViewModel(
            new PreviewCatalogClient(),
            new PreviewAuthenticationService(),
            new MemorySettingsStore(settings),
            new PreviewInstallationService(),
            new PreviewGameLauncherService(),
            new MemoryDownloadHistoryStore(),
            new PreviewGameDiagnosticsService(),
            new PreviewDiagnosticUploadService(),
            telemetryService: NullLauncherTelemetryService.Instance,
            launcherUpdateService: NullLauncherUpdateService.Instance,
            minecraftSkinService: NullMinecraftSkinService.Instance,
            playerGameSettingsService: NullPlayerGameSettingsService.Instance,
            catalogFallbackRetryDelay: TimeSpan.FromMinutes(5),
            activityCatalogRefreshInterval: TimeSpan.FromMinutes(5),
            themeService: themeService,
            initialSettings: settings,
            isUiPreview: true);
    }

    private static InvalidOperationException PreviewOnlyException() =>
        new("UI 预览不会执行账号、下载、诊断上传或游戏启动操作。");

    private sealed class PreviewCatalogClient : IServerCatalogClient
    {
        public Task<LauncherCatalogSnapshot> GetCatalogAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var now = DateTimeOffset.Now;
            var activityStartsAt = new DateTimeOffset(
                now.Date.AddDays(1).AddHours(19).AddMinutes(30),
                now.Offset);

            IReadOnlyList<ServerSummary> servers =
            [
                new(
                    "skyrealm",
                    "赫朝天域",
                    "天域",
                    "域",
                    ServerStatus.Online,
                    42,
                    100,
                    "1.21.11",
                    ModLoaderKind.Paper,
                    AccessTier.Member,
                    "skyrealm-1.21.11",
                    "长期生存世界，和熟悉的人一起慢慢建设。",
                    CatalogSection: ServerCatalogSection.Permanent),
                new(
                    "shopping-street",
                    "商业街建筑对决",
                    "商业街",
                    "街",
                    ServerStatus.Online,
                    16,
                    24,
                    "1.20.1",
                    ModLoaderKind.Forge,
                    AccessTier.Participant,
                    "shopping-street-1.20.1",
                    "今晚开放试玩，组队认领店铺并完成限时建筑挑战。",
                    activityStartsAt,
                    activityStartsAt.AddHours(3),
                    ServerCatalogSection.Activity),
                new(
                    "doll-night",
                    "玩偶惊魂夜",
                    "惊魂夜",
                    "偶",
                    ServerStatus.Maintenance,
                    0,
                    20,
                    "1.21.11",
                    ModLoaderKind.Fabric,
                    AccessTier.Participant,
                    "doll-night-1.21.11",
                    "场景维护中，开放时间确认后会在活动页通知。",
                    CatalogSection: ServerCatalogSection.Activity)
            ];

            IReadOnlyList<ClientProfileSummary> profiles =
            [
                new(
                    "skyrealm-1.21.11",
                    "天域基础客户端",
                    "1.0.4",
                    48_234_102,
                    string.Empty,
                    now.AddDays(-5)),
                new(
                    "shopping-street-1.20.1",
                    "商业街活动客户端",
                    "0.9.5",
                    132_120_576,
                    string.Empty,
                    now.AddDays(-2)),
                new(
                    "doll-night-1.21.11",
                    "玩偶惊魂夜客户端",
                    "0.6.2",
                    82_575_360,
                    string.Empty,
                    now.AddDays(-1))
            ];

            return Task.FromResult(new LauncherCatalogSnapshot(now, servers, profiles));
        }
    }

    private sealed class PreviewAuthenticationService : ILauncherAuthenticationService
    {
        private static readonly Guid UserId =
            Guid.Parse("5c52be5f-e129-43d0-8d50-908c6f65d0d8");
        private static readonly Guid MinecraftUuid =
            Guid.Parse("f84c6a79-2bdc-4f4b-9fc2-78cbeca2ac11");
        private static readonly DateTimeOffset CreatedAt =
            new(2025, 8, 1, 8, 0, 0, TimeSpan.Zero);

        public HechaoAccount? CurrentAccount { get; private set; } = CreateAccount();

        public AuthenticatedPlayer? CurrentPlayer => CurrentAccount is null
            ? null
            : new AuthenticatedPlayer(
                UserId,
                MinecraftUuid,
                "HechaoPlayer",
                "participant",
                AccessTier.Participant,
                DateTimeOffset.UtcNow);

        public Task<HechaoAccount?> TryRestoreAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CurrentAccount);

        public Task SendRegistrationCodeAsync(
            string email,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<HechaoAccount> RegisterAsync(
            string username,
            string displayName,
            string password,
            string email,
            string code,
            bool legalAccepted,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CurrentAccount ??= CreateAccount());

        public Task<HechaoAccount> LoginAsync(
            string usernameOrEmail,
            string password,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CurrentAccount ??= CreateAccount());

        public Task<HechaoAccount> LinkMinecraftAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CurrentAccount ??= CreateAccount());

        public Task UnlinkMinecraftAsync(
            string currentPassword,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<MinecraftLaunchSession> GetMinecraftLaunchSessionAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromException<MinecraftLaunchSession>(PreviewOnlyException());

        public Task<MinecraftLaunchSession> RefreshMinecraftLaunchSessionAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromException<MinecraftLaunchSession>(PreviewOnlyException());

        public Task<VelocityLaunchGrantResponse> PrepareVelocityLaunchAsync(
            string serverId,
            CancellationToken cancellationToken = default) =>
            Task.FromException<VelocityLaunchGrantResponse>(PreviewOnlyException());

        public Task<AdminBrowserTicketResponse> CreateAdminBrowserTicketAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromException<AdminBrowserTicketResponse>(PreviewOnlyException());

        public Task LogoutAsync(CancellationToken cancellationToken = default)
        {
            CurrentAccount = null;
            return Task.CompletedTask;
        }

        public Task<SessionRevocationResponse> LogoutAllDevicesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromException<SessionRevocationResponse>(PreviewOnlyException());

        private static HechaoAccount CreateAccount() =>
            new(
                UserId,
                "preview",
                "赫朝成员",
                null,
                MinecraftUuid,
                "HechaoPlayer",
                "participant",
                AccessTier.Participant,
                DateTimeOffset.UtcNow,
                CreatedAt);
    }

    private sealed class MemorySettingsStore(LauncherSettings settings)
        : ILauncherSettingsStore
    {
        private LauncherSettings _settings = settings;

        public LauncherSettings Load() => _settings;

        public void Save(LauncherSettings nextSettings) => _settings = nextSettings;
    }

    private sealed class PreviewInstallationService : IClientInstallationService
    {
        public Task<LocalProfileState> GetLocalStateAsync(
            ClientProfileSummary profile,
            string dataRoot,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(LocalProfileState.Ready);

        public Task<InstalledProfileState?> GetRollbackCandidateAsync(
            ClientProfileSummary profile,
            string dataRoot,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<InstalledProfileState?>(null);

        public Task InstallAsync(
            ClientProfileSummary profile,
            ClientInstallationOptions options,
            IProgress<ClientInstallProgress>? progress = null,
            CancellationToken cancellationToken = default) =>
            Task.FromException(PreviewOnlyException());

        public Task<bool> DeleteAsync(
            ClientProfileSummary profile,
            string dataRoot,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<InstalledProfileState> RollbackAsync(
            ClientProfileSummary profile,
            string dataRoot,
            IProgress<ClientInstallProgress>? progress = null,
            CancellationToken cancellationToken = default) =>
            Task.FromException<InstalledProfileState>(PreviewOnlyException());
    }

    private sealed class PreviewGameLauncherService : IMinecraftGameLauncherService
    {
        public event EventHandler<MinecraftProcessExitedEventArgs>? ProcessExited
        {
            add { }
            remove { }
        }

        public bool IsProfileRunning(string profileId) => false;

        public MinecraftRunningGame? GetRunningGame() => null;

        public Task<MinecraftStopResult> StopRunningGameAsync(
            TimeSpan gracefulTimeout,
            IProgress<MinecraftStopProgress>? progress = null,
            CancellationToken cancellationToken = default) =>
            Task.FromException<MinecraftStopResult>(PreviewOnlyException());

        public Task<MinecraftLaunchResult> LaunchAsync(
            MinecraftLaunchRequest request,
            IProgress<MinecraftLaunchProgress>? progress = null,
            Func<CancellationToken, Task>? beforeStart = null,
            CancellationToken cancellationToken = default) =>
            Task.FromException<MinecraftLaunchResult>(PreviewOnlyException());
    }

    private sealed class MemoryDownloadHistoryStore : IDownloadHistoryStore
    {
        private IReadOnlyList<DownloadHistoryRecord> _records = [];

        public IReadOnlyList<DownloadHistoryRecord> Load() => _records;

        public void Save(IEnumerable<DownloadHistoryRecord> records) =>
            _records = records.ToArray();
    }

    private sealed class PreviewGameDiagnosticsService : IGameDiagnosticsService
    {
        public string DiagnosticsDirectory => PreviewDiagnosticsRoot;

        public GameExitRecord? LoadLatestExit() => null;

        public Task RecordExitAsync(
            GameExitRecord record,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<GameDiagnosticBundleResult> CreateBundleAsync(
            GameDiagnosticBundleRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromException<GameDiagnosticBundleResult>(
                new IOException("UI 预览不会创建诊断包。"));
    }

    private sealed class PreviewDiagnosticUploadService : IGameDiagnosticUploadService
    {
        public Task<DiagnosticUploadReceipt> UploadAsync(
            GameDiagnosticBundleResult bundle,
            string profileId,
            CancellationToken cancellationToken = default) =>
            Task.FromException<DiagnosticUploadReceipt>(
                new IOException("UI 预览不会上传诊断包。"));
    }

    internal sealed record ScreenshotRequest(
        string OutputPath,
        int Width,
        int Height);
}
#endif

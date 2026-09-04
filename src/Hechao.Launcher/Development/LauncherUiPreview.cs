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
    private const string PreviewPageArgumentPrefix = "--ui-preview-page=";
    private const string PreviewSettingsTabArgumentPrefix =
        "--ui-preview-settings-tab=";
    private const string ScreenshotArgumentPrefix = "--ui-preview-screenshot=";
    private const string ScreenshotSizeArgumentPrefix = "--ui-preview-size=";
    private const string ScreenshotThemeSwitchArgumentPrefix =
        "--ui-preview-switch-theme=";
    private static readonly string PreviewRoot = Path.Combine(
        Path.GetTempPath(),
        "Hechao",
        "Launcher-UI-Preview");
    private static readonly string PreviewDataRoot = Path.Combine(
        PreviewRoot,
        "GameData");
    private static readonly string PreviewDiagnosticsRoot = Path.Combine(
        PreviewRoot,
        "Diagnostics");

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
        var width = 1200;
        var height = 720;
        bool? useDarkModeAfterRender = null;
        foreach (var argument in arguments)
        {
            if (argument.StartsWith(
                    ScreenshotArgumentPrefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                outputPath = argument[ScreenshotArgumentPrefix.Length..].Trim();
                continue;
            }

            if (argument.StartsWith(
                    ScreenshotThemeSwitchArgumentPrefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                useDarkModeAfterRender =
                    argument[ScreenshotThemeSwitchArgumentPrefix.Length..]
                        .Trim()
                        .ToLowerInvariant() switch
                    {
                        "dark" => true,
                        "light" => false,
                        _ => throw new ArgumentException(
                            "UI preview runtime theme must be dark or light.",
                            nameof(arguments))
                    };
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

        request = new ScreenshotRequest(
            normalizedPath,
            width,
            height,
            useDarkModeAfterRender);
        return true;
    }

    public static bool TryGetRequestedPage(
        IEnumerable<string> arguments,
        out LauncherPage page)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        foreach (var argument in arguments)
        {
            if (!argument.StartsWith(
                    PreviewPageArgumentPrefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            page = argument[PreviewPageArgumentPrefix.Length..]
                .Trim()
                .ToLowerInvariant() switch
            {
                "servers" => LauncherPage.Servers,
                "downloads" => LauncherPage.Downloads,
                "activities" => LauncherPage.Activities,
                "account" => LauncherPage.Account,
                "settings" => LauncherPage.Settings,
                _ => throw new ArgumentException(
                    "UI preview page must be servers, downloads, activities, account, or settings.",
                    nameof(arguments))
            };
            return true;
        }

        page = LauncherPage.Servers;
        return false;
    }

    public static bool TryGetRequestedSettingsTab(
        IEnumerable<string> arguments,
        out int selectedIndex)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        foreach (var argument in arguments)
        {
            if (!argument.StartsWith(
                    PreviewSettingsTabArgumentPrefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            selectedIndex = argument[PreviewSettingsTabArgumentPrefix.Length..]
                .Trim()
                .ToLowerInvariant() switch
            {
                "game" => 0,
                "client" => 1,
                "behavior" => 2,
                "diagnostics" => 3,
                _ => throw new ArgumentException(
                    "UI preview settings tab must be game, client, behavior, or diagnostics.",
                    nameof(arguments))
            };
            return true;
        }

        selectedIndex = 0;
        return false;
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

        if (viewModel.ActivePage == LauncherPage.Activities &&
            viewModel.ActivityCalendar.HasNoSelectedActivities)
        {
            var activityDay = viewModel.ActivityCalendar.Days
                .FirstOrDefault(day => day.HasActivities);
            if (activityDay is not null)
            {
                viewModel.ActivityCalendar.SelectDayCommand.Execute(activityDay);
            }
        }

        if (request.UseDarkModeAfterRender is { } useDarkModeAfterRender &&
            viewModel.UseDarkMode != useDarkModeAfterRender)
        {
            viewModel.UseDarkMode = useDarkModeAfterRender;
            await window.Dispatcher.InvokeAsync(
                window.UpdateLayout,
                DispatcherPriority.ContextIdle);
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
        ILauncherThemeService themeService,
        LauncherPage previewPage = LauncherPage.Servers)
    {
        ArgumentNullException.ThrowIfNull(themeService);
        if (!Enum.IsDefined(previewPage))
        {
            throw new ArgumentOutOfRangeException(nameof(previewPage));
        }

        var startupPage = previewPage switch
        {
            LauncherPage.Downloads => "下载中心",
            LauncherPage.Activities => "活动",
            _ => "服务器"
        };

        var settings = new LauncherSettings(
            SelectedServerId: "skyrealm",
            Memory: "8 GB",
            ClientDirectory: PreviewDataRoot,
            CheckForUpdates: true,
            KeepDownloadsAfterClose: true,
            CloseLauncherAfterGameStart: false,
            OpenDownloadsWhenInstalling: true,
            StartupPage: startupPage,
            UseSystemProxy: false,
            UseDarkMode: useDarkMode);

        var viewModel = new MainWindowViewModel(
            new PreviewCatalogClient(),
            new PreviewAuthenticationService(),
            new MemorySettingsStore(settings),
            new PreviewInstallationService(),
            new PreviewGameLauncherService(),
            new MemoryDownloadHistoryStore(
                previewPage == LauncherPage.Downloads
                    ? CreateDownloadHistory()
                    : []),
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

        switch (previewPage)
        {
            case LauncherPage.Account:
                viewModel.ShowAccountCommand.Execute(null);
                break;
            case LauncherPage.Settings:
                viewModel.ShowSettingsPageCommand.Execute(null);
                break;
        }

        return viewModel;
    }

    private static IReadOnlyList<DownloadHistoryRecord> CreateDownloadHistory()
    {
        var now = DateTimeOffset.Now;
        return
        [
            new DownloadHistoryRecord(
                Guid.Parse("384f78f8-1f29-4f85-a926-eb1905559976"),
                "shopping-street-1.20.1",
                "商业街活动客户端",
                "0.9.5",
                now.AddHours(-3),
                now.AddHours(-2).AddMinutes(-46),
                DownloadJobStatus.Failed,
                86_245_376,
                132_120_576,
                "mods/architectury-9.2.14-forge.jar",
                "连接中断，已保留校验通过的文件"),
            new DownloadHistoryRecord(
                Guid.Parse("3e84e57c-9ebd-43c6-85ba-3bbf6e812e70"),
                "skyrealm-1.21.11",
                "天域基础客户端",
                "1.0.4",
                now.AddDays(-1).AddMinutes(-6),
                now.AddDays(-1),
                DownloadJobStatus.Completed,
                48_234_102,
                48_234_102,
                string.Empty,
                null)
        ];
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
                    "block-break-valorant",
                    "赫朝挖方块瓦罗兰特爆破挑战",
                    "挖方块爆破",
                    "爆",
                    ServerStatus.Online,
                    8,
                    10,
                    "1.20.1",
                    ModLoaderKind.Forge,
                    AccessTier.Participant,
                    "block-break-valorant-1.20.1",
                    "挖掘资源换取装备，在方块战场完成限时爆破挑战。",
                    activityStartsAt.AddHours(1),
                    activityStartsAt.AddDays(6),
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
                    "block-break-valorant-1.20.1",
                    "挖方块爆破客户端",
                    "1.0.0",
                    164_626_432,
                    string.Empty,
                    now.AddHours(-18)),
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
        private IReadOnlyList<DownloadHistoryRecord> _records;

        public MemoryDownloadHistoryStore(
            IEnumerable<DownloadHistoryRecord> records)
        {
            _records = records.ToArray();
        }

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
        int Height,
        bool? UseDarkModeAfterRender = null);
}
#endif

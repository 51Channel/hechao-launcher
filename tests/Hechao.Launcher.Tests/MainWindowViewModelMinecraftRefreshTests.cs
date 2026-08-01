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
    public async Task Catalog_UsesExplicitActivitySectionInsteadOfServerId()
    {
        var viewModel = CreateViewModel(
            new StubAuthenticationService(),
            new StubGameLauncherService());

        await WaitUntilAsync(() => viewModel.SelectedServer is not null);

        var activity = Assert.Single(viewModel.ActivityServers);
        Assert.Equal("activity", activity.Server.Id);
        Assert.DoesNotContain(
            viewModel.ActivityServers,
            item => item.Server.Id == "survival2");
    }

    [Fact]
    public async Task Catalog_OlderRequestCannotOverwriteNewerResponse()
    {
        var catalog = new ControllableCatalogClient();
        var viewModel = CreateViewModel(
            new StubAuthenticationService(),
            new StubGameLauncherService(),
            catalogClient: catalog);
        await WaitUntilAsync(() => catalog.RequestCount == 1);

        viewModel.RefreshCommand.Execute(null);
        await WaitUntilAsync(() => catalog.RequestCount == 2);
        catalog.SecondResponse.SetResult(CreateCatalogSnapshot("new-server", "新目录"));
        await WaitUntilAsync(() => viewModel.SelectedServer?.Id == "new-server");

        catalog.FirstResponse.SetResult(CreateCatalogSnapshot("old-server", "旧目录"));
        await Task.Delay(50);

        Assert.Equal("new-server", viewModel.SelectedServer?.Id);
        Assert.DoesNotContain(viewModel.Servers, server => server.Id == "old-server");
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
    public async Task Startup_WhenTargetVersionPreviouslyFailed_WaitsForManualRetry()
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
                "修复说明",
                new Uri("https://download.hechao.world/launcher.exe")),
            HasPreviousInstallFailureResult = true
        };
        var viewModel = CreateViewModel(
            new StubAuthenticationService(),
            new StubGameLauncherService(),
            launcherUpdateService: updateService);

        await WaitUntilAsync(() => viewModel.IsLauncherUpdateVisible);

        Assert.Equal(0, updateService.DownloadRequestCount);
        Assert.Contains("不会循环重启", viewModel.LauncherUpdateStatus);

        await viewModel.InstallLauncherUpdateCommand.ExecuteAsync();

        Assert.Equal(1, updateService.DownloadRequestCount);
    }

    [Fact]
    public async Task UpdateCheck_AfterTransientFailure_CanBeRetriedManually()
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
                "重试说明",
                new Uri("https://download.hechao.world/launcher.exe")),
            CheckFailuresRemaining = 1
        };
        var viewModel = CreateViewModel(
            new StubAuthenticationService(),
            new StubGameLauncherService(),
            launcherUpdateService: updateService);
        await WaitUntilAsync(() => updateService.CheckRequestCount == 1);

        await viewModel.CheckLauncherUpdateCommand.ExecuteAsync();

        Assert.Equal(2, updateService.CheckRequestCount);
        Assert.True(viewModel.IsLauncherUpdateVisible);
        Assert.Equal(0, updateService.DownloadRequestCount);
    }

    [Fact]
    public async Task Install_WhenSelectionChanges_DoesNotMarkNewSelectionReady()
    {
        var installRelease = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var installation = new StubInstallationService
        {
            LocalState = LocalProfileState.Missing,
            InstallRelease = installRelease
        };
        var gameLauncher = new StubGameLauncherService();
        var dataRoot = Path.Combine(Path.GetTempPath(), "hechao-install-snapshot");
        var viewModel = CreateViewModel(
            new StubAuthenticationService(),
            gameLauncher,
            installation,
            new LauncherSettings(
                SelectedServerId: "server-a",
                ClientDirectory: dataRoot),
            catalogClient: new StaticCatalogClient(CreateTwoProfileCatalog()));
        await WaitUntilAsync(() =>
            viewModel.SelectedServer?.Id == "server-a" &&
            viewModel.PrimaryActionText == "安装客户端");

        var installTask = viewModel.PrimaryActionCommand.ExecuteAsync();
        await installation.InstallStarted.Task;
        Assert.False(viewModel.SelectServerCommand.CanExecute(
            viewModel.Servers.Single(server => server.Id == "server-b")));
        viewModel.SelectedServer = viewModel.Servers.Single(server => server.Id == "server-b");
        installRelease.SetResult(null);
        await installTask;
        await WaitUntilAsync(() => viewModel.PrimaryActionText == "安装客户端");

        Assert.Equal("server-b", viewModel.SelectedServer?.Id);
        Assert.Equal("profile-a", installation.LastInstalledProfileId);
        Assert.Equal(Path.GetFullPath(dataRoot), installation.LastInstallOptions?.DataRoot);
        Assert.Equal(0, gameLauncher.LaunchRequestCount);
    }

    [Fact]
    public async Task Launch_UsesServerJavaMemoryAndDirectoryCapturedAtStart()
    {
        var signInGate = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var authentication = new StubAuthenticationService
        {
            InteractiveSessionGate = signInGate
        };
        var gameLauncher = new StubGameLauncherService();
        var dataRoot = Path.Combine(Path.GetTempPath(), "hechao-launch-snapshot");
        var javaA = Path.Combine(dataRoot, "java-a.exe");
        var viewModel = CreateViewModel(
            authentication,
            gameLauncher,
            new StubInstallationService(),
            new LauncherSettings(
                SelectedServerId: "server-a",
                Memory: "4 GB",
                ClientDirectory: dataRoot,
                ProfileJavaPaths: new Dictionary<string, string>
                {
                    ["profile-a"] = javaA,
                    ["profile-b"] = Path.Combine(dataRoot, "java-b.exe")
                }),
            catalogClient: new StaticCatalogClient(CreateTwoProfileCatalog()));
        await WaitUntilAsync(() =>
            viewModel.SelectedServer?.Id == "server-a" &&
            viewModel.PrimaryActionText == "进入服务器");

        var launchTask = viewModel.PrimaryActionCommand.ExecuteAsync();
        await WaitUntilAsync(() => authentication.InteractiveSessionRequestCount == 1);
        viewModel.SelectedServer = viewModel.Servers.Single(server => server.Id == "server-b");
        viewModel.SelectedMemory = "16 GB";
        signInGate.SetResult(null);
        await launchTask;

        var request = Assert.IsType<MinecraftLaunchRequest>(gameLauncher.LastLaunchRequest);
        Assert.Equal("server-a", request.ServerId);
        Assert.Equal("profile-a", request.ProfileId);
        Assert.Equal(4096, request.MaximumRamMb);
        Assert.Equal(Path.GetFullPath(dataRoot), request.DataRoot);
        Assert.Equal(javaA, request.JavaExecutablePath);
    }

    [Fact]
    public async Task MaintenanceServer_CanInstallButCannotLaunch()
    {
        var installation = new StubInstallationService
        {
            LocalState = LocalProfileState.Missing
        };
        var gameLauncher = new StubGameLauncherService();
        var viewModel = CreateViewModel(
            new StubAuthenticationService(),
            gameLauncher,
            installation,
            new LauncherSettings(SelectedServerId: "server-a"),
            catalogClient: new StaticCatalogClient(
                CreateTwoProfileCatalog(firstStatus: ServerStatus.Maintenance)));
        await WaitUntilAsync(() => viewModel.PrimaryActionText == "安装客户端");

        Assert.True(viewModel.PrimaryActionCommand.CanExecute(null));
        await viewModel.PrimaryActionCommand.ExecuteAsync();

        Assert.Equal(1, installation.InstallRequestCount);
        Assert.Equal(0, gameLauncher.LaunchRequestCount);
        Assert.Equal("维护中", viewModel.PrimaryActionText);
        Assert.False(viewModel.PrimaryActionCommand.CanExecute(null));
    }

    [Fact]
    public async Task GameExit_CapturesSettingsFromActualRunningDataRoot()
    {
        var gameLauncher = new StubGameLauncherService();
        var playerSettings = new StubPlayerGameSettingsService();
        var viewModel = CreateViewModel(
            new StubAuthenticationService(),
            gameLauncher,
            playerGameSettingsService: playerSettings);
        await WaitUntilAsync(() => viewModel.SelectedServer is not null);
        var actualRunningRoot = Path.Combine(Path.GetTempPath(), "hechao-old-data-root");
        viewModel.UpdateClientDirectory(
            Path.Combine(Path.GetTempPath(), "hechao-new-data-root"));

        gameLauncher.RaiseProcessExited(0, dataRoot: actualRunningRoot);
        await WaitUntilAsync(() => playerSettings.LastCapturedDataRoot is not null);

        Assert.Equal(actualRunningRoot, playerSettings.LastCapturedDataRoot);
    }

    [Fact]
    public async Task Install_WhenDownloadHistoryCannotBeSaved_StillCompletesCleanup()
    {
        var installation = new StubInstallationService
        {
            LocalState = LocalProfileState.Missing
        };
        var gameLauncher = new StubGameLauncherService();
        var viewModel = CreateViewModel(
            new StubAuthenticationService(),
            gameLauncher,
            installation,
            downloadHistoryStore: new ThrowingDownloadHistoryStore());
        await WaitUntilAsync(() => viewModel.PrimaryActionText == "安装客户端");

        await viewModel.PrimaryActionCommand.ExecuteAsync();

        Assert.Equal(1, installation.InstallRequestCount);
        Assert.Equal(1, gameLauncher.LaunchRequestCount);
        Assert.False(viewModel.IsProgressActive);
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

    [Fact]
    public async Task DeleteClient_CapturesDirectoryAndDisablesSettingsReset()
    {
        var dataRoot = Path.Combine(
            Path.GetTempPath(),
            $"hechao-delete-{Guid.NewGuid():N}");
        var installation = new StubInstallationService();
        var playerSettings = new StubPlayerGameSettingsService();
        var viewModel = CreateViewModel(
            new StubAuthenticationService(),
            new StubGameLauncherService(),
            installation,
            new LauncherSettings(ClientDirectory: dataRoot),
            playerGameSettingsService: playerSettings);
        await WaitUntilAsync(() =>
            viewModel.SelectedServer is not null &&
            viewModel.ClientStatusText == "客户端已就绪");

        playerSettings.ImportStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        playerSettings.ImportRelease = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var deleteTask = viewModel.DeleteSelectedProfileAsync();
        await playerSettings.ImportStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(viewModel.ResetLauncherSettingsCommand.CanExecute(null));
        Assert.Equal(Path.GetFullPath(dataRoot), viewModel.ClientDirectory);

        playerSettings.ImportRelease.SetResult(null);
        Assert.True(await deleteTask);
        Assert.Equal(
            Path.GetFullPath(dataRoot),
            Path.GetFullPath(installation.LastDeleteDataRoot!));
        Assert.Equal(
            Path.GetFullPath(dataRoot),
            Path.GetFullPath(playerSettings.LastImportedDataRoot!));
    }

    [Fact]
    public async Task ClientState_OlderDirectoryRequestCannotOverwriteNewDirectory()
    {
        var firstDataRoot = Path.Combine(
            Path.GetTempPath(),
            $"hechao-state-a-{Guid.NewGuid():N}");
        var secondDataRoot = Path.Combine(
            Path.GetTempPath(),
            $"hechao-state-b-{Guid.NewGuid():N}");
        var firstResponse = new TaskCompletionSource<LocalProfileState>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var installation = new StubInstallationService
        {
            LocalStateHandler = (_, dataRoot, _) =>
                string.Equals(
                    Path.GetFullPath(dataRoot),
                    Path.GetFullPath(firstDataRoot),
                    StringComparison.OrdinalIgnoreCase)
                    ? firstResponse.Task
                    : Task.FromResult(LocalProfileState.Ready),
        };
        var viewModel = CreateViewModel(
            new StubAuthenticationService(),
            new StubGameLauncherService(),
            installation,
            new LauncherSettings(ClientDirectory: firstDataRoot));
        await WaitUntilAsync(() =>
            viewModel.SelectedServer is not null &&
            installation.LocalStateRequestCount >= 1);

        viewModel.UpdateClientDirectory(secondDataRoot);
        await WaitUntilAsync(() =>
            installation.LocalStateRequestCount >= 2 &&
            viewModel.ClientStatusText == "客户端已就绪");

        firstResponse.SetResult(LocalProfileState.Missing);
        await Task.Delay(100);

        Assert.Equal(Path.GetFullPath(secondDataRoot), viewModel.ClientDirectory);
        Assert.Equal("客户端已就绪", viewModel.ClientStatusText);
        Assert.Equal("进入服务器", viewModel.PrimaryActionText);
    }

    [Fact]
    public async Task Catalog_LegacyEntriesRetainOriginalActivityClassification()
    {
        var viewModel = CreateViewModel(
            new StubAuthenticationService(),
            new StubGameLauncherService(),
            catalogClient: new StaticCatalogClient(CreateLegacyCatalog()));

        await WaitUntilAsync(() => viewModel.SelectedServer is not null);

        var activity = Assert.Single(viewModel.ActivityServers);
        Assert.Equal("legacy-event", activity.Server.Id);
        Assert.DoesNotContain(
            viewModel.ActivityServers,
            item => item.Server.Id == "survival2");
    }

    [Fact]
    public void CatalogBoundaryDelaySlice_LongScheduleStaysWithinTaskDelayLimit()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.Equal(
            TimeSpan.FromDays(30),
            MainWindowViewModel.GetCatalogBoundaryDelaySlice(
                now.AddDays(60),
                now));
        Assert.Equal(
            TimeSpan.Zero,
            MainWindowViewModel.GetCatalogBoundaryDelaySlice(
                now.AddSeconds(-2),
                now));
    }

    [Fact]
    public void NavigationSelectionSetters_KeepExactlyOnePageSelected()
    {
        var viewModel = CreateViewModel(
            new StubAuthenticationService(),
            new StubGameLauncherService());
        var selections = new Action[]
        {
            () => viewModel.IsServersPage = true,
            () => viewModel.IsDownloadsPage = true,
            () => viewModel.IsActivitiesPage = true,
            () => viewModel.IsAccountPage = true,
            () => viewModel.IsSettingsPage = true
        };

        foreach (var select in selections)
        {
            select();
            Assert.Single(
                new[]
                {
                    viewModel.IsServersPage,
                    viewModel.IsDownloadsPage,
                    viewModel.IsActivitiesPage,
                    viewModel.IsAccountPage,
                    viewModel.IsSettingsPage
                },
                isSelected => isSelected);
        }

        viewModel.IsSettingsPage = false;
        Assert.True(viewModel.IsSettingsPage);
    }

    [Fact]
    public async Task Catalog_SameServerProfileVersionAndHashChange_RechecksReadyClientState()
    {
        var initialCatalog = CreateCatalogSnapshot("catalog-test", "目录测试");
        var changedCatalog = initialCatalog with
        {
            ClientProfiles =
            [
                initialCatalog.ClientProfiles[0] with
                {
                    Version = "1.0.1",
                    Sha256 = new string('b', 64),
                },
            ],
        };
        var catalog = new ControllableCatalogClient();
        var refreshedState = new TaskCompletionSource<LocalProfileState>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var localStateChecks = 0;
        var installation = new StubInstallationService
        {
            LocalStateHandler = (_, _, _) =>
                Interlocked.Increment(ref localStateChecks) >= 2
                ? refreshedState.Task
                : Task.FromResult(LocalProfileState.Ready),
        };

        var viewModel = CreateViewModel(
            new StubAuthenticationService(),
            new StubGameLauncherService(),
            installation,
            catalogClient: catalog);

        await WaitUntilAsync(() => catalog.RequestCount == 1);
        catalog.FirstResponse.SetResult(initialCatalog);
        await WaitUntilAsync(() =>
            viewModel.SelectedServer is not null &&
            viewModel.ClientStatusText == "客户端已就绪");

        viewModel.RefreshCommand.Execute(null);
        await WaitUntilAsync(() => catalog.RequestCount == 2);
        catalog.SecondResponse.SetResult(changedCatalog);
        await WaitUntilAsync(() => installation.LocalStateRequestCount == 2);

        Assert.Equal("正在检查客户端", viewModel.ClientStatusText);

        refreshedState.SetResult(LocalProfileState.Ready);
        await WaitUntilAsync(() => viewModel.ClientStatusText == "客户端已就绪");

        Assert.Equal("catalog-test", viewModel.SelectedServer?.Id);
        Assert.Equal(2, installation.LocalStateRequestCount);
    }

    [Fact]
    public async Task LauncherUpdateCheck_OlderResponseCannotOverwriteNewerResponse()
    {
        var firstResponse = new TaskCompletionSource<LauncherUpdatePlan?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var secondResponse = new TaskCompletionSource<LauncherUpdatePlan?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var updateService = new StubLauncherUpdateService();
        updateService.CheckResponses.Enqueue(firstResponse.Task);
        updateService.CheckResponses.Enqueue(secondResponse.Task);
        var viewModel = CreateViewModel(
            new StubAuthenticationService(),
            new StubGameLauncherService(),
            launcherUpdateService: updateService);

        await WaitUntilAsync(() => updateService.CheckRequestCount == 1);

        var newerCheck = InvokeLauncherUpdateCheckAsync(viewModel, userInitiated: true);
        await WaitUntilAsync(() => updateService.CheckRequestCount == 2);
        secondResponse.SetResult(CreateLauncherUpdatePlan(new Version(0, 15, 0)));
        await newerCheck;
        await WaitUntilAsync(() =>
            viewModel.IsLauncherUpdateVisible &&
            viewModel.LauncherUpdateTitle.Contains("0.15.0", StringComparison.Ordinal));

        firstResponse.SetResult(CreateLauncherUpdatePlan(new Version(0, 14, 0)));
        await Task.Delay(50);

        Assert.Contains("0.15.0", viewModel.LauncherUpdateTitle, StringComparison.Ordinal);
        Assert.DoesNotContain("0.14.0", viewModel.LauncherUpdateTitle, StringComparison.Ordinal);
        Assert.Equal(0, updateService.DownloadRequestCount);
    }

    [Fact]
    public async Task LauncherUpdate_AvailableDuringInstall_DefersAutoInstallUntilTaskCompletes()
    {
        var updateResponse = new TaskCompletionSource<LauncherUpdatePlan?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var updateService = new StubLauncherUpdateService();
        updateService.CheckResponses.Enqueue(updateResponse.Task);
        var installRelease = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var installation = new StubInstallationService
        {
            LocalState = LocalProfileState.Missing,
            InstallRelease = installRelease,
        };
        var viewModel = CreateViewModel(
            new StubAuthenticationService(),
            new StubGameLauncherService(),
            installation,
            launcherUpdateService: updateService);

        await WaitUntilAsync(() => viewModel.PrimaryActionText == "安装客户端");

        var installAndLaunch = viewModel.PrimaryActionCommand.ExecuteAsync();
        await installation.InstallStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        updateResponse.SetResult(CreateLauncherUpdatePlan(new Version(0, 15, 0)));
        await WaitUntilAsync(() => updateService.CheckRequestCount == 1);
        await Task.Delay(50);

        Assert.Equal(0, updateService.DownloadRequestCount);
        Assert.True(viewModel.IsProgressActive);

        installRelease.SetResult(null);
        await installAndLaunch;
        await WaitUntilAsync(() => updateService.DownloadRequestCount == 1);

    }

    [Fact]
    public async Task ActivityLaunch_WhenAuthoritativeRefreshTurnsServerOffline_DoesNotLaunch()
    {
        var catalog = new ControllableCatalogClient();
        var gameLauncher = new StubGameLauncherService();
        var viewModel = CreateViewModel(
            new StubAuthenticationService(),
            gameLauncher,
            catalogClient: catalog);

        await WaitUntilAsync(() => catalog.RequestCount == 1);
        catalog.FirstResponse.SetResult(CreateActivityCatalog(ServerStatus.Online));
        await WaitUntilAsync(() =>
            viewModel.SelectedServer?.Status == ServerStatus.Online &&
            viewModel.ClientStatusText == "客户端已就绪");

        var action = viewModel.PrimaryActionCommand.ExecuteAsync();
        await WaitUntilAsync(() => catalog.RequestCount == 2);
        catalog.SecondResponse.SetResult(CreateActivityCatalog(ServerStatus.Closed));
        await action;

        Assert.Equal(ServerStatus.Closed, viewModel.SelectedServer?.Status);
        Assert.Equal(0, gameLauncher.LaunchRequestCount);
    }

    [Fact]
    public async Task Catalog_CacheFallbackAutomaticallyRetriesUntilLiveSnapshotArrives()
    {
        var catalog = new CacheThenLiveCatalogClient(CreateCatalogSnapshot("catalog-test", "目录测试"));
        _ = CreateViewModel(
            new StubAuthenticationService(),
            new StubGameLauncherService(),
            catalogClient: catalog,
            catalogFallbackRetryDelay: TimeSpan.FromMilliseconds(20));

        await WaitUntilAsync(() => catalog.RequestCount >= 2);

        Assert.Equal(2, catalog.RequestCount);
        Assert.Equal(CatalogSource.Live, catalog.LastSource);
    }

    [Fact]
    public async Task TelemetryThatNeverCompletes_DoesNotKeepInstallProgressActive()
    {
        var telemetry = new NeverCompletingTelemetryService();
        var installation = new StubInstallationService
        {
            LocalState = LocalProfileState.Missing,
        };
        var gameLauncher = new StubGameLauncherService();
        var viewModel = CreateViewModel(
            new StubAuthenticationService(),
            gameLauncher,
            installation,
            telemetryService: telemetry);

        await WaitUntilAsync(() => viewModel.PrimaryActionText == "安装客户端");
        await viewModel.PrimaryActionCommand.ExecuteAsync();

        Assert.Equal(1, installation.InstallRequestCount);
        Assert.Equal(1, gameLauncher.LaunchRequestCount);
        Assert.False(viewModel.IsProgressActive);
        Assert.True(telemetry.RecordRequestCount >= 2);
    }

    private static Task InvokeLauncherUpdateCheckAsync(
        MainWindowViewModel viewModel,
        bool userInitiated)
    {
        var method = typeof(MainWindowViewModel).GetMethod(
            "TryCheckLauncherUpdateAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        return Assert.IsAssignableFrom<Task>(method?.Invoke(viewModel, [userInitiated]));
    }

    private static MainWindowViewModel CreateViewModel(
        StubAuthenticationService authentication,
        StubGameLauncherService gameLauncher,
        StubInstallationService? installation = null,
        LauncherSettings? settings = null,
        ILauncherUpdateService? launcherUpdateService = null,
        IPlayerGameSettingsService? playerGameSettingsService = null,
        IServerCatalogClient? catalogClient = null,
        IDownloadHistoryStore? downloadHistoryStore = null,
        ILauncherTelemetryService? telemetryService = null,
        TimeSpan? catalogFallbackRetryDelay = null)
    {
        return new MainWindowViewModel(
            catalogClient ?? new StubCatalogClient(),
            authentication,
            new StubSettingsStore(settings ?? new LauncherSettings()),
            installation ?? new StubInstallationService(),
            gameLauncher,
            downloadHistoryStore ?? new StubDownloadHistoryStore(),
            new StubGameDiagnosticsService(),
            new StubDiagnosticUploadService(),
            telemetryService: telemetryService,
            launcherUpdateService: launcherUpdateService,
            playerGameSettingsService: playerGameSettingsService,
            catalogFallbackRetryDelay: catalogFallbackRetryDelay);
    }

    private static InstalledProfileState CreateInstalledState(string version) =>
        new(
            ClientStorageLayout.CurrentStorageSchemaVersion,
            "base-1.21.11",
            version,
            new string('a', 64),
            "release-test",
            DateTimeOffset.UtcNow);

    private static LauncherCatalogSnapshot CreateCatalogSnapshot(
        string serverId,
        string serverName)
    {
        const string profileId = "catalog-test-profile";
        return new LauncherCatalogSnapshot(
            DateTimeOffset.UtcNow,
            [
                new ServerSummary(
                    serverId,
                    serverName,
                    "测",
                    "测",
                    ServerStatus.Online,
                    0,
                    20,
                    "1.21.11",
                    ModLoaderKind.Paper,
                    AccessTier.Member,
                    profileId,
                    CatalogSection: ServerCatalogSection.Permanent)
            ],
            [
                new ClientProfileSummary(
                    profileId,
                    "目录测试客户端",
                    "1.0.0",
                    1,
                    string.Empty,
                    DateTimeOffset.UtcNow)
            ]);
    }

    private static LauncherCatalogSnapshot CreateActivityCatalog(ServerStatus status)
    {
        var snapshot = CreateCatalogSnapshot("activity-test", "活动测试");
        return snapshot with
        {
            Servers =
            [
                snapshot.Servers[0] with
                {
                    Status = status,
                    CatalogSection = ServerCatalogSection.Activity,
                },
            ],
        };
    }

    private static LauncherUpdatePlan CreateLauncherUpdatePlan(Version targetVersion) =>
        new(
            new Version(0, 13, 7),
            targetVersion,
            new Version(0, 12, 3),
            64 * 1024 * 1024,
            new string('a', 64),
            DateTimeOffset.UtcNow,
            "自动更新测试",
            new Uri("https://download.hechao.world/launcher.exe"));

    private static LauncherCatalogSnapshot CreateTwoProfileCatalog(
        ServerStatus firstStatus = ServerStatus.Online,
        ServerStatus secondStatus = ServerStatus.Online) =>
        new(
            DateTimeOffset.UtcNow,
            [
                new ServerSummary(
                    "server-a",
                    "服务器 A",
                    "A",
                    "A",
                    firstStatus,
                    0,
                    20,
                    "1.21.11",
                    ModLoaderKind.NeoForge,
                    AccessTier.Member,
                    "profile-a",
                    CatalogSection: ServerCatalogSection.Permanent),
                new ServerSummary(
                    "server-b",
                    "服务器 B",
                    "B",
                    "B",
                    secondStatus,
                    0,
                    20,
                    "1.20.1",
                    ModLoaderKind.Fabric,
                    AccessTier.Member,
                    "profile-b",
                    CatalogSection: ServerCatalogSection.Permanent)
            ],
            [
                new ClientProfileSummary(
                    "profile-a",
                    "客户端 A",
                    "1.0.0",
                    1024,
                    string.Empty,
                    DateTimeOffset.UtcNow),
                new ClientProfileSummary(
                    "profile-b",
                    "客户端 B",
                    "1.0.0",
                    2048,
                    string.Empty,
                    DateTimeOffset.UtcNow)
            ]);

    private static LauncherCatalogSnapshot CreateLegacyCatalog()
    {
        var current = CreateTwoProfileCatalog();
        return current with
        {
            Servers =
            [
                current.Servers[0] with
                {
                    Id = "survival2",
                    CatalogSection = null,
                },
                current.Servers[1] with
                {
                    Id = "legacy-event",
                    CatalogSection = null,
                },
            ],
        };
    }

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
                    "base-1.21.11",
                    CatalogSection: ServerCatalogSection.Activity)
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

    private sealed class ControllableCatalogClient : IServerCatalogClient
    {
        public TaskCompletionSource<LauncherCatalogSnapshot> FirstResponse { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<LauncherCatalogSnapshot> SecondResponse { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int RequestCount { get; private set; }

        public Task<LauncherCatalogSnapshot> GetCatalogAsync(
            CancellationToken cancellationToken = default)
        {
            RequestCount++;
            return RequestCount switch
            {
                1 => FirstResponse.Task,
                2 => SecondResponse.Task,
                _ => Task.FromResult(CreateCatalogSnapshot("latest-server", "最新目录"))
            };
        }
    }

    private sealed class StaticCatalogClient(LauncherCatalogSnapshot snapshot)
        : IServerCatalogClient
    {
        public Task<LauncherCatalogSnapshot> GetCatalogAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(snapshot);
    }

    private sealed class CacheThenLiveCatalogClient : IServerCatalogClient, ICatalogSourceState
    {
        private readonly LauncherCatalogSnapshot _snapshot;

        public CacheThenLiveCatalogClient(LauncherCatalogSnapshot snapshot)
        {
            _snapshot = snapshot;
        }

        public int RequestCount { get; private set; }

        public CatalogSource LastSource { get; private set; } = CatalogSource.BuiltIn;

        public Task<LauncherCatalogSnapshot> GetCatalogAsync(CancellationToken cancellationToken = default)
        {
            RequestCount++;
            LastSource = RequestCount == 1 ? CatalogSource.Cache : CatalogSource.Live;
            return Task.FromResult(_snapshot);
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
        public TaskCompletionSource<object?>? InteractiveSessionGate { get; init; }
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

        public async Task<MinecraftLaunchSession> RefreshMinecraftLaunchSessionAsync(
            CancellationToken cancellationToken = default)
        {
            InteractiveSessionRequestCount++;
            if (InteractiveSessionFailure is not null)
            {
                throw InteractiveSessionFailure;
            }

            if (InteractiveSessionGate is not null)
            {
                await InteractiveSessionGate.Task.WaitAsync(cancellationToken);
            }

            return new MinecraftLaunchSession(
                "HechaoTester",
                MinecraftUuid,
                "minecraft-access-token",
                DateTimeOffset.UtcNow.AddMinutes(30),
                "123456789");
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
        public bool HasPreviousInstallFailureResult { get; init; }
        public int CheckFailuresRemaining { get; set; }
        public int CheckRequestCount { get; private set; }
        public int DownloadRequestCount { get; private set; }
        public Queue<Task<LauncherUpdatePlan?>> CheckResponses { get; } = [];

        public Task<LauncherUpdatePlan?> CheckAsync(
            CancellationToken cancellationToken = default)
        {
            CheckRequestCount++;
            if (CheckFailuresRemaining > 0)
            {
                CheckFailuresRemaining--;
                return Task.FromException<LauncherUpdatePlan?>(
                    new HttpRequestException("transient"));
            }

            if (CheckResponses.TryDequeue(out var response))
            {
                return response;
            }

            return Task.FromResult(Plan);
        }

        public bool HasPreviousInstallFailure(LauncherUpdatePlan plan) =>
            HasPreviousInstallFailureResult;

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
        public string? LastCapturedDataRoot { get; private set; }
        public string? LastImportedDataRoot { get; private set; }
        public TaskCompletionSource<object?>? ImportStarted { get; set; }
        public TaskCompletionSource<object?>? ImportRelease { get; set; }

        public async Task ImportLatestAsync(
            string dataRoot,
            CancellationToken cancellationToken = default)
        {
            LastImportedDataRoot = dataRoot;
            OperationLog?.Add("settings:import");
            ImportStarted?.TrySetResult(null);
            if (ImportRelease is not null)
            {
                await ImportRelease.Task.WaitAsync(cancellationToken);
            }
        }

        public Task CaptureProfileAsync(
            string dataRoot,
            string profileId,
            CancellationToken cancellationToken = default)
        {
            LastCapturedDataRoot = dataRoot;
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
        public LocalProfileState LocalState { get; set; } = LocalProfileState.Ready;
        public Dictionary<string, LocalProfileState> LocalStates { get; } = [];
        public TaskCompletionSource<object?> InstallStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<object?>? InstallRelease { get; init; }
        public Func<
            ClientProfileSummary,
            string,
            CancellationToken,
            Task<LocalProfileState>>? LocalStateHandler { get; init; }
        public int LocalStateRequestCount { get; private set; }
        public int InstallRequestCount { get; private set; }
        public int RollbackRequestCount { get; private set; }
        public int DeleteRequestCount { get; private set; }
        public List<string>? OperationLog { get; init; }
        public ClientInstallationOptions? LastInstallOptions { get; private set; }
        public string? LastInstalledProfileId { get; private set; }
        public string? LastDeleteDataRoot { get; private set; }

        public Task<LocalProfileState> GetLocalStateAsync(
            ClientProfileSummary profile,
            string dataRoot,
            CancellationToken cancellationToken = default)
        {
            LocalStateRequestCount++;
            if (LocalStateHandler is not null)
            {
                return LocalStateHandler(profile, dataRoot, cancellationToken);
            }

            return Task.FromResult(LocalStates.GetValueOrDefault(profile.Id, LocalState));
        }

        public Task<InstalledProfileState?> GetRollbackCandidateAsync(
            ClientProfileSummary profile,
            string dataRoot,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(RollbackCandidate);

        public async Task InstallAsync(
            ClientProfileSummary profile,
            ClientInstallationOptions options,
            IProgress<ClientInstallProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            InstallRequestCount++;
            LastInstallOptions = options;
            LastInstalledProfileId = profile.Id;
            progress?.Report(new ClientInstallProgress(
                ClientInstallPhase.Checking,
                0,
                string.Empty,
                0,
                profile.DownloadBytes));
            InstallStarted.TrySetResult(null);
            if (InstallRelease is not null)
            {
                await InstallRelease.Task.WaitAsync(cancellationToken);
            }

            LocalStates[profile.Id] = LocalProfileState.Ready;
            progress?.Report(new ClientInstallProgress(
                ClientInstallPhase.Complete,
                100,
                string.Empty,
                profile.DownloadBytes,
                profile.DownloadBytes));
        }

        public Task<bool> DeleteAsync(
            ClientProfileSummary profile,
            string dataRoot,
            CancellationToken cancellationToken = default)
        {
            DeleteRequestCount++;
            LastDeleteDataRoot = dataRoot;
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
        public string? RunningDataRoot { get; set; }
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
                    DateTimeOffset.UtcNow.AddMinutes(-1),
                    RunningDataRoot)
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
            MinecraftProcessExitKind exitKind = MinecraftProcessExitKind.Natural,
            string? dataRoot = null)
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
                    exitKind,
                    dataRoot));
        }
    }

    private sealed class NeverCompletingTelemetryService : ILauncherTelemetryService
    {
        private readonly TaskCompletionSource<object?> _neverCompletes = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public int RecordRequestCount { get; private set; }

        public Task RecordAsync(
            LauncherTelemetryEventType type,
            LauncherTelemetryOutcome outcome,
            LauncherTelemetryFailureCode failureCode = LauncherTelemetryFailureCode.None,
            string? profileId = null,
            string? profileVersion = null,
            TimeSpan? duration = null,
            long? bytesTransferred = null)
        {
            RecordRequestCount++;
            return _neverCompletes.Task;
        }

        public void TryFlush()
        {
        }
    }

    private sealed class StubDownloadHistoryStore : IDownloadHistoryStore
    {
        public IReadOnlyList<DownloadHistoryRecord> Load() => [];

        public void Save(IEnumerable<DownloadHistoryRecord> records)
        {
        }
    }

    private sealed class ThrowingDownloadHistoryStore : IDownloadHistoryStore
    {
        public IReadOnlyList<DownloadHistoryRecord> Load() => [];

        public void Save(IEnumerable<DownloadHistoryRecord> records) =>
            throw new UnauthorizedAccessException("read only");
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

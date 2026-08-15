using Hechao.Contracts;
using Hechao.Launcher.Services;
using Hechao.Launcher.Mac.ViewModels;

namespace Hechao.Launcher.Mac.Services;

internal static class LauncherBootstrap
{
    public static LauncherMacViewModel Create()
    {
        var settingsStore = new JsonLauncherSettingsStore();
        var settings = settingsStore.Load();
        var useSystemProxy = settings.UseSystemProxy;
        ISecureSessionStore sessionStore = OperatingSystem.IsMacOS()
            ? new MacKeychainSessionStore()
            : new DpapiSessionStore();
        var apiClient = LauncherApiClient.CreateDefault(
            sessionStore,
            useSystemProxy);
        var authentication = new MicrosoftMinecraftAuthenticationService(
            apiClient,
            ForumRegistrationClient.CreateDefault(useSystemProxy),
            XboxMinecraftAuthenticationClient.CreateDefault(useSystemProxy),
            LauncherIdentityConfiguration.MicrosoftClientId);
        var catalog = HttpServerCatalogClient.CreateDefault(
            new DemoServerCatalogClient(),
            apiClient);

        return new LauncherMacViewModel(
            authentication,
            catalog,
            settingsStore,
            ClientInstallationService.CreateDefault(apiClient, useSystemProxy),
            MinecraftGameLauncherService.CreateDefault(
                LauncherIdentityConfiguration.MicrosoftClientId,
                useSystemProxy),
            new JsonDownloadHistoryStore(),
            new JsonGameDiagnosticsService(),
            new PlayerGameSettingsService(),
            MinecraftSkinService.CreateDefault(useSystemProxy));
    }
}

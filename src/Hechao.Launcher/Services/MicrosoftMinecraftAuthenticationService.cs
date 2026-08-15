using System.IO;
using Hechao.Contracts;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;

namespace Hechao.Launcher.Services;

public interface ILauncherAuthenticationService
{
    HechaoAccount? CurrentAccount { get; }
    AuthenticatedPlayer? CurrentPlayer { get; }
    Task<HechaoAccount?> TryRestoreAsync(CancellationToken cancellationToken = default);
    Task SendRegistrationCodeAsync(
        string email,
        CancellationToken cancellationToken = default);
    Task<HechaoAccount> RegisterAsync(
        string username,
        string displayName,
        string password,
        string email,
        string code,
        CancellationToken cancellationToken = default);
    Task<HechaoAccount> LoginAsync(
        string usernameOrEmail,
        string password,
        CancellationToken cancellationToken = default);
    Task<HechaoAccount> LinkMinecraftAsync(CancellationToken cancellationToken = default);
    Task UnlinkMinecraftAsync(
        string currentPassword,
        CancellationToken cancellationToken = default);
    Task<MinecraftLaunchSession> GetMinecraftLaunchSessionAsync(
        CancellationToken cancellationToken = default);
    Task<MinecraftLaunchSession> RefreshMinecraftLaunchSessionAsync(
        CancellationToken cancellationToken = default);
    Task<VelocityLaunchGrantResponse> PrepareVelocityLaunchAsync(
        string serverId,
        CancellationToken cancellationToken = default);
    Task<AdminBrowserTicketResponse> CreateAdminBrowserTicketAsync(
        CancellationToken cancellationToken = default);
    Task LogoutAsync(CancellationToken cancellationToken = default);
    Task<SessionRevocationResponse> LogoutAllDevicesAsync(
        CancellationToken cancellationToken = default);
}

public sealed class MicrosoftMinecraftAuthenticationService : ILauncherAuthenticationService
{
    private static readonly string[] XboxScopes = ["XboxLive.signin", "XboxLive.offline_access"];

    private readonly LauncherApiClient _apiClient;
    private readonly ForumRegistrationClient _forumRegistrationClient;
    private readonly XboxMinecraftAuthenticationClient _minecraftAuthenticationClient;
    private readonly string? _microsoftClientId;
    private readonly SemaphoreSlim _clientInitializationGate = new(1, 1);
    private IPublicClientApplication? _microsoftClient;
    private MsalCacheHelper? _cacheHelper;
    private MinecraftLaunchSession? _cachedMinecraftLaunchSession;

    public MicrosoftMinecraftAuthenticationService(
        LauncherApiClient apiClient,
        ForumRegistrationClient forumRegistrationClient,
        XboxMinecraftAuthenticationClient minecraftAuthenticationClient,
        string? microsoftClientId)
    {
        _apiClient = apiClient;
        _forumRegistrationClient = forumRegistrationClient;
        _minecraftAuthenticationClient = minecraftAuthenticationClient;
        _microsoftClientId = microsoftClientId;
    }

    public HechaoAccount? CurrentAccount => _apiClient.CurrentAccount;
    public AuthenticatedPlayer? CurrentPlayer => _apiClient.CurrentPlayer;

    public Task<HechaoAccount?> TryRestoreAsync(CancellationToken cancellationToken = default)
    {
        return _apiClient.TryRestoreSessionAsync(cancellationToken);
    }

    public Task SendRegistrationCodeAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        return _forumRegistrationClient.SendRegistrationCodeAsync(
            email,
            cancellationToken);
    }

    public async Task<HechaoAccount> RegisterAsync(
        string username,
        string displayName,
        string password,
        string email,
        string code,
        CancellationToken cancellationToken = default)
    {
        await _forumRegistrationClient.RegisterAsync(
            username,
            displayName,
            email,
            password,
            code,
            cancellationToken);

        try
        {
            return await _apiClient.LoginAccountAsync(
                email,
                password,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new RegistrationLoginFailedException(exception);
        }
    }

    public Task<HechaoAccount> LoginAsync(
        string usernameOrEmail,
        string password,
        CancellationToken cancellationToken = default)
    {
        return _apiClient.LoginAccountAsync(
            usernameOrEmail,
            password,
            cancellationToken);
    }

    public async Task<HechaoAccount> LinkMinecraftAsync(
        CancellationToken cancellationToken = default)
    {
        _ = CurrentAccount ?? throw new LauncherAuthenticationRequiredException();
        var microsoftResult = await AcquireMicrosoftTokenInteractiveAsync(
            cancellationToken);

        var minecraftSession = await _minecraftAuthenticationClient.AuthenticateAsync(
            microsoftResult.AccessToken,
            cancellationToken);
        var account = await _apiClient.LinkMinecraftIdentityAsync(
            minecraftSession.AccessToken,
            cancellationToken);
        _cachedMinecraftLaunchSession = CreateLaunchSession(account, minecraftSession);
        return account;
    }

    private async Task<AuthenticationResult> AcquireMicrosoftTokenInteractiveAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var client = await GetMicrosoftClientAsync(cancellationToken);
            return await client
                .AcquireTokenInteractive(XboxScopes)
                .WithUseEmbeddedWebView(false)
                .WithSystemWebViewOptions(MicrosoftBrowserCompletionPage.CreateOptions())
                .WithPrompt(Prompt.SelectAccount)
                .ExecuteAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw new MicrosoftSignInCanceledException();
        }
        catch (MsalClientException exception) when (
            exception.ErrorCode == MsalError.AuthenticationCanceledError)
        {
            throw new MicrosoftSignInCanceledException();
        }
        catch (Exception exception) when (
            exception is MsalException or IOException or UnauthorizedAccessException or
                InvalidOperationException)
        {
            throw new MicrosoftSignInFailedException(exception);
        }
    }

    public async Task UnlinkMinecraftAsync(
        string currentPassword,
        CancellationToken cancellationToken = default)
    {
        _ = CurrentAccount ?? throw new LauncherAuthenticationRequiredException();
        await _apiClient.UnlinkMinecraftIdentityAsync(
            currentPassword,
            cancellationToken);
        _cachedMinecraftLaunchSession = null;
        await ClearMicrosoftAccountsAsync();
    }

    public async Task<MinecraftLaunchSession> GetMinecraftLaunchSessionAsync(
        CancellationToken cancellationToken = default)
    {
        var currentAccount = CurrentAccount ?? throw new LauncherAuthenticationRequiredException();
        if (currentAccount.MinecraftUuid is not { } linkedMinecraftUuid ||
            string.IsNullOrWhiteSpace(currentAccount.MinecraftName))
        {
            throw new MinecraftIdentityLinkRequiredException();
        }

        if (_cachedMinecraftLaunchSession is { } cached &&
            cached.MinecraftUuid == linkedMinecraftUuid &&
            cached.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(2))
        {
            return cached;
        }

        var client = await GetMicrosoftClientAsync(cancellationToken);
        var accounts = (await client.GetAccountsAsync()).ToArray();
        MinecraftSignInException? lastMinecraftFailure = null;
        foreach (var account in accounts)
        {
            AuthenticationResult microsoftResult;
            try
            {
                microsoftResult = await client
                    .AcquireTokenSilent(XboxScopes, account)
                    .ExecuteAsync(cancellationToken);
            }
            catch (MsalUiRequiredException)
            {
                continue;
            }
            catch (MsalException)
            {
                throw new MicrosoftSignInFailedException();
            }

            try
            {
                var minecraftSession = await _minecraftAuthenticationClient.AuthenticateAsync(
                    microsoftResult.AccessToken,
                    cancellationToken);
                var profile = await _minecraftAuthenticationClient.GetProfileAsync(
                    minecraftSession.AccessToken,
                    cancellationToken);
                if (profile.MinecraftUuid != linkedMinecraftUuid)
                {
                    continue;
                }

                _cachedMinecraftLaunchSession = new MinecraftLaunchSession(
                    profile.MinecraftName,
                    profile.MinecraftUuid,
                    minecraftSession.AccessToken,
                    minecraftSession.ExpiresAt,
                    minecraftSession.Xuid);
                return _cachedMinecraftLaunchSession;
            }
            catch (MinecraftSignInException exception)
            {
                if (exception.Failure is
                    MinecraftSignInFailure.ApplicationNotApproved or
                    MinecraftSignInFailure.ServiceUnavailable)
                {
                    throw;
                }

                lastMinecraftFailure = exception;
            }
        }

        if (accounts.Length == 1 && lastMinecraftFailure is not null)
        {
            throw lastMinecraftFailure;
        }

        throw new MicrosoftReauthenticationRequiredException();
    }

    public async Task<MinecraftLaunchSession> RefreshMinecraftLaunchSessionAsync(
        CancellationToken cancellationToken = default)
    {
        var currentAccount = CurrentAccount ?? throw new LauncherAuthenticationRequiredException();
        if (currentAccount.MinecraftUuid is not { } linkedMinecraftUuid ||
            string.IsNullOrWhiteSpace(currentAccount.MinecraftName))
        {
            throw new MinecraftIdentityLinkRequiredException();
        }

        var microsoftResult = await AcquireMicrosoftTokenInteractiveAsync(cancellationToken);
        var minecraftSession = await _minecraftAuthenticationClient.AuthenticateAsync(
            microsoftResult.AccessToken,
            cancellationToken);
        var profile = await _minecraftAuthenticationClient.GetProfileAsync(
            minecraftSession.AccessToken,
            cancellationToken);
        if (profile.MinecraftUuid != linkedMinecraftUuid)
        {
            throw new MicrosoftAccountMismatchException(
                currentAccount.MinecraftName,
                profile.MinecraftName);
        }

        _cachedMinecraftLaunchSession = new MinecraftLaunchSession(
            profile.MinecraftName,
            profile.MinecraftUuid,
            minecraftSession.AccessToken,
            minecraftSession.ExpiresAt,
            minecraftSession.Xuid);
        return _cachedMinecraftLaunchSession;
    }

    public Task<VelocityLaunchGrantResponse> PrepareVelocityLaunchAsync(
        string serverId,
        CancellationToken cancellationToken = default)
    {
        return _apiClient.CreateVelocityLaunchGrantAsync(serverId, cancellationToken);
    }

    public Task<AdminBrowserTicketResponse> CreateAdminBrowserTicketAsync(
        CancellationToken cancellationToken = default)
    {
        return _apiClient.CreateAdminBrowserTicketAsync(cancellationToken);
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        _cachedMinecraftLaunchSession = null;
        await _apiClient.LogoutAsync(cancellationToken);
        await ClearMicrosoftAccountsAsync();
    }

    public async Task<SessionRevocationResponse> LogoutAllDevicesAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await _apiClient.LogoutAllSessionsAsync(cancellationToken);
        _cachedMinecraftLaunchSession = null;
        await ClearMicrosoftAccountsAsync();
        return response;
    }

    private async Task<IPublicClientApplication> GetMicrosoftClientAsync(CancellationToken cancellationToken)
    {
        if (_microsoftClient is not null)
        {
            return _microsoftClient;
        }

        if (!Guid.TryParse(_microsoftClientId, out _))
        {
            throw new MicrosoftAuthenticationNotConfiguredException();
        }

        await _clientInitializationGate.WaitAsync(cancellationToken);
        try
        {
            if (_microsoftClient is not null)
            {
                return _microsoftClient;
            }

            var client = PublicClientApplicationBuilder
                .Create(_microsoftClientId)
                .WithAuthority("https://login.microsoftonline.com/consumers")
                .WithRedirectUri("http://localhost")
                .Build();

            var applicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var cacheDirectory = Path.Combine(applicationData, "Hechao", "Launcher", "Identity");
            Directory.CreateDirectory(cacheDirectory);
            var storageBuilder = new StorageCreationPropertiesBuilder(
                "msal-cache.bin",
                cacheDirectory);
            if (OperatingSystem.IsMacOS())
            {
                storageBuilder.WithMacKeyChain(
                    "world.hechao.launcher.identity",
                    "microsoft-token-cache");
            }

            var storageProperties = storageBuilder.Build();
            _cacheHelper = await MsalCacheHelper.CreateAsync(storageProperties);
            _cacheHelper.RegisterCache(client.UserTokenCache);
            _microsoftClient = client;
            return client;
        }
        finally
        {
            _clientInitializationGate.Release();
        }
    }

    private static MinecraftLaunchSession CreateLaunchSession(
        HechaoAccount account,
        MinecraftAccessSession minecraftSession)
    {
        if (account.MinecraftUuid is not { } minecraftUuid ||
            string.IsNullOrWhiteSpace(account.MinecraftName))
        {
            throw new MinecraftIdentityLinkRequiredException();
        }

        return new MinecraftLaunchSession(
            account.MinecraftName,
            minecraftUuid,
            minecraftSession.AccessToken,
            minecraftSession.ExpiresAt,
            minecraftSession.Xuid);
    }

    private async Task ClearMicrosoftAccountsAsync()
    {
        if (_microsoftClient is null)
        {
            return;
        }

        try
        {
            var accounts = await _microsoftClient.GetAccountsAsync();
            foreach (var account in accounts)
            {
                await _microsoftClient.RemoveAsync(account);
            }
        }
        catch (Exception exception) when (
            exception is MsalException or IOException or UnauthorizedAccessException)
        {
            // The Hechao session is already revoked; stale Microsoft cache can be
            // retried or replaced on the next interactive authentication.
        }
    }
}

public sealed class RegistrationLoginFailedException : Exception
{
    public RegistrationLoginFailedException(Exception innerException)
        : base(
            "The Hechao account was created, but automatic launcher login failed.",
            innerException)
    {
    }
}

public sealed record MinecraftLaunchSession(
    string Username,
    Guid MinecraftUuid,
    string AccessToken,
    DateTimeOffset ExpiresAt,
    string? Xuid);

public sealed class MicrosoftAuthenticationNotConfiguredException : Exception;
public sealed class MicrosoftSignInCanceledException : Exception;
public sealed class MicrosoftSignInFailedException(Exception? innerException = null)
    : Exception("Microsoft authentication did not complete.", innerException);
public sealed class MicrosoftReauthenticationRequiredException : Exception;
public sealed class MicrosoftAccountMismatchException(
    string linkedMinecraftName,
    string authenticatedMinecraftName)
    : Exception("The authenticated Microsoft account does not match the linked Minecraft identity.")
{
    public string LinkedMinecraftName { get; } = linkedMinecraftName;
    public string AuthenticatedMinecraftName { get; } = authenticatedMinecraftName;
}
public sealed class MinecraftIdentityLinkRequiredException : Exception;

internal static class MicrosoftBrowserCompletionPage
{
    internal static SystemWebViewOptions CreateOptions() => new()
    {
        HtmlMessageSuccess = CreatePage(
            "认证完成",
            "Microsoft 正版身份已通过验证。",
            "现在可以关闭此标签页并返回赫朝启动器，后续步骤会自动完成。",
            isError: false),
        HtmlMessageError = CreatePage(
            "认证未完成",
            "Microsoft 没有完成这次登录。",
            "请关闭此标签页并返回赫朝启动器，然后点击“绑定 Microsoft 正版身份”重试。",
            isError: true)
    };

    private static string CreatePage(
        string title,
        string heading,
        string description,
        bool isError)
    {
        var accent = isError ? "#9f1d18" : "#23845b";
        var status = isError ? "需要重试" : "验证成功";
        return $"""
            <!doctype html>
            <html lang="zh-CN">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width,initial-scale=1">
              <title>{title} - 赫朝启动器</title>
            </head>
            <body style="margin:0;background:#f3f4f1;color:#171816;font-family:'PingFang SC','Microsoft YaHei UI','Segoe UI',sans-serif;">
              <main style="min-height:100vh;display:flex;align-items:center;justify-content:center;padding:24px;box-sizing:border-box;">
                <section style="width:min(520px,100%);background:#fff;border:1px solid #d9dcd6;border-radius:8px;padding:38px;box-sizing:border-box;box-shadow:0 14px 34px rgba(23,24,22,.08);">
                  <div style="display:flex;align-items:center;gap:12px;margin-bottom:30px;">
                    <div style="width:42px;height:42px;border-radius:6px;background:#b6231c;color:#fff;display:flex;align-items:center;justify-content:center;font-size:26px;font-weight:800;">C</div>
                    <div>
                      <div style="font-size:19px;font-weight:800;">赫朝启动器</div>
                      <div style="font-size:12px;color:#727770;margin-top:2px;">MICROSOFT / MINECRAFT</div>
                    </div>
                  </div>
                  <div style="display:inline-block;padding:6px 10px;border:1px solid {accent};color:{accent};font-size:12px;font-weight:700;margin-bottom:18px;">{status}</div>
                  <h1 style="font-size:28px;line-height:1.35;margin:0 0 12px;font-weight:800;">{heading}</h1>
                  <p style="font-size:15px;line-height:1.8;color:#5e635d;margin:0;">{description}</p>
                  <div style="height:1px;background:#e4e6e1;margin:28px 0 18px;"></div>
                  <p style="font-size:12px;line-height:1.7;color:#878c85;margin:0;">出于安全考虑，请勿分享此页面或地址栏内容。</p>
                </section>
              </main>
            </body>
            </html>
            """;
    }
}

using System.IO;
using Hechao.Contracts;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;

namespace Hechao.Launcher.Services;

public interface ILauncherAuthenticationService
{
    AuthenticatedPlayer? CurrentPlayer { get; }
    Task<AuthenticatedPlayer?> TryRestoreAsync(CancellationToken cancellationToken = default);
    Task<AuthenticatedPlayer> SignInAsync(CancellationToken cancellationToken = default);
    Task<MinecraftLaunchSession> GetMinecraftLaunchSessionAsync(
        CancellationToken cancellationToken = default);
    Task<VelocityLaunchGrantResponse> PrepareVelocityLaunchAsync(
        string serverId,
        CancellationToken cancellationToken = default);
    Task LogoutAsync(CancellationToken cancellationToken = default);
}

public sealed class MicrosoftMinecraftAuthenticationService : ILauncherAuthenticationService
{
    private static readonly string[] XboxScopes = ["XboxLive.signin", "XboxLive.offline_access"];

    private readonly LauncherApiClient _apiClient;
    private readonly XboxMinecraftAuthenticationClient _minecraftAuthenticationClient;
    private readonly string? _microsoftClientId;
    private readonly SemaphoreSlim _clientInitializationGate = new(1, 1);
    private IPublicClientApplication? _microsoftClient;
    private MsalCacheHelper? _cacheHelper;
    private MinecraftLaunchSession? _cachedMinecraftLaunchSession;

    public MicrosoftMinecraftAuthenticationService(
        LauncherApiClient apiClient,
        XboxMinecraftAuthenticationClient minecraftAuthenticationClient,
        string? microsoftClientId)
    {
        _apiClient = apiClient;
        _minecraftAuthenticationClient = minecraftAuthenticationClient;
        _microsoftClientId = microsoftClientId;
    }

    public AuthenticatedPlayer? CurrentPlayer => _apiClient.CurrentPlayer;

    public Task<AuthenticatedPlayer?> TryRestoreAsync(CancellationToken cancellationToken = default)
    {
        return _apiClient.TryRestoreSessionAsync(cancellationToken);
    }

    public async Task<AuthenticatedPlayer> SignInAsync(CancellationToken cancellationToken = default)
    {
        var client = await GetMicrosoftClientAsync(cancellationToken);
        AuthenticationResult microsoftResult;
        try
        {
            microsoftResult = await client
                .AcquireTokenInteractive(XboxScopes)
                .WithUseEmbeddedWebView(false)
                .WithPrompt(Prompt.SelectAccount)
                .ExecuteAsync(cancellationToken);
        }
        catch (MsalClientException exception) when (exception.ErrorCode == MsalError.AuthenticationCanceledError)
        {
            throw new MicrosoftSignInCanceledException();
        }
        catch (MsalException)
        {
            throw new MicrosoftSignInFailedException();
        }

        var minecraftSession = await _minecraftAuthenticationClient.AuthenticateAsync(
            microsoftResult.AccessToken,
            cancellationToken);
        var player = await _apiClient.ExchangeMinecraftSessionAsync(
            minecraftSession.AccessToken,
            cancellationToken);
        _cachedMinecraftLaunchSession = CreateLaunchSession(player, minecraftSession);
        return player;
    }

    public async Task<MinecraftLaunchSession> GetMinecraftLaunchSessionAsync(
        CancellationToken cancellationToken = default)
    {
        var currentPlayer = CurrentPlayer ?? throw new LauncherAuthenticationRequiredException();
        if (_cachedMinecraftLaunchSession is { } cached &&
            cached.MinecraftUuid == currentPlayer.MinecraftUuid &&
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
                if (profile.MinecraftUuid != currentPlayer.MinecraftUuid)
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

    public Task<VelocityLaunchGrantResponse> PrepareVelocityLaunchAsync(
        string serverId,
        CancellationToken cancellationToken = default)
    {
        return _apiClient.CreateVelocityLaunchGrantAsync(serverId, cancellationToken);
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        _cachedMinecraftLaunchSession = null;
        await _apiClient.LogoutAsync(cancellationToken);
        if (_microsoftClient is null)
        {
            return;
        }

        var accounts = await _microsoftClient.GetAccountsAsync();
        foreach (var account in accounts)
        {
            await _microsoftClient.RemoveAsync(account);
        }
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
            var storageProperties = new StorageCreationPropertiesBuilder("msal-cache.bin", cacheDirectory).Build();
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
        AuthenticatedPlayer player,
        MinecraftAccessSession minecraftSession)
    {
        return new MinecraftLaunchSession(
            player.MinecraftName,
            player.MinecraftUuid,
            minecraftSession.AccessToken,
            minecraftSession.ExpiresAt,
            minecraftSession.Xuid);
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
public sealed class MicrosoftSignInFailedException : Exception;
public sealed class MicrosoftReauthenticationRequiredException : Exception;

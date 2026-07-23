using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hechao.Contracts;
using Hechao.Distribution;

namespace Hechao.Launcher.Services;

public sealed class LauncherApiClient
{
    private const string DefaultApiBaseUrl = "https://launcher-api.hechao.world/";
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    private readonly HttpClient _httpClient;
    private readonly ISecureSessionStore _sessionStore;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private AuthSessionResponse? _session;

    internal LauncherApiClient(HttpClient httpClient, ISecureSessionStore sessionStore)
    {
        _httpClient = httpClient;
        _sessionStore = sessionStore;
    }

    public AuthenticatedPlayer? CurrentPlayer => _session?.Player;

    internal HttpMessageHandler CreateDownloadAuthorizationHandler(HttpMessageHandler innerHandler)
    {
        ArgumentNullException.ThrowIfNull(innerHandler);
        return new OriginBoundBearerTokenHandler(
            _httpClient.BaseAddress ?? throw new InvalidOperationException("The launcher API base URL is unavailable."),
            GetRequiredAccessTokenAsync)
        {
            InnerHandler = innerHandler
        };
    }

    public static LauncherApiClient CreateDefault(ISecureSessionStore? sessionStore = null)
    {
        var configuredBaseUrl = Environment.GetEnvironmentVariable("HECHAO_LAUNCHER_API_BASE_URL");
        var baseUri = new Uri(string.IsNullOrWhiteSpace(configuredBaseUrl) ? DefaultApiBaseUrl : configuredBaseUrl);
        if (baseUri.Scheme != Uri.UriSchemeHttps && !baseUri.IsLoopback)
        {
            throw new InvalidOperationException("The launcher API must use HTTPS unless it is a loopback test endpoint.");
        }

        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            PooledConnectionLifetime = TimeSpan.FromMinutes(10)
        };
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = baseUri,
            Timeout = TimeSpan.FromSeconds(12)
        };
        httpClient.DefaultRequestHeaders.UserAgent.Add(LauncherProductInfo.CreateUserAgent());
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return new LauncherApiClient(httpClient, sessionStore ?? new DpapiSessionStore());
    }

    public async Task<AuthenticatedPlayer?> TryRestoreSessionAsync(CancellationToken cancellationToken = default)
    {
        var storedSession = await _sessionStore.LoadAsync(cancellationToken);
        if (storedSession is null)
        {
            return null;
        }

        try
        {
            return await RefreshCoreAsync(storedSession.RefreshToken, cancellationToken)
                ? _session?.Player
                : null;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
        catch (LauncherApiException exception) when ((int)exception.StatusCode >= 500)
        {
            return null;
        }
    }

    public async Task<AuthenticatedPlayer> ExchangeMinecraftSessionAsync(
        string minecraftAccessToken,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            "v1/auth/minecraft/exchange",
            new MinecraftSessionExchangeRequest(minecraftAccessToken),
            SerializerOptions,
            cancellationToken);
        var session = await ReadRequiredAsync<AuthSessionResponse>(response, cancellationToken);
        await SetSessionAsync(session, cancellationToken);
        return session.Player;
    }

    public async Task<LauncherCatalogSnapshot> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        await EnsureFreshAccessTokenAsync(cancellationToken);
        using var firstResponse = await SendCatalogRequestAsync(cancellationToken);
        if (firstResponse.StatusCode != HttpStatusCode.Unauthorized || _session is null)
        {
            return await ReadRequiredAsync<LauncherCatalogSnapshot>(firstResponse, cancellationToken);
        }

        if (!await RefreshCoreAsync(_session.RefreshToken, cancellationToken))
        {
            throw new LauncherAuthenticationRequiredException();
        }

        using var retryResponse = await SendCatalogRequestAsync(cancellationToken);
        return await ReadRequiredAsync<LauncherCatalogSnapshot>(retryResponse, cancellationToken);
    }

    public async Task<byte[]> GetProfileManifestAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        await EnsureFreshAccessTokenAsync(cancellationToken);
        using var firstResponse = await SendProfileManifestRequestAsync(profileId, cancellationToken);
        if (firstResponse.StatusCode != HttpStatusCode.Unauthorized || _session is null)
        {
            return await ReadRequiredBytesAsync(firstResponse, cancellationToken);
        }

        if (!await RefreshCoreAsync(_session.RefreshToken, cancellationToken))
        {
            throw new LauncherAuthenticationRequiredException();
        }

        using var retryResponse = await SendProfileManifestRequestAsync(profileId, cancellationToken);
        return await ReadRequiredBytesAsync(retryResponse, cancellationToken);
    }

    public async Task<VelocityLaunchGrantResponse> CreateVelocityLaunchGrantAsync(
        string serverId,
        CancellationToken cancellationToken = default)
    {
        var accessToken = await GetRequiredAccessTokenAsync(cancellationToken);
        using var firstResponse = await SendVelocityLaunchGrantRequestAsync(
            serverId,
            accessToken,
            cancellationToken);
        if (firstResponse.StatusCode != HttpStatusCode.Unauthorized || _session is null)
        {
            return await ReadRequiredAsync<VelocityLaunchGrantResponse>(firstResponse, cancellationToken);
        }

        if (!await RefreshCoreAsync(_session.RefreshToken, cancellationToken))
        {
            throw new LauncherAuthenticationRequiredException();
        }

        accessToken = await GetRequiredAccessTokenAsync(cancellationToken);
        using var retryResponse = await SendVelocityLaunchGrantRequestAsync(
            serverId,
            accessToken,
            cancellationToken);
        return await ReadRequiredAsync<VelocityLaunchGrantResponse>(retryResponse, cancellationToken);
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        var accessToken = _session?.AccessToken;
        _session = null;
        await _sessionStore.ClearAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "v1/auth/logout");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
        }
    }

    private async Task EnsureFreshAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_session is null || _session.AccessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
        {
            return;
        }

        if (!await RefreshCoreAsync(_session.RefreshToken, cancellationToken))
        {
            throw new LauncherAuthenticationRequiredException();
        }
    }

    private async ValueTask<string> GetRequiredAccessTokenAsync(CancellationToken cancellationToken)
    {
        await EnsureFreshAccessTokenAsync(cancellationToken);
        return _session?.AccessToken ?? throw new LauncherAuthenticationRequiredException();
    }

    private async Task<bool> RefreshCoreAsync(string refreshToken, CancellationToken cancellationToken)
    {
        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            if (_session is not null &&
                _session.RefreshToken != refreshToken &&
                _session.AccessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
            {
                return true;
            }

            using var response = await _httpClient.PostAsJsonAsync(
                "v1/auth/refresh",
                new RefreshSessionRequest(refreshToken),
                SerializerOptions,
                cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                _session = null;
                await _sessionStore.ClearAsync(cancellationToken);
                return false;
            }

            var session = await ReadRequiredAsync<AuthSessionResponse>(response, cancellationToken);
            await SetSessionAsync(session, cancellationToken);
            return true;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task<HttpResponseMessage> SendCatalogRequestAsync(CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "v1/catalog");
        if (_session is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _session.AccessToken);
        }

        try
        {
            return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        finally
        {
            request.Dispose();
        }
    }

    private async Task<HttpResponseMessage> SendProfileManifestRequestAsync(
        string profileId,
        CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"v1/profiles/{Uri.EscapeDataString(profileId)}/manifest");
        if (_session is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _session.AccessToken);
        }

        try
        {
            return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        finally
        {
            request.Dispose();
        }
    }

    private async Task<HttpResponseMessage> SendVelocityLaunchGrantRequestAsync(
        string serverId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/velocity/launch-grants")
        {
            Content = JsonContent.Create(
                new VelocityLaunchGrantRequest(serverId),
                options: SerializerOptions)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
    }

    private async Task SetSessionAsync(AuthSessionResponse session, CancellationToken cancellationToken)
    {
        _session = session;
        await _sessionStore.SaveAsync(
            new StoredLauncherSession(session.RefreshToken, session.Player),
            cancellationToken);
    }

    private static async Task<T> ReadRequiredAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                throw new LauncherAuthenticationRequiredException();
            }

            var detail = await TryReadProblemDetailAsync(response, cancellationToken);
            throw new LauncherApiException(response.StatusCode, detail);
        }

        return await response.Content.ReadFromJsonAsync<T>(SerializerOptions, cancellationToken)
            ?? throw new InvalidDataException("The launcher API returned an empty response.");
    }

    private static async Task<byte[]> ReadRequiredBytesAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        const int maximumBytes = 8 * 1024 * 1024;
        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                throw new LauncherAuthenticationRequiredException();
            }

            var detail = await TryReadProblemDetailAsync(response, cancellationToken);
            throw new LauncherApiException(response.StatusCode, detail);
        }

        if (response.Content.Headers.ContentLength is <= 0 or > maximumBytes)
        {
            throw new InvalidDataException("The launcher API returned an invalid manifest size.");
        }

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var destination = new MemoryStream();
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var bytesRead = await source.ReadAsync(buffer, cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }

            if (destination.Length + bytesRead > maximumBytes)
            {
                throw new InvalidDataException("The launcher API returned an oversized manifest.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
        }

        if (destination.Length == 0)
        {
            throw new InvalidDataException("The launcher API returned an empty manifest.");
        }

        return destination.ToArray();
    }

    private static async Task<string?> TryReadProblemDetailAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            using var document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);
            if (document.RootElement.TryGetProperty("detail", out var detail))
            {
                return detail.GetString();
            }

            if (document.RootElement.TryGetProperty("title", out var title))
            {
                return title.GetString();
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}

public sealed class LauncherAuthenticationRequiredException : Exception;

public sealed class LauncherApiException(HttpStatusCode statusCode, string? apiDetail) : Exception(apiDetail)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public string? ApiDetail { get; } = apiDetail;
}

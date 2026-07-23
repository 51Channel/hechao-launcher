using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Hechao.Launcher.Services;

public sealed class XboxMinecraftAuthenticationClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;

    private XboxMinecraftAuthenticationClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public static XboxMinecraftAuthenticationClient CreateDefault()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            PooledConnectionLifetime = TimeSpan.FromMinutes(10)
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.Add(LauncherProductInfo.CreateUserAgent());
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return new XboxMinecraftAuthenticationClient(client);
    }

    public async Task<MinecraftAccessSession> AuthenticateAsync(
        string microsoftAccessToken,
        CancellationToken cancellationToken = default)
    {
        var userToken = await GetXboxUserTokenAsync(microsoftAccessToken, cancellationToken);
        var xstsToken = await GetMinecraftXstsTokenAsync(userToken.Token, cancellationToken);
        return await GetMinecraftAccessTokenAsync(
            xstsToken.UserHash,
            xstsToken.Token,
            xstsToken.Xuid,
            cancellationToken);
    }

    public async Task<MinecraftPlayerProfile> GetProfileAsync(
        string minecraftAccessToken,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "https://api.minecraftservices.com/minecraft/profile");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", minecraftAccessToken);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MinecraftSignInException(MinecraftSignInFailure.ServiceUnavailable);
        }
        catch (HttpRequestException)
        {
            throw new MinecraftSignInException(MinecraftSignInFailure.ServiceUnavailable);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new MinecraftSignInException(
                    (int)response.StatusCode >= 500
                        ? MinecraftSignInFailure.ServiceUnavailable
                        : MinecraftSignInFailure.SignInRejected);
            }

            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var document = await JsonDocument.ParseAsync(
                    stream,
                    cancellationToken: cancellationToken);
                var root = document.RootElement;
                var id = root.TryGetProperty("id", out var idProperty)
                    ? idProperty.GetString()
                    : null;
                var name = root.TryGetProperty("name", out var nameProperty)
                    ? nameProperty.GetString()
                    : null;
                if (!Guid.TryParseExact(id, "N", out var minecraftUuid) ||
                    string.IsNullOrWhiteSpace(name))
                {
                    throw new MinecraftSignInException(MinecraftSignInFailure.InvalidResponse);
                }

                return new MinecraftPlayerProfile(minecraftUuid, name);
            }
            catch (JsonException)
            {
                throw new MinecraftSignInException(MinecraftSignInFailure.InvalidResponse);
            }
        }
    }

    private async Task<XboxToken> GetXboxUserTokenAsync(
        string microsoftAccessToken,
        CancellationToken cancellationToken)
    {
        var body = new
        {
            Properties = new
            {
                AuthMethod = "RPS",
                SiteName = "user.auth.xboxlive.com",
                RpsTicket = "d=" + microsoftAccessToken
            },
            RelyingParty = "http://auth.xboxlive.com",
            TokenType = "JWT"
        };

        using var document = await PostJsonAsync(
            "https://user.auth.xboxlive.com/user/authenticate",
            body,
            AuthenticationStage.XboxUser,
            cancellationToken);
        return ReadXboxToken(document);
    }

    private async Task<XboxToken> GetMinecraftXstsTokenAsync(
        string xboxUserToken,
        CancellationToken cancellationToken)
    {
        var body = new
        {
            Properties = new
            {
                SandboxId = "RETAIL",
                UserTokens = new[] { xboxUserToken }
            },
            RelyingParty = "rp://api.minecraftservices.com/",
            TokenType = "JWT"
        };

        using var document = await PostJsonAsync(
            "https://xsts.auth.xboxlive.com/xsts/authorize",
            body,
            AuthenticationStage.Xsts,
            cancellationToken);
        return ReadXboxToken(document);
    }

    private async Task<MinecraftAccessSession> GetMinecraftAccessTokenAsync(
        string userHash,
        string xstsToken,
        string? xuid,
        CancellationToken cancellationToken)
    {
        var body = new { identityToken = $"XBL3.0 x={userHash};{xstsToken}" };
        using var document = await PostJsonAsync(
            "https://api.minecraftservices.com/authentication/login_with_xbox",
            body,
            AuthenticationStage.Minecraft,
            cancellationToken);

        var root = document.RootElement;
        if (!root.TryGetProperty("access_token", out var tokenProperty) ||
            !root.TryGetProperty("expires_in", out var expiresProperty) ||
            string.IsNullOrWhiteSpace(tokenProperty.GetString()) ||
            !expiresProperty.TryGetInt32(out var expiresIn) ||
            expiresIn <= 0)
        {
            throw new MinecraftSignInException(MinecraftSignInFailure.InvalidResponse);
        }

        return new MinecraftAccessSession(
            tokenProperty.GetString()!,
            DateTimeOffset.UtcNow.AddSeconds(expiresIn),
            xuid);
    }

    private async Task<JsonDocument> PostJsonAsync<T>(
        string requestUri,
        T body,
        AuthenticationStage stage,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsJsonAsync(
                requestUri,
                body,
                SerializerOptions,
                cancellationToken);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MinecraftSignInException(MinecraftSignInFailure.ServiceUnavailable);
        }
        catch (HttpRequestException)
        {
            throw new MinecraftSignInException(MinecraftSignInFailure.ServiceUnavailable);
        }

        using (response)
        {
            JsonDocument? document = null;
            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            }
            catch (JsonException)
            {
                if (response.IsSuccessStatusCode)
                {
                    throw new MinecraftSignInException(MinecraftSignInFailure.InvalidResponse);
                }
            }

            if (!response.IsSuccessStatusCode)
            {
                var failure = MapFailure(response.StatusCode, stage, document);
                document?.Dispose();
                throw new MinecraftSignInException(failure);
            }

            return document ?? throw new MinecraftSignInException(MinecraftSignInFailure.InvalidResponse);
        }
    }

    private static XboxToken ReadXboxToken(JsonDocument document)
    {
        var root = document.RootElement;
        if (!root.TryGetProperty("Token", out var tokenProperty) ||
            !root.TryGetProperty("DisplayClaims", out var displayClaims) ||
            !displayClaims.TryGetProperty("xui", out var xui) ||
            xui.ValueKind != JsonValueKind.Array ||
            xui.GetArrayLength() == 0 ||
            !xui[0].TryGetProperty("uhs", out var userHashProperty) ||
            string.IsNullOrWhiteSpace(tokenProperty.GetString()) ||
            string.IsNullOrWhiteSpace(userHashProperty.GetString()))
        {
            throw new MinecraftSignInException(MinecraftSignInFailure.InvalidResponse);
        }

        var xuid = xui[0].TryGetProperty("xid", out var xuidProperty) &&
                   xuidProperty.ValueKind == JsonValueKind.String
            ? xuidProperty.GetString()
            : null;
        return new XboxToken(tokenProperty.GetString()!, userHashProperty.GetString()!, xuid);
    }

    private static MinecraftSignInFailure MapFailure(
        HttpStatusCode statusCode,
        AuthenticationStage stage,
        JsonDocument? document)
    {
        if (stage == AuthenticationStage.Minecraft && statusCode == HttpStatusCode.Forbidden)
        {
            var message = TryReadString(document, "errorMessage");
            if (message?.Contains("Invalid app registration", StringComparison.OrdinalIgnoreCase) == true)
            {
                return MinecraftSignInFailure.ApplicationNotApproved;
            }
        }

        var xError = TryReadInt64(document, "XErr");
        return xError switch
        {
            2148916233 => MinecraftSignInFailure.XboxAccountRequired,
            2148916238 => MinecraftSignInFailure.FamilyRestriction,
            _ when (int)statusCode == 429 || (int)statusCode >= 500
                => MinecraftSignInFailure.ServiceUnavailable,
            _ => MinecraftSignInFailure.SignInRejected
        };
    }

    private static string? TryReadString(JsonDocument? document, string propertyName)
    {
        return document is not null &&
               document.RootElement.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static long? TryReadInt64(JsonDocument? document, string propertyName)
    {
        if (document is null || !document.RootElement.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out var numericValue))
        {
            return numericValue;
        }

        return property.ValueKind == JsonValueKind.String && long.TryParse(property.GetString(), out var stringValue)
            ? stringValue
            : null;
    }

    private sealed record XboxToken(string Token, string UserHash, string? Xuid);

    private enum AuthenticationStage
    {
        XboxUser,
        Xsts,
        Minecraft
    }
}

public sealed record MinecraftAccessSession(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    string? Xuid);

public sealed record MinecraftPlayerProfile(
    Guid MinecraftUuid,
    string MinecraftName);

public enum MinecraftSignInFailure
{
    SignInRejected,
    XboxAccountRequired,
    FamilyRestriction,
    ApplicationNotApproved,
    ServiceUnavailable,
    InvalidResponse
}

public sealed class MinecraftSignInException(MinecraftSignInFailure failure) : Exception
{
    public MinecraftSignInFailure Failure { get; } = failure;
}

using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Hechao.Launcher.Services;

public sealed class ForumRegistrationClient
{
    private const string DefaultForumBaseUrl = "https://hechao.world/";
    private readonly HttpClient _httpClient;

    internal ForumRegistrationClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public static ForumRegistrationClient CreateDefault()
    {
        var configuredBaseUrl = Environment.GetEnvironmentVariable("HECHAO_FORUM_BASE_URL");
        var baseUri = new Uri(
            string.IsNullOrWhiteSpace(configuredBaseUrl)
                ? DefaultForumBaseUrl
                : configuredBaseUrl);
        if (baseUri.Scheme != Uri.UriSchemeHttps && !baseUri.IsLoopback)
        {
            throw new InvalidOperationException(
                "The Hechao forum must use HTTPS unless it is a loopback test endpoint.");
        }

        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression =
                DecompressionMethods.GZip |
                DecompressionMethods.Deflate |
                DecompressionMethods.Brotli,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            UseCookies = false
        };
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = baseUri,
            Timeout = TimeSpan.FromSeconds(12)
        };
        httpClient.DefaultRequestHeaders.UserAgent.Add(LauncherProductInfo.CreateUserAgent());
        httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        return new ForumRegistrationClient(httpClient);
    }

    public async Task SendRegistrationCodeAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            "api/forum/send-code",
            new { email },
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task RegisterAsync(
        string username,
        string displayName,
        string email,
        string password,
        string code,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            "api/forum/register",
            new { username, displayName, email, password, code },
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string? detail = null;
        try
        {
            using var document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);
            if (document.RootElement.TryGetProperty("error", out var error))
            {
                detail = error.GetString();
            }
        }
        catch (JsonException)
        {
        }

        throw new ForumRegistrationException(
            response.StatusCode,
            detail ?? "赫朝社区暂时无法处理注册请求。");
    }
}

public sealed class ForumRegistrationException(
    HttpStatusCode statusCode,
    string detail) : Exception(detail)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public string Detail { get; } = detail;
}

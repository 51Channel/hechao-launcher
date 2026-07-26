namespace Hechao.Api.Authentication;

public sealed class ForumSessionRevocationOptions
{
    public const string SectionName = "ForumSessionRevocation";

    public bool Enabled { get; init; }

    public string BaseUrl { get; init; } = "http://127.0.0.1:3000/";

    public string InternalToken { get; init; } = string.Empty;

    public int DeliveryIntervalSeconds { get; init; } = 5;

    public int RequestTimeoutSeconds { get; init; } = 5;

    public int LeaseSeconds { get; init; } = 30;

    public int BatchSize { get; init; } = 20;

    public bool TryGetBaseUri(out Uri baseUri)
    {
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var parsed) ||
            parsed.Scheme != Uri.UriSchemeHttp ||
            !parsed.IsLoopback ||
            parsed.AbsolutePath != "/" ||
            !string.IsNullOrEmpty(parsed.Query) ||
            !string.IsNullOrEmpty(parsed.Fragment) ||
            !string.IsNullOrEmpty(parsed.UserInfo))
        {
            baseUri = null!;
            return false;
        }

        var builder = new UriBuilder(parsed)
        {
            Path = "/",
            Query = string.Empty,
            Fragment = string.Empty
        };
        baseUri = builder.Uri;
        return true;
    }

    public bool HasValidToken() =>
        InternalToken.Length is >= 32 and <= 256 &&
        !InternalToken.Any(char.IsWhiteSpace) &&
        !InternalToken.Any(char.IsControl);
}

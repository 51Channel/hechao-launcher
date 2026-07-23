namespace Hechao.Api.Admin;

public sealed class AdminWebOptions
{
    public const string SectionName = "AdminWeb";

    public bool Enabled { get; init; }
    public string PublicBaseUrl { get; init; } = "https://admin.hechao.world";
    public string DataProtectionKeyPath { get; init; } = string.Empty;
    public int TicketSeconds { get; init; } = 90;
    public int SessionMinutes { get; init; } = 30;
    public int EnrollmentMinutes { get; init; } = 10;
    public string TotpIssuer { get; init; } = "赫朝服务器";

    public bool TryGetPublicBaseUri(out Uri baseUri)
    {
        if (!Uri.TryCreate(PublicBaseUrl, UriKind.Absolute, out var parsed) ||
            (parsed.Scheme != Uri.UriSchemeHttps &&
             (parsed.Scheme != Uri.UriSchemeHttp || !parsed.IsLoopback)) ||
            !string.IsNullOrEmpty(parsed.UserInfo) ||
            parsed.AbsolutePath != "/" ||
            !string.IsNullOrEmpty(parsed.Query) ||
            !string.IsNullOrEmpty(parsed.Fragment))
        {
            baseUri = null!;
            return false;
        }

        baseUri = new Uri(parsed.GetLeftPart(UriPartial.Authority));
        return true;
    }

    public bool IsExpectedHost(HostString requestHost)
    {
        return TryGetPublicBaseUri(out var baseUri) &&
               string.Equals(
                   requestHost.Value,
                   baseUri.Authority,
                   StringComparison.OrdinalIgnoreCase);
    }
}

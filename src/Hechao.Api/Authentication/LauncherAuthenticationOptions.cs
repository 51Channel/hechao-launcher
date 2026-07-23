namespace Hechao.Api.Authentication;

public sealed class LauncherAuthenticationOptions
{
    public const string SectionName = "Authentication";

    public bool EnforceCatalogAuthentication { get; init; }
    public int AccessTokenMinutes { get; init; } = 15;
    public int RefreshTokenDays { get; init; } = 30;
    public string InternalSyncTokenSha256 { get; init; } = string.Empty;
}

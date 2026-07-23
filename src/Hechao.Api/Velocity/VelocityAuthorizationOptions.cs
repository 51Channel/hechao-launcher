namespace Hechao.Api.Velocity;

public sealed class VelocityAuthorizationOptions
{
    public const string SectionName = "VelocityAuthorization";

    public string InternalTokenSha256 { get; init; } = string.Empty;
    public int LaunchGrantMinutes { get; init; } = 10;
    public int MaximumLuckPermsAgeMinutes { get; init; } = 20;
    public bool RequireGrantIpMatch { get; init; }
}

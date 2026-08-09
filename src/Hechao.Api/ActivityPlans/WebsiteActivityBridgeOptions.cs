namespace Hechao.Api.ActivityPlans;

public sealed class WebsiteActivityBridgeOptions
{
    public const string SectionName = "WebsiteActivityBridge";

    public string InternalTokenSha256 { get; init; } = string.Empty;

    public Guid ActorUserId { get; init; }
}

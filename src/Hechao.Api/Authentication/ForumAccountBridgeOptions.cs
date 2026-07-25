namespace Hechao.Api.Authentication;

public sealed class ForumAccountBridgeOptions
{
    public const string SectionName = "ForumAccountBridge";

    public string InternalTokenSha256 { get; init; } = string.Empty;

    public bool AllowLegacyImport { get; init; }
}

namespace Hechao.Launcher.Services;

public static class LauncherIdentityConfiguration
{
    // Public OAuth application identifier. This is not a client secret.
    private const string RegisteredMicrosoftClientId = "8b8c209c-d390-4d8d-af35-5d4981852cab";

    public static string? MicrosoftClientId
    {
        get
        {
            var environmentValue = Environment.GetEnvironmentVariable("HECHAO_MICROSOFT_CLIENT_ID");
            return string.IsNullOrWhiteSpace(environmentValue)
                ? NullIfEmpty(RegisteredMicrosoftClientId)
                : environmentValue.Trim();
        }
    }

    private static string? NullIfEmpty(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}

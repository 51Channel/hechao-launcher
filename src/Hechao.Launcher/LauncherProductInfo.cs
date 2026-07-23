using System.Net.Http.Headers;

namespace Hechao.Launcher;

internal static class LauncherProductInfo
{
    public const string ProductName = "Hechao.Launcher";

    public static string Version { get; } =
        typeof(LauncherProductInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public static ProductInfoHeaderValue CreateUserAgent() =>
        new(ProductName, Version);
}

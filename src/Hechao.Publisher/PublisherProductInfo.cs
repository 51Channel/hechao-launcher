internal static class PublisherProductInfo
{
    public const string ProductName = "Hechao.Publisher";

    public static string Version { get; } =
        typeof(PublisherProductInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public static string UserAgent => $"{ProductName}/{Version}";
}

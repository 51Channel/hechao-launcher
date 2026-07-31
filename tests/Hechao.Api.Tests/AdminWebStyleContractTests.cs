using System.Text.RegularExpressions;

namespace Hechao.Api.Tests;

public sealed class AdminWebStyleContractTests
{
    [Fact]
    public void ServerControlWhitelist_OverridesGlobalInputDimensions()
    {
        var repositoryRoot = FindRepositoryRoot();
        var css = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "Hechao.Api",
            "wwwroot",
            "admin",
            "admin.css"));

        var rowRule = ReadRule(css, ".control-whitelist");
        var checkboxRule = ReadRule(
            css,
            ".control-whitelist input[type=\"checkbox\"]");

        Assert.Contains("grid-template-columns: 16px minmax(0, 1fr);", rowRule);
        Assert.Contains("align-items: center;", rowRule);
        Assert.Contains("width: 16px;", checkboxRule);
        Assert.Contains("height: 16px;", checkboxRule);
        Assert.Contains("margin: 0;", checkboxRule);
        Assert.Contains("padding: 0;", checkboxRule);
        Assert.Contains("accent-color: var(--brand);", checkboxRule);
    }

    [Fact]
    public void ServerControlMemorySettings_HaveStableMarkupAndResponsiveLayout()
    {
        var repositoryRoot = FindRepositoryRoot();
        var webRoot = Path.Combine(
            repositoryRoot,
            "src",
            "Hechao.Api",
            "wwwroot",
            "admin");
        var html = File.ReadAllText(Path.Combine(webRoot, "index.html"));
        var script = File.ReadAllText(Path.Combine(webRoot, "admin.js"));
        var css = File.ReadAllText(Path.Combine(webRoot, "admin.css"));

        Assert.Contains("id=\"control-initial-memory\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"control-maximum-memory\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"control-info-memory\"", html, StringComparison.Ordinal);
        Assert.Contains("initialMemoryMiB", script, StringComparison.Ordinal);
        Assert.Contains("maximumAllowedMemoryMiB", script, StringComparison.Ordinal);
        Assert.Contains(
            "grid-template-columns: repeat(6, minmax(0, 1fr));",
            ReadRule(css, ".control-detail-metrics"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ServerDirectory_ExplainsControlDerivedAvailability()
    {
        var repositoryRoot = FindRepositoryRoot();
        var script = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "Hechao.Api",
            "wwwroot",
            "admin",
            "admin.js"));

        Assert.Contains("server.hasControlTarget", script, StringComparison.Ordinal);
        Assert.Contains("server.controlTargetFresh", script, StringComparison.Ordinal);
        Assert.Contains("server.controlReportedOnline === false", script, StringComparison.Ordinal);
        Assert.Contains("服控失联", script, StringComparison.Ordinal);
        Assert.Contains("服务已停止", script, StringComparison.Ordinal);
        Assert.Contains("scheduleServerPolling(view === \"servers\")", script, StringComparison.Ordinal);
        Assert.Contains("/v1/admin/catalog/servers", script, StringComparison.Ordinal);
    }

    private static string ReadRule(string css, string selector)
    {
        var match = Regex.Match(
            css,
            $@"{Regex.Escape(selector)}\s*\{{(?<body>[^}}]*)\}}",
            RegexOptions.CultureInvariant);

        Assert.True(match.Success, $"CSS rule not found: {selector}");
        return match.Groups["body"].Value;
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(
                    current.FullName,
                    "Hechao.Launcher.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            "The repository root could not be located.");
    }
}

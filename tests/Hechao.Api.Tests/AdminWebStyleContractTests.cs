using System.Text.RegularExpressions;

namespace Hechao.Api.Tests;

public sealed class AdminWebStyleContractTests
{
    [Fact]
    public void DisabledButtons_KeepTheirVariantBackgroundOnHover()
    {
        var css = ReadAdminWebSource("src", "styles", "admin.css");

        Assert.DoesNotContain("revert-layer", css, StringComparison.Ordinal);
        foreach (var selector in new[]
        {
            ".button-primary:not(:disabled):hover",
            ".button-secondary:not(:disabled):hover",
            ".button-danger:not(:disabled):hover",
            ".icon-button:not(:disabled):hover",
            ".button-quiet:not(:disabled):hover",
            ".control-quick-commands button:not(:disabled):hover",
        })
        {
            Assert.NotEmpty(ReadRule(css, selector));
        }
    }

    [Fact]
    public void ServerControlWhitelist_OverridesGlobalInputDimensions()
    {
        var css = ReadAdminWebSource("src", "styles", "admin.css");

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
        var view = ReadAdminWebSource("src", "views", "ControlView.vue");
        var css = ReadAdminWebSource("src", "styles", "admin.css");

        Assert.Contains("settingsDraft.initialMemoryGiB", view, StringComparison.Ordinal);
        Assert.Contains("settingsDraft.maximumMemoryGiB", view, StringComparison.Ordinal);
        Assert.Contains("selectedTarget.settings?.maximumAllowedMemoryMiB", view, StringComparison.Ordinal);
        Assert.Contains("初始内存（GiB）", view, StringComparison.Ordinal);
        Assert.Contains("最大内存（GiB）", view, StringComparison.Ordinal);
        Assert.Contains(
            "grid-template-columns: repeat(5, minmax(0, 1fr));",
            css,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ServerDirectory_ExplainsControlDerivedAvailability()
    {
        var view = ReadAdminWebSource("src", "views", "ServersView.vue");

        Assert.Contains("item.hasControlTarget", view, StringComparison.Ordinal);
        Assert.Contains("item.controlTargetFresh", view, StringComparison.Ordinal);
        Assert.Contains("item.controlReportedOnline === false", view, StringComparison.Ordinal);
        Assert.Contains("服控失联", view, StringComparison.Ordinal);
        Assert.Contains("服务已停止", view, StringComparison.Ordinal);
        Assert.Contains("usePolling(servers.refresh, 5_000)", view, StringComparison.Ordinal);
        Assert.Contains("/v1/admin/catalog/servers", view, StringComparison.Ordinal);
    }

    [Fact]
    public void ServerCreation_DiscoversFreshOnlineUncataloguedControlTargets()
    {
        var view = ReadAdminWebSource("src", "views", "ServersView.vue");
        var css = ReadAdminWebSource("src", "styles", "admin.css");

        Assert.Contains("item.agentConnected && item.online", view, StringComparison.Ordinal);
        Assert.Contains("!ids.has(item.serverId)", view, StringComparison.Ordinal);
        Assert.Contains("@change=\"applyDiscovery\"", view, StringComparison.Ordinal);
        Assert.Contains("runtime.value?.targets", view, StringComparison.Ordinal);
        Assert.Contains("softwareVersion", view, StringComparison.Ordinal);
        Assert.Contains(".server-discovery", css, StringComparison.Ordinal);
    }

    [Fact]
    public void BuiltAdminEntry_UsesVueModuleBundlesWithoutLegacyScripts()
    {
        var webRoot = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Hechao.Api",
            "wwwroot",
            "admin");
        var html = File.ReadAllText(Path.Combine(webRoot, "index.html"));

        Assert.Contains("<div id=\"app\"></div>", html, StringComparison.Ordinal);
        Assert.Contains("src=\"/admin/assets/admin.js\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/admin/assets/admin.css\"", html, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(webRoot, "admin.js")));
        Assert.False(File.Exists(Path.Combine(webRoot, "admin.css")));
        Assert.Empty(Directory.GetFiles(
            webRoot,
            "*.map",
            SearchOption.AllDirectories));
    }

    private static string ReadAdminWebSource(params string[] segments)
    {
        return File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Hechao.Api",
            "AdminWeb",
            Path.Combine(segments)));
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

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

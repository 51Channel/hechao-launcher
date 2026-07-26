using Hechao.Launcher.Services;

namespace Hechao.Launcher.Tests;

public sealed class JavaRuntimeValidatorTests
{
    [Theory]
    [InlineData("openjdk version \"21.0.7\" 2025-04-15 LTS", 21)]
    [InlineData("java version \"17.0.12\" 2024-07-16 LTS", 17)]
    [InlineData("java version \"1.8.0_431\"", 8)]
    public void ParseMajorVersion_ReadsModernAndLegacyJavaVersions(
        string output,
        int expected)
    {
        Assert.Equal(expected, JavaRuntimeValidator.ParseMajorVersion(output));
    }

    [Theory]
    [InlineData("1.21.11", 21)]
    [InlineData("1.20.5", 21)]
    [InlineData("1.20.1", 17)]
    [InlineData("1.18.2", 17)]
    [InlineData("1.17.1", 16)]
    [InlineData("1.16.5", 8)]
    public void RecommendedJavaVersion_MatchesMinecraftRequirements(
        string minecraftVersion,
        int expected)
    {
        Assert.Equal(
            expected,
            ViewModels.MainWindowViewModel.GetRecommendedJavaMajorVersion(
                minecraftVersion));
    }
}

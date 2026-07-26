using Hechao.Launcher.Services;

namespace Hechao.Launcher.Tests;

public sealed class ProfileRuntimePathResolverTests
{
    [Fact]
    public void ContainsFormatCharacters_DetectsInvisibleUnicodeFormatting()
    {
        Assert.True(ProfileRuntimePathResolver.ContainsFormatCharacters(
            @"H:\hechao \u200cLauncher".Replace(@"\u200c", "\u200c")));
        Assert.False(ProfileRuntimePathResolver.ContainsFormatCharacters(
            @"H:\Hechao GameData"));
    }

    [Fact]
    public void GetLaunchRoot_CreatesSafeJunctionForRuntimeWithFormatCharacter()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var testRoot = Path.Combine(
            Path.GetTempPath(),
            "Hechao.RuntimePath.Tests",
            Guid.NewGuid().ToString("N"));
        var runtimeRoot = Path.Combine(testRoot, "runtime-\u200cdata");
        Directory.CreateDirectory(runtimeRoot);
        File.WriteAllText(Path.Combine(runtimeRoot, "marker.txt"), "ready");

        string? launchRoot = null;
        try
        {
            launchRoot = ProfileRuntimePathResolver.GetLaunchRoot(
                runtimeRoot,
                "base-1.21.11");

            Assert.DoesNotContain('\u200c', launchRoot);
            Assert.Equal("ready", File.ReadAllText(Path.Combine(launchRoot, "marker.txt")));
            Assert.NotEqual(
                FileAttributes.Normal,
                new DirectoryInfo(launchRoot).Attributes & FileAttributes.ReparsePoint);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(launchRoot) &&
                Directory.Exists(launchRoot))
            {
                Directory.Delete(launchRoot);
            }

            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }
    }
}

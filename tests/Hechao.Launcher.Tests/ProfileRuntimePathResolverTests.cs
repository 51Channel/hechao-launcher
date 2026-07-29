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
    public void GetRuntimeLaunchRoot_CreatesSafeJunctionForRuntimeWithFormatCharacter()
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
            launchRoot = ProfileRuntimePathResolver.GetRuntimeLaunchRoot(
                runtimeRoot,
                "base-1.21.11");

            Assert.DoesNotContain('\u200c', launchRoot);
            Assert.Equal("ready", File.ReadAllText(Path.Combine(launchRoot, "marker.txt")));
            Assert.NotEqual(
                (FileAttributes)0,
                new DirectoryInfo(launchRoot).Attributes & FileAttributes.ReparsePoint);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(launchRoot) &&
                Directory.Exists(launchRoot) &&
                (new DirectoryInfo(launchRoot).Attributes & FileAttributes.ReparsePoint) != 0)
            {
                Directory.Delete(launchRoot);
            }

            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void GetGameLaunchRoot_UsesSafeShortPathForGameWithFormatCharacter()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var testRoot = Path.Combine(
            Path.GetTempPath(),
            "Hechao.RuntimePath.Tests",
            Guid.NewGuid().ToString("N"));
        var gameRoot = Path.Combine(testRoot, "game-\u200cdata");
        Directory.CreateDirectory(gameRoot);
        File.WriteAllText(Path.Combine(gameRoot, "marker.txt"), "ready");

        try
        {
            var shortPath = ProfileRuntimePathResolver.TryGetSafeShortPath(gameRoot);
            if (shortPath is null)
            {
                Assert.Throws<IOException>(
                    () => ProfileRuntimePathResolver.GetGameLaunchRoot(gameRoot));
                return;
            }

            var launchRoot = ProfileRuntimePathResolver.GetGameLaunchRoot(gameRoot);
            Assert.Equal(shortPath, launchRoot, ignoreCase: true);
            Assert.DoesNotContain('\u200c', launchRoot);
            Assert.DoesNotContain("runtime-links", launchRoot);
            Assert.Equal("ready", File.ReadAllText(Path.Combine(launchRoot, "marker.txt")));
            Assert.Equal(
                (FileAttributes)0,
                new DirectoryInfo(launchRoot).Attributes & FileAttributes.ReparsePoint);
        }
        finally
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void TryGetSafeShortPath_ReturnsFormatFreeExistingDirectoryWhenAvailable()
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

        try
        {
            var shortPath = ProfileRuntimePathResolver.TryGetSafeShortPath(runtimeRoot);
            if (shortPath is null)
            {
                return;
            }

            Assert.True(Directory.Exists(shortPath));
            Assert.False(ProfileRuntimePathResolver.ContainsFormatCharacters(shortPath));
            Assert.Equal(
                (FileAttributes)0,
                new DirectoryInfo(shortPath).Attributes & FileAttributes.ReparsePoint);
        }
        finally
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }
    }
}

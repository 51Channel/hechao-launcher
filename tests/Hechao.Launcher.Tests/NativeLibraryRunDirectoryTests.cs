using Hechao.Launcher.Services;

namespace Hechao.Launcher.Tests;

public sealed class NativeLibraryRunDirectoryTests
{
    [Fact]
    public async Task PrepareAsync_CreatesWritablePhysicalFormatSafeDirectory()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var testRoot = CreateTestRoot();
        var sourceDirectory = Path.Combine(testRoot, "source-\u200cdata");
        var runRoot = Path.Combine(testRoot, "runs");
        Directory.CreateDirectory(sourceDirectory);
        await PopulateSourceAsync(sourceDirectory, "native-v1");

        try
        {
            var result = await NativeLibraryRunDirectory.PrepareAsync(
                sourceDirectory,
                "activity-neoforge-1.21.11",
                "1.21.11-NeoForge_21.11.42",
                runRootOverride: runRoot);

            Assert.DoesNotContain('\u200c', result);
            Assert.True(File.Exists(Path.Combine(result, "lwjgl.dll")));
            Assert.True(File.Exists(Path.Combine(result, "native-marker.bin")));
            Assert.False(File.Exists(Path.Combine(result, "jna123456.dll")));
            Assert.Equal(
                (FileAttributes)0,
                new DirectoryInfo(result).Attributes & FileAttributes.ReparsePoint);
            var probePath = Path.Combine(result, "runtime-write-probe.txt");
            await File.WriteAllTextAsync(probePath, "ready");
            File.Delete(probePath);
        }
        finally
        {
            DeleteDirectory(testRoot);
        }
    }

    [Fact]
    public async Task PrepareAsync_ReplacesStaleRunContents()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var testRoot = CreateTestRoot();
        var sourceDirectory = Path.Combine(testRoot, "source");
        var runRoot = Path.Combine(testRoot, "runs");
        Directory.CreateDirectory(sourceDirectory);
        await PopulateSourceAsync(sourceDirectory, "native-v1");

        try
        {
            var first = await NativeLibraryRunDirectory.PrepareAsync(
                sourceDirectory,
                "activity",
                "neoforge",
                runRootOverride: runRoot);
            await File.WriteAllTextAsync(
                Path.Combine(first, "stale-file.bin"),
                "stale");
            await File.WriteAllTextAsync(
                Path.Combine(sourceDirectory, "native-marker.bin"),
                "native-v2");
            var second = await NativeLibraryRunDirectory.PrepareAsync(
                sourceDirectory,
                "activity",
                "neoforge",
                runRootOverride: runRoot);

            Assert.Equal(first, second, ignoreCase: true);
            Assert.False(File.Exists(Path.Combine(second, "stale-file.bin")));
            Assert.Equal(
                "native-v2",
                await File.ReadAllTextAsync(Path.Combine(second, "native-marker.bin")));
        }
        finally
        {
            DeleteDirectory(testRoot);
        }
    }

    [Fact]
    public async Task PrepareAsync_AllowsModernLwjglRuntimeExtraction()
    {
        var testRoot = CreateTestRoot();
        var sourceDirectory = Path.Combine(testRoot, "empty-source");
        var runRoot = Path.Combine(testRoot, "runs");
        Directory.CreateDirectory(sourceDirectory);

        try
        {
            var result = await NativeLibraryRunDirectory.PrepareAsync(
                sourceDirectory,
                "activity",
                "neoforge",
                runRootOverride: runRoot);

            Assert.True(Directory.Exists(result));
            Assert.Empty(Directory.EnumerateFiles(result));
        }
        finally
        {
            DeleteDirectory(testRoot);
        }
    }

    [Fact]
    public async Task PrepareAsync_RejectsUnusableExistingLwjglLibrary()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var testRoot = CreateTestRoot();
        var sourceDirectory = Path.Combine(testRoot, "source");
        var runRoot = Path.Combine(testRoot, "runs");
        Directory.CreateDirectory(sourceDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(sourceDirectory, "lwjgl.dll"),
            "not-a-native-library");

        try
        {
            var exception = await Assert.ThrowsAsync<IOException>(() =>
                NativeLibraryRunDirectory.PrepareAsync(
                    sourceDirectory,
                    "activity",
                    "neoforge",
                    runRootOverride: runRoot));

            Assert.Contains("cannot be loaded", exception.Message);
            Assert.Empty(Directory.EnumerateDirectories(runRoot));
        }
        finally
        {
            DeleteDirectory(testRoot);
        }
    }

    private static async Task PopulateSourceAsync(
        string sourceDirectory,
        string marker)
    {
        File.Copy(
            Path.Combine(Environment.SystemDirectory, "version.dll"),
            Path.Combine(sourceDirectory, "lwjgl.dll"));
        await File.WriteAllTextAsync(
            Path.Combine(sourceDirectory, "native-marker.bin"),
            marker);
        await File.WriteAllTextAsync(
            Path.Combine(sourceDirectory, "jna123456.dll"),
            "generated");
    }

    private static string CreateTestRoot()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "Hechao.NativeLibraryRunDirectory.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}

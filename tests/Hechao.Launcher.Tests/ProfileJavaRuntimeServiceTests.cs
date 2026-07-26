using System.Text.Json;
using Hechao.Distribution;
using Hechao.Launcher.Services;

namespace Hechao.Launcher.Tests;

public sealed class ProfileJavaRuntimeServiceTests
{
    [Fact]
    public async Task IsReadyAsync_AcceptsMatchingPerProfileRuntimeMarker()
    {
        using var temporary = new TemporaryDirectory();
        const string profileId = "base-1.21.11";
        const string versionId = "1.21.11-Fabric 0.19.2";
        var layout = new ClientStorageLayout(temporary.Path);
        var gameDirectory = layout.GetProfileGameDirectory(profileId);
        var versionDirectory = Path.Combine(gameDirectory, "versions", versionId);
        Directory.CreateDirectory(versionDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(gameDirectory, "hechao-profile.json"),
            JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                versionId,
                javaMajorVersion = 21
            }));
        await File.WriteAllTextAsync(
            Path.Combine(versionDirectory, versionId + ".json"),
            JsonSerializer.Serialize(new
            {
                id = versionId,
                javaVersion = new { majorVersion = 21 }
            }));
        await File.WriteAllBytesAsync(
            Path.Combine(versionDirectory, versionId + ".jar"),
            [0x50, 0x4b]);

        var runtimeRoot = layout.GetProfileRuntimeRoot(profileId);
        var relativeExecutable = Path.Combine(
            "windows-x64",
            "java-runtime-delta",
            "bin",
            "java.exe");
        var executable = Path.Combine(runtimeRoot, relativeExecutable);
        Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
        await File.WriteAllTextAsync(executable, "test");
        await File.WriteAllTextAsync(
            Path.Combine(layout.GetProfileRoot(profileId), ".hechao-java.json"),
            JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                javaMajorVersion = 21,
                executablePath = relativeExecutable,
                installedAt = DateTimeOffset.UtcNow
            }));

        using var httpClient = new HttpClient();
        var service = new ProfileJavaRuntimeService(httpClient);

        Assert.True(await service.IsReadyAsync(
            temporary.Path,
            profileId));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "Hechao.ProfileJava.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}

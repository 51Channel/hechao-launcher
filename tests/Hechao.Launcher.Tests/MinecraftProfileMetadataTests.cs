using System.Text.Json;
using Hechao.Launcher.Services;

namespace Hechao.Launcher.Tests;

public sealed class MinecraftProfileMetadataTests
{
    [Fact]
    public async Task ReadAndValidateMetadataAsync_AcceptsMatchingInstalledVersion()
    {
        using var profile = await TestProfile.CreateAsync(
            metadataVersionId: "1.21.11-Fabric 0.19.2",
            versionJsonId: "1.21.11-Fabric 0.19.2",
            metadataJavaMajor: 21,
            versionJsonJavaMajor: 21);

        var metadata = await MinecraftGameLauncherService.ReadAndValidateMetadataAsync(
            profile.Path,
            CancellationToken.None);

        Assert.Equal("1.21.11-Fabric 0.19.2", metadata.VersionId);
        Assert.Equal(21, metadata.JavaMajorVersion);
    }

    [Theory]
    [InlineData("different-version", 21)]
    [InlineData("1.21.11-Fabric 0.19.2", 17)]
    public async Task ReadAndValidateMetadataAsync_RejectsVersionJsonMismatch(
        string versionJsonId,
        int versionJsonJavaMajor)
    {
        using var profile = await TestProfile.CreateAsync(
            metadataVersionId: "1.21.11-Fabric 0.19.2",
            versionJsonId,
            metadataJavaMajor: 21,
            versionJsonJavaMajor);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => MinecraftGameLauncherService.ReadAndValidateMetadataAsync(
                profile.Path,
                CancellationToken.None));
    }

    [Fact]
    public async Task ReadAndValidateMetadataAsync_RejectsUnsafeVersionId()
    {
        using var profile = await TestProfile.CreateAsync(
            metadataVersionId: "../outside",
            versionJsonId: "../outside",
            metadataJavaMajor: 21,
            versionJsonJavaMajor: 21,
            createVersionFiles: false);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => MinecraftGameLauncherService.ReadAndValidateMetadataAsync(
                profile.Path,
                CancellationToken.None));
    }

    private sealed class TestProfile(string path) : IDisposable
    {
        public string Path { get; } = path;

        public static async Task<TestProfile> CreateAsync(
            string metadataVersionId,
            string versionJsonId,
            int metadataJavaMajor,
            int versionJsonJavaMajor,
            bool createVersionFiles = true)
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "Hechao.Launcher.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);

            var metadata = new
            {
                schemaVersion = 1,
                versionId = metadataVersionId,
                javaMajorVersion = metadataJavaMajor
            };
            await File.WriteAllTextAsync(
                System.IO.Path.Combine(path, "hechao-profile.json"),
                JsonSerializer.Serialize(metadata));

            if (createVersionFiles)
            {
                var versionDirectory = System.IO.Path.Combine(
                    path,
                    "versions",
                    metadataVersionId);
                Directory.CreateDirectory(versionDirectory);
                var versionJson = new
                {
                    id = versionJsonId,
                    javaVersion = new
                    {
                        majorVersion = versionJsonJavaMajor
                    }
                };
                await File.WriteAllTextAsync(
                    System.IO.Path.Combine(versionDirectory, metadataVersionId + ".json"),
                    JsonSerializer.Serialize(versionJson));
                await File.WriteAllBytesAsync(
                    System.IO.Path.Combine(versionDirectory, metadataVersionId + ".jar"),
                    [0x50, 0x4b, 0x03, 0x04]);
            }

            return new TestProfile(path);
        }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}

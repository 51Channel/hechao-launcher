using System.Text.Json;

namespace Hechao.StatusCollector.Tests;

public sealed class CollectorConfigurationTests
{
    [Fact]
    public async Task Load_NormalizesExpectedProcessExecutablePath()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"hechao-status-config-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var configurationPath = Path.Combine(directory, "server-heartbeats.json");

        try
        {
            await File.WriteAllTextAsync(
                configurationPath,
                JsonSerializer.Serialize(new
                {
                    apiEndpoint = "https://launcher-api.hechao.world/v1/internal/server-heartbeats",
                    collectorInstance = "owl9-pvp",
                    tokenPath = "heartbeat-token.dat",
                    servers = new[]
                    {
                        new
                        {
                            velocityTarget = "pvp-purpur",
                            host = "127.0.0.1",
                            port = 25565,
                            fallbackMaxPlayers = 20,
                            dataPath = "server",
                            expectedProcessExecutablePath = "runtime/java.exe"
                        }
                    }
                }));

            var configuration = CollectorConfiguration.Load(configurationPath);

            var server = Assert.Single(configuration.Servers);
            Assert.Equal(
                Path.GetFullPath(Path.Combine(directory, "runtime", "java.exe")),
                server.ExpectedProcessExecutablePath);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

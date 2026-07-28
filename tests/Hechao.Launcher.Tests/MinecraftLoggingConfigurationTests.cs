using System.Net;
using System.Security.Cryptography;
using CmlLib.Core.Files;
using Hechao.Launcher.Services;

namespace Hechao.Launcher.Tests;

public sealed class MinecraftLoggingConfigurationTests
{
    [Fact]
    public async Task EnsureLoggingConfigurationAsync_DownloadsAndReusesVerifiedFile()
    {
        using var temporary = new TemporaryDirectory();
        var content = "<Configuration />"u8.ToArray();
        var handler = new RecordingHandler(content);
        using var httpClient = new HttpClient(handler);
        var logging = CreateMetadata(content);

        await MinecraftGameLauncherService.EnsureLoggingConfigurationAsync(
            httpClient,
            temporary.Path,
            logging);
        await MinecraftGameLauncherService.EnsureLoggingConfigurationAsync(
            httpClient,
            temporary.Path,
            logging);

        var destination = Path.Combine(
            temporary.Path,
            "assets",
            "log_configs",
            "client-test.xml");
        Assert.Equal(content, await File.ReadAllBytesAsync(destination));
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task EnsureLoggingConfigurationAsync_ReplacesCorruptFileAtomically()
    {
        using var temporary = new TemporaryDirectory();
        var content = "<Configuration status=\"warn\" />"u8.ToArray();
        var destination = Path.Combine(
            temporary.Path,
            "assets",
            "log_configs",
            "client-test.xml");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await File.WriteAllTextAsync(destination, "corrupt");
        using var httpClient = new HttpClient(new RecordingHandler(content));

        await MinecraftGameLauncherService.EnsureLoggingConfigurationAsync(
            httpClient,
            temporary.Path,
            CreateMetadata(content));

        Assert.Equal(content, await File.ReadAllBytesAsync(destination));
        Assert.Empty(Directory.GetFiles(
            Path.GetDirectoryName(destination)!,
            "*.tmp",
            SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task EnsureLoggingConfigurationAsync_AcceptsManagedGameRelativePath()
    {
        using var temporary = new TemporaryDirectory();
        var content = "<Configuration />"u8.ToArray();
        var logging = CreateMetadata(content);
        Assert.NotNull(logging.LogFile);
        logging.LogFile!.Path = "assets/log_configs/client-test.xml";
        using var httpClient = new HttpClient(new RecordingHandler(content));

        await MinecraftGameLauncherService.EnsureLoggingConfigurationAsync(
            httpClient,
            temporary.Path,
            logging);

        Assert.Equal(content, await File.ReadAllBytesAsync(Path.Combine(
            temporary.Path,
            "assets",
            "log_configs",
            "client-test.xml")));
    }

    [Fact]
    public async Task EnsureLoggingConfigurationAsync_RejectsPathOutsideLogDirectory()
    {
        using var temporary = new TemporaryDirectory();
        var content = "<Configuration />"u8.ToArray();
        var logging = CreateMetadata(content);
        Assert.NotNull(logging.LogFile);
        logging.LogFile!.Path = "../outside.xml";
        var handler = new RecordingHandler(content);
        using var httpClient = new HttpClient(handler);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => MinecraftGameLauncherService.EnsureLoggingConfigurationAsync(
                httpClient,
                temporary.Path,
                logging));

        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task EnsureLoggingConfigurationAsync_RejectsInvalidDownload()
    {
        using var temporary = new TemporaryDirectory();
        var expected = "<Configuration />"u8.ToArray();
        var downloaded = "<Configuration status=\"invalid\" />"u8.ToArray();
        using var httpClient = new HttpClient(new RecordingHandler(downloaded));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => MinecraftGameLauncherService.EnsureLoggingConfigurationAsync(
                httpClient,
                temporary.Path,
                CreateMetadata(expected)));

        Assert.False(File.Exists(Path.Combine(
            temporary.Path,
            "assets",
            "log_configs",
            "client-test.xml")));
    }

    private static MLogFileMetadata CreateMetadata(byte[] content) =>
        new()
        {
            Type = "log4j2-xml",
            Argument = "-Dlog4j.configurationFile=${path}",
            LogFile = new MFileMetadata
            {
                Id = "client-test.xml",
                Name = "client-test.xml",
                Path = "client-test.xml",
                Sha1 = Convert.ToHexString(SHA1.HashData(content)).ToLowerInvariant(),
                Size = content.LongLength,
                Url = "https://piston-data.mojang.com/test/client-test.xml"
            }
        };

    private sealed class RecordingHandler(byte[] content) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content)
            });
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "Hechao.Logging.Tests",
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

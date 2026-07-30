using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using Hechao.Launcher.Services;

namespace Hechao.Launcher.Tests;

public sealed class MinecraftSkinServiceTests
{
    [Fact]
    public async Task GetSkinAsync_UsesOfficialHttpsTextureAndCachesResult()
    {
        var textureValue = Convert.ToBase64String(Encoding.UTF8.GetBytes(
            """
            {
              "textures": {
                "SKIN": {
                  "url": "http://textures.minecraft.net/texture/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
                }
              }
            }
            """));
        var png = CreateSkinHeader(height: 64);
        var handler = new RecordingHandler(
            request =>
            {
                Assert.Equal(
                    "sessionserver.mojang.com",
                    request.RequestUri!.Host);
                return JsonResponse(new
                {
                    properties = new[]
                    {
                        new { name = "textures", value = textureValue }
                    }
                });
            },
            request =>
            {
                Assert.Equal(Uri.UriSchemeHttps, request.RequestUri!.Scheme);
                Assert.Equal(
                    "textures.minecraft.net",
                    request.RequestUri.Host);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(png)
                };
            });
        var cache = CreateTemporaryDirectory();
        try
        {
            var service = new MinecraftSkinService(
                new HttpClient(handler),
                cache);
            var uuid = Guid.Parse(
                "069a79f4-44e9-4726-a5be-fca90e38aaf5");

            var result = await service.GetSkinAsync(uuid);
            var cached = await service.GetSkinAsync(uuid);

            Assert.NotNull(result);
            Assert.Equal(png, result.PngBytes);
            Assert.NotNull(cached);
            Assert.Equal(2, handler.RequestCount);
            Assert.Single(Directory.GetFiles(cache, "*.png"));
        }
        finally
        {
            Directory.Delete(cache, recursive: true);
        }
    }

    [Fact]
    public async Task GetSkinAsync_RejectsNonOfficialTextureHost()
    {
        var textureValue = Convert.ToBase64String(Encoding.UTF8.GetBytes(
            """
            {
              "textures": {
                "SKIN": {
                  "url": "https://example.com/texture/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
                }
              }
            }
            """));
        var handler = new RecordingHandler(_ => JsonResponse(new
        {
            properties = new[]
            {
                new { name = "textures", value = textureValue }
            }
        }));
        var cache = CreateTemporaryDirectory();
        try
        {
            var service = new MinecraftSkinService(
                new HttpClient(handler),
                cache);

            var result = await service.GetSkinAsync(Guid.NewGuid());

            Assert.Null(result);
            Assert.Equal(1, handler.RequestCount);
            Assert.Empty(Directory.GetFiles(cache));
        }
        finally
        {
            Directory.Delete(cache, recursive: true);
        }
    }

    private static byte[] CreateSkinHeader(int height)
    {
        var bytes = new byte[33];
        new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8, 4), 13);
        "IHDR"u8.CopyTo(bytes.AsSpan(12, 4));
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16, 4), 64);
        BinaryPrimitives.WriteUInt32BigEndian(
            bytes.AsSpan(20, 4),
            (uint)height);
        return bytes;
    }

    private static HttpResponseMessage JsonResponse<T>(T value) =>
        new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(value)
        };

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "hechao-skin-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class RecordingHandler(
        params Func<HttpRequestMessage, HttpResponseMessage>[] responses)
        : HttpMessageHandler
    {
        private readonly Queue<Func<
            HttpRequestMessage,
            HttpResponseMessage>> _responses = new(responses);

        internal int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestCount++;
            Assert.NotEmpty(_responses);
            return Task.FromResult(_responses.Dequeue()(request));
        }
    }
}

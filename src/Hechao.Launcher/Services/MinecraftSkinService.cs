using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Hechao.Launcher.Services;

public interface IMinecraftSkinService
{
    Task<MinecraftSkinImage?> GetSkinAsync(
        Guid minecraftUuid,
        CancellationToken cancellationToken = default);
}

public sealed record MinecraftSkinImage(byte[] PngBytes);

public sealed partial class MinecraftSkinService : IMinecraftSkinService
{
    private const int MaximumProfileBytes = 64 * 1024;
    private const int MaximumTextureBytes = 512 * 1024;
    private static readonly TimeSpan Freshness = TimeSpan.FromHours(24);
    private static readonly byte[] PngSignature =
        [137, 80, 78, 71, 13, 10, 26, 10];
    private readonly HttpClient _httpClient;
    private readonly string _cacheDirectory;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);

    internal MinecraftSkinService(
        HttpClient httpClient,
        string cacheDirectory,
        TimeProvider? timeProvider = null)
    {
        _httpClient = httpClient;
        _cacheDirectory = Path.GetFullPath(cacheDirectory);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public static MinecraftSkinService CreateDefault()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression =
                DecompressionMethods.GZip |
                DecompressionMethods.Deflate |
                DecompressionMethods.Brotli,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            PooledConnectionLifetime = TimeSpan.FromMinutes(10)
        };
        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(12)
        };
        client.DefaultRequestHeaders.UserAgent.Add(
            LauncherProductInfo.CreateUserAgent());
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        var cacheDirectory = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "Hechao",
            "Launcher",
            "cache",
            "skins");
        return new MinecraftSkinService(client, cacheDirectory);
    }

    public async Task<MinecraftSkinImage?> GetSkinAsync(
        Guid minecraftUuid,
        CancellationToken cancellationToken = default)
    {
        if (minecraftUuid == Guid.Empty)
        {
            return null;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var cachePath = GetCachePath(minecraftUuid);
            var cached = await TryReadCacheAsync(
                cachePath,
                requireFresh: true,
                cancellationToken);
            if (cached is not null)
            {
                return cached;
            }

            try
            {
                var textureUri = await ResolveTextureUriAsync(
                    minecraftUuid,
                    cancellationToken);
                if (textureUri is null)
                {
                    return await TryReadCacheAsync(
                        cachePath,
                        requireFresh: false,
                        cancellationToken);
                }

                var bytes = await DownloadTextureAsync(
                    textureUri,
                    cancellationToken);
                if (!IsSupportedSkinPng(bytes))
                {
                    throw new InvalidDataException(
                        "The Minecraft skin image has an invalid format.");
                }

                await WriteCacheAsync(cachePath, bytes, cancellationToken);
                return new MinecraftSkinImage(bytes);
            }
            catch (OperationCanceledException) when (
                !cancellationToken.IsCancellationRequested)
            {
                return await TryReadCacheAsync(
                    cachePath,
                    requireFresh: false,
                    cancellationToken);
            }
            catch (Exception exception) when (
                exception is HttpRequestException or IOException or
                    UnauthorizedAccessException or InvalidDataException or
                    JsonException or FormatException)
            {
                return await TryReadCacheAsync(
                    cachePath,
                    requireFresh: false,
                    cancellationToken);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Uri?> ResolveTextureUriAsync(
        Guid minecraftUuid,
        CancellationToken cancellationToken)
    {
        var profileUri = new Uri(
            "https://sessionserver.mojang.com/session/minecraft/profile/" +
            minecraftUuid.ToString("N") +
            "?unsigned=false",
            UriKind.Absolute);
        using var request = new HttpRequestMessage(HttpMethod.Get, profileUri);
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (response.StatusCode is HttpStatusCode.NoContent or
                HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var profileBytes = await ReadLimitedAsync(
            response.Content,
            MaximumProfileBytes,
            cancellationToken);
        using var document = JsonDocument.Parse(profileBytes);
        if (!document.RootElement.TryGetProperty(
                "properties",
                out var properties) ||
            properties.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                "The Minecraft profile has no texture properties.");
        }

        string? encodedTextures = null;
        foreach (var property in properties.EnumerateArray())
        {
            if (property.TryGetProperty("name", out var name) &&
                string.Equals(
                    name.GetString(),
                    "textures",
                    StringComparison.Ordinal) &&
                property.TryGetProperty("value", out var value))
            {
                encodedTextures = value.GetString();
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(encodedTextures) ||
            encodedTextures.Length > 48 * 1024)
        {
            throw new InvalidDataException(
                "The Minecraft profile texture property is invalid.");
        }

        var textureJson = Convert.FromBase64String(encodedTextures);
        try
        {
            using var textures = JsonDocument.Parse(textureJson);
            if (!textures.RootElement.TryGetProperty(
                    "textures",
                    out var textureRoot) ||
                !textureRoot.TryGetProperty("SKIN", out var skin) ||
                !skin.TryGetProperty("url", out var urlProperty))
            {
                return null;
            }

            return NormalizeTextureUri(urlProperty.GetString());
        }
        finally
        {
            Array.Clear(textureJson, 0, textureJson.Length);
        }
    }

    private async Task<byte[]> DownloadTextureAsync(
        Uri textureUri,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, textureUri);
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("image/png"));
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return await ReadLimitedAsync(
            response.Content,
            MaximumTextureBytes,
            cancellationToken);
    }

    private async Task<MinecraftSkinImage?> TryReadCacheAsync(
        string path,
        bool requireFresh,
        CancellationToken cancellationToken)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists ||
                file.Length is < 33 or > MaximumTextureBytes ||
                (requireFresh &&
                 _timeProvider.GetUtcNow() -
                 new DateTimeOffset(file.LastWriteTimeUtc) > Freshness))
            {
                return null;
            }

            var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
            return IsSupportedSkinPng(bytes)
                ? new MinecraftSkinImage(bytes)
                : null;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static async Task WriteCacheAsync(
        string path,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidDataException(
                "The Minecraft skin cache path is invalid.");
        Directory.CreateDirectory(directory);
        var temporaryPath =
            path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(
                temporaryPath,
                bytes,
                cancellationToken);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private string GetCachePath(Guid minecraftUuid) =>
        Path.Combine(_cacheDirectory, minecraftUuid.ToString("N") + ".png");

    private static Uri NormalizeTextureUri(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !string.Equals(
                uri.Host,
                "textures.minecraft.net",
                StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            !TexturePathRegex().IsMatch(uri.AbsolutePath) ||
            (uri.Scheme != Uri.UriSchemeHttps &&
             uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new InvalidDataException(
                "The Minecraft skin texture URL is not trusted.");
        }

        return new UriBuilder(uri)
        {
            Scheme = Uri.UriSchemeHttps,
            Port = -1
        }.Uri;
    }

    private static bool IsSupportedSkinPng(byte[] bytes)
    {
        if (bytes.Length < 33 ||
            !bytes.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature) ||
            BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(8, 4)) != 13 ||
            !bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8))
        {
            return false;
        }

        var width = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20, 4));
        return width == 64 && height is 32 or 64;
    }

    private static async Task<byte[]> ReadLimitedAsync(
        HttpContent content,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is long contentLength &&
            contentLength > maximumBytes)
        {
            throw new InvalidDataException(
                "The Minecraft response exceeds the allowed size.");
        }

        await using var source =
            await content.ReadAsStreamAsync(cancellationToken);
        using var destination = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (destination.Length + read > maximumBytes)
            {
                throw new InvalidDataException(
                    "The Minecraft response exceeds the allowed size.");
            }

            destination.Write(buffer, 0, read);
        }

        return destination.ToArray();
    }

    [GeneratedRegex("^/texture/[a-fA-F0-9]{32,128}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex TexturePathRegex();
}

internal sealed class NullMinecraftSkinService : IMinecraftSkinService
{
    internal static NullMinecraftSkinService Instance { get; } = new();

    public Task<MinecraftSkinImage?> GetSkinAsync(
        Guid minecraftUuid,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<MinecraftSkinImage?>(null);
}

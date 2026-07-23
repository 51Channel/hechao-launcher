using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hechao.Contracts;

namespace Hechao.Launcher.Services;

public enum CatalogSource
{
    Live,
    Cache,
    BuiltIn
}

public interface ICatalogSourceState
{
    CatalogSource LastSource { get; }
}

public sealed class HttpServerCatalogClient : IServerCatalogClient, ICatalogSourceState
{
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    private readonly LauncherApiClient _apiClient;
    private readonly IServerCatalogClient _builtInCatalog;
    private readonly string _cachePath;
    private readonly SemaphoreSlim _requestGate = new(1, 1);

    private HttpServerCatalogClient(LauncherApiClient apiClient, IServerCatalogClient builtInCatalog, string cachePath)
    {
        _apiClient = apiClient;
        _builtInCatalog = builtInCatalog;
        _cachePath = cachePath;
    }

    public CatalogSource LastSource { get; private set; } = CatalogSource.BuiltIn;

    public static HttpServerCatalogClient CreateDefault(
        IServerCatalogClient builtInCatalog,
        LauncherApiClient apiClient)
    {
        var applicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var cachePath = Path.Combine(applicationData, "Hechao", "Launcher", "catalog-cache.json");
        return new HttpServerCatalogClient(apiClient, builtInCatalog, cachePath);
    }

    public async Task<LauncherCatalogSnapshot> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        await _requestGate.WaitAsync(cancellationToken);
        try
        {
            try
            {
                var liveSnapshot = await GetLiveCatalogAsync(cancellationToken);
                LastSource = CatalogSource.Live;
                await TryWriteCacheAsync(liveSnapshot, cancellationToken);
                return liveSnapshot;
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return await GetFallbackCatalogAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is HttpRequestException or JsonException or InvalidDataException)
            {
                return await GetFallbackCatalogAsync(cancellationToken);
            }
            catch (LauncherApiException exception) when ((int)exception.StatusCode >= 500)
            {
                return await GetFallbackCatalogAsync(cancellationToken);
            }
        }
        finally
        {
            _requestGate.Release();
        }
    }

    private async Task<LauncherCatalogSnapshot> GetLiveCatalogAsync(CancellationToken cancellationToken)
    {
        var snapshot = await _apiClient.GetCatalogAsync(cancellationToken);
        ValidateSnapshot(snapshot);
        return snapshot;
    }

    private async Task<LauncherCatalogSnapshot> GetFallbackCatalogAsync(CancellationToken cancellationToken)
    {
        var cachedSnapshot = await TryReadCacheAsync(cancellationToken);
        if (cachedSnapshot is not null)
        {
            LastSource = CatalogSource.Cache;
            return cachedSnapshot;
        }

        LastSource = CatalogSource.BuiltIn;
        return await _builtInCatalog.GetCatalogAsync(cancellationToken);
    }

    private async Task<LauncherCatalogSnapshot?> TryReadCacheAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(_cachePath))
            {
                return null;
            }

            await using var stream = File.OpenRead(_cachePath);
            var snapshot = await JsonSerializer.DeserializeAsync<LauncherCatalogSnapshot>(stream, SerializerOptions, cancellationToken);
            if (snapshot is null)
            {
                return null;
            }

            ValidateSnapshot(snapshot);
            return snapshot;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            return null;
        }
    }

    private async Task TryWriteCacheAsync(LauncherCatalogSnapshot snapshot, CancellationToken cancellationToken)
    {
        var temporaryPath = _cachePath + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, snapshot, SerializerOptions, cancellationToken);
            }

            File.Move(temporaryPath, _cachePath, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (IOException)
            {
            }
        }
    }

    private static void ValidateSnapshot(LauncherCatalogSnapshot snapshot)
    {
        if (snapshot.Servers.Count == 0 || snapshot.ClientProfiles.Count == 0)
        {
            throw new InvalidDataException("The server catalog is empty.");
        }

        var profileIds = snapshot.ClientProfiles.Select(profile => profile.Id).ToHashSet(StringComparer.Ordinal);
        if (profileIds.Count != snapshot.ClientProfiles.Count ||
            snapshot.Servers.Select(server => server.Id).Distinct(StringComparer.Ordinal).Count() != snapshot.Servers.Count)
        {
            throw new InvalidDataException("The server catalog contains duplicate identifiers.");
        }

        if (snapshot.Servers.Any(server =>
                string.IsNullOrWhiteSpace(server.Id) ||
                !profileIds.Contains(server.ClientProfileId) ||
                server.MaxPlayers <= 0 ||
                server.OnlinePlayers < 0 ||
                server.OnlinePlayers > server.MaxPlayers))
        {
            throw new InvalidDataException("The server catalog contains invalid server data.");
        }
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}

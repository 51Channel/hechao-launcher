using System.IO;
using System.Net;
using System.Net.Http;
using Hechao.Contracts;
using Hechao.Distribution;

namespace Hechao.Launcher.Services;

public interface IClientInstallationService
{
    Task<LocalProfileState> GetLocalStateAsync(
        ClientProfileSummary profile,
        string dataRoot,
        CancellationToken cancellationToken = default);

    Task InstallAsync(
        ClientProfileSummary profile,
        ClientInstallationOptions options,
        IProgress<ClientInstallProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed class ClientInstallationService : IClientInstallationService
{
    private readonly LauncherApiClient _apiClient;
    private readonly ManifestTrustBundle _trustBundle;
    private readonly ClientProfileInstaller _installer;

    private ClientInstallationService(
        LauncherApiClient apiClient,
        ManifestTrustBundle trustBundle,
        ClientProfileInstaller installer)
    {
        _apiClient = apiClient;
        _trustBundle = trustBundle;
        _installer = installer;
    }

    public static ClientInstallationService CreateDefault(LauncherApiClient apiClient)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            PooledConnectionLifetime = TimeSpan.FromMinutes(10)
        };
        var httpClient = new HttpClient(apiClient.CreateDownloadAuthorizationHandler(handler))
        {
            Timeout = TimeSpan.FromMinutes(30)
        };
        httpClient.DefaultRequestHeaders.UserAgent.Add(LauncherProductInfo.CreateUserAgent());
        return new ClientInstallationService(
            apiClient,
            ManifestTrustBundleLoader.LoadDefault(),
            new ClientProfileInstaller(new ResumableFileDownloader(httpClient)));
    }

    public Task<LocalProfileState> GetLocalStateAsync(
        ClientProfileSummary profile,
        string dataRoot,
        CancellationToken cancellationToken = default) =>
        _installer.GetLocalStateAsync(dataRoot, profile.Id, profile.Version, cancellationToken);

    public async Task InstallAsync(
        ClientProfileSummary profile,
        ClientInstallationOptions options,
        IProgress<ClientInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var envelope = await _apiClient.GetProfileManifestAsync(profile.Id, cancellationToken);
        var verified = SignedManifestCodec.Verify(envelope, _trustBundle);
        if (!string.Equals(verified.Manifest.ProfileId, profile.Id, StringComparison.Ordinal) ||
            !string.Equals(verified.Manifest.Version, profile.Version, StringComparison.Ordinal))
        {
            throw new ClientManifestMismatchException("The signed manifest does not match the selected client profile.");
        }

        if (!string.IsNullOrWhiteSpace(profile.Sha256) &&
            !string.Equals(profile.Sha256, verified.EnvelopeSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new ClientManifestMismatchException("The signed manifest digest does not match the catalog.");
        }

        await _installer.InstallAsync(verified, options, progress, cancellationToken);
    }
}

public sealed class ClientManifestMismatchException(string message) : IOException(message);

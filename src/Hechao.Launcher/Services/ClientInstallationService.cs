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

    Task<InstalledProfileState?> GetRollbackCandidateAsync(
        ClientProfileSummary profile,
        string dataRoot,
        CancellationToken cancellationToken = default);

    Task InstallAsync(
        ClientProfileSummary profile,
        ClientInstallationOptions options,
        IProgress<ClientInstallProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        ClientProfileSummary profile,
        string dataRoot,
        CancellationToken cancellationToken = default);

    Task<InstalledProfileState> RollbackAsync(
        ClientProfileSummary profile,
        string dataRoot,
        IProgress<ClientInstallProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed class ClientInstallationService : IClientInstallationService
{
    private readonly LauncherApiClient _apiClient;
    private readonly ManifestTrustBundle _trustBundle;
    private readonly ClientProfileInstaller _installer;
    private readonly IProfileJavaRuntimeService _javaRuntimeService;

    internal ClientInstallationService(
        LauncherApiClient apiClient,
        ManifestTrustBundle trustBundle,
        ClientProfileInstaller installer,
        IProfileJavaRuntimeService javaRuntimeService)
    {
        _apiClient = apiClient;
        _trustBundle = trustBundle;
        _installer = installer;
        _javaRuntimeService = javaRuntimeService;
    }

    public static ClientInstallationService CreateDefault(
        LauncherApiClient apiClient,
        bool useSystemProxy = false)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            MaxConnectionsPerServer = ClientProfileInstaller.DefaultMaxConcurrentDownloads,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            UseProxy = useSystemProxy
        };
        var httpClient = new HttpClient(apiClient.CreateDownloadAuthorizationHandler(handler))
        {
            Timeout = TimeSpan.FromMinutes(30)
        };
        httpClient.DefaultRequestHeaders.UserAgent.Add(LauncherProductInfo.CreateUserAgent());
        return new ClientInstallationService(
            apiClient,
            ManifestTrustBundleLoader.LoadDefault(),
            new ClientProfileInstaller(new ResumableFileDownloader(httpClient)),
            new ProfileJavaRuntimeService(httpClient));
    }

    public async Task<LocalProfileState> GetLocalStateAsync(
        ClientProfileSummary profile,
        string dataRoot,
        CancellationToken cancellationToken = default)
    {
        var profileState = await _installer.GetLocalStateAsync(
            dataRoot,
            profile.Id,
            profile.Version,
            cancellationToken);
        if (profileState != LocalProfileState.Ready)
        {
            return profileState;
        }

        return await _javaRuntimeService.IsReadyAsync(
            dataRoot,
            profile.Id,
            cancellationToken)
            ? LocalProfileState.Ready
            : LocalProfileState.UpdateRequired;
    }

    public Task<InstalledProfileState?> GetRollbackCandidateAsync(
        ClientProfileSummary profile,
        string dataRoot,
        CancellationToken cancellationToken = default) =>
        _installer.GetPreviousStateAsync(
            dataRoot,
            profile.Id,
            cancellationToken);

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

        var totalClientBytes = verified.Manifest.Files.Sum(file => file.Size);
        var profileProgress = progress is null
            ? null
            : new InlineProgress<ClientInstallProgress>(value =>
            {
                var mappedPhase = value.Phase == ClientInstallPhase.Complete
                    ? ClientInstallPhase.Switching
                    : value.Phase;
                progress.Report(value with
                {
                    Phase = mappedPhase,
                    Percent = Math.Clamp(value.Percent * 0.85, 0, 85)
                });
            });

        // Profile verification, preservation and atomic directory switching contain
        // synchronous file-system work. Keep those operations away from the WPF
        // dispatcher so a large existing client cannot freeze the launcher window.
        await Task.Run(
            () => _installer.InstallAsync(
                verified,
                options,
                profileProgress,
                cancellationToken),
            cancellationToken);

        var runtimeProgress = progress is null
            ? null
            : new InlineProgress<ProfileJavaInstallProgress>(value =>
                progress.Report(new ClientInstallProgress(
                    ClientInstallPhase.PreparingRuntime,
                    85 + Math.Clamp(value.Percent, 0, 100) * 0.15,
                    value.CurrentPath,
                    totalClientBytes,
                    totalClientBytes)));
        await _javaRuntimeService.InstallAsync(
            options.DataRoot,
            profile.Id,
            runtimeProgress,
            cancellationToken);
        progress?.Report(new ClientInstallProgress(
            ClientInstallPhase.Complete,
            100,
            string.Empty,
            totalClientBytes,
            totalClientBytes));
    }

    public Task<bool> DeleteAsync(
        ClientProfileSummary profile,
        string dataRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return _installer.DeleteAsync(dataRoot, profile.Id, cancellationToken);
    }

    public async Task<InstalledProfileState> RollbackAsync(
        ClientProfileSummary profile,
        string dataRoot,
        IProgress<ClientInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report(new ClientInstallProgress(
            ClientInstallPhase.Switching,
            10,
            string.Empty,
            0,
            0));
        var state = await Task.Run(
            () => _installer.RollbackAsync(
                dataRoot,
                profile.Id,
                cancellationToken),
            cancellationToken);

        progress?.Report(new ClientInstallProgress(
            ClientInstallPhase.Switching,
            85,
            string.Empty,
            0,
            0));
        try
        {
            if (!await _javaRuntimeService.IsReadyAsync(
                    dataRoot,
                    profile.Id,
                    cancellationToken))
            {
                var runtimeProgress = progress is null
                    ? null
                    : new InlineProgress<ProfileJavaInstallProgress>(value =>
                        progress.Report(new ClientInstallProgress(
                            ClientInstallPhase.PreparingRuntime,
                            85 + Math.Clamp(value.Percent, 0, 100) * 0.15,
                            value.CurrentPath,
                            0,
                            0)));
                await _javaRuntimeService.InstallAsync(
                    dataRoot,
                    profile.Id,
                    runtimeProgress,
                    cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ProfileRollbackRuntimeException(state, exception);
        }

        progress?.Report(new ClientInstallProgress(
            ClientInstallPhase.Complete,
            100,
            string.Empty,
            0,
            0));
        return state;
    }

    private sealed class InlineProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }
}

public sealed class ClientManifestMismatchException(string message) : IOException(message);

public sealed class ProfileRollbackRuntimeException(
    InstalledProfileState activatedState,
    Exception innerException)
    : IOException(
        $"Profile {activatedState.ProfileId} was rolled back to {activatedState.Version}, but its Java runtime is not ready.",
        innerException)
{
    public InstalledProfileState ActivatedState { get; } = activatedState;
}

using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using Hechao.Contracts;
using Hechao.Distribution;

namespace Hechao.Launcher.Services;

public sealed record LauncherUpdatePlan(
    Version CurrentVersion,
    Version LatestVersion,
    Version MinimumSupportedVersion,
    long InstallerBytes,
    string InstallerSha256,
    DateTimeOffset PublishedAt,
    string ReleaseNotes,
    Uri InstallerUri)
{
    public bool IsRequired => CurrentVersion < MinimumSupportedVersion;
}

public sealed record LauncherUpdateDownloadProgress(
    long BytesDownloaded,
    long TotalBytes)
{
    public double Percent =>
        TotalBytes <= 0
            ? 0
            : Math.Clamp(BytesDownloaded * 100d / TotalBytes, 0, 100);
}

public interface ILauncherUpdateService
{
    Task<LauncherUpdatePlan?> CheckAsync(
        CancellationToken cancellationToken = default);

    Task<bool> DownloadAndLaunchUpdaterAsync(
        LauncherUpdatePlan plan,
        IProgress<LauncherUpdateDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed class NullLauncherUpdateService : ILauncherUpdateService
{
    public static NullLauncherUpdateService Instance { get; } = new();

    public Task<LauncherUpdatePlan?> CheckAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<LauncherUpdatePlan?>(null);

    public Task<bool> DownloadAndLaunchUpdaterAsync(
        LauncherUpdatePlan plan,
        IProgress<LauncherUpdateDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
}

public sealed class LauncherUpdateService
{
    private const long MaximumInstallerBytes = 512L * 1024 * 1024;
    private readonly LauncherApiClient _apiClient;
    private readonly ResumableFileDownloader _downloader;
    private readonly string _updateRoot;
    private readonly Func<string?> _processPathProvider;
    private readonly Func<ProcessStartInfo, Process?> _processStarter;

    internal LauncherUpdateService(
        LauncherApiClient apiClient,
        ResumableFileDownloader downloader,
        string updateRoot,
        Func<string?> processPathProvider,
        Func<ProcessStartInfo, Process?> processStarter)
    {
        _apiClient = apiClient;
        _downloader = downloader;
        _updateRoot = Path.GetFullPath(updateRoot);
        _processPathProvider = processPathProvider;
        _processStarter = processStarter;
    }

    public static ILauncherUpdateService CreateDefault(LauncherApiClient apiClient)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression =
                System.Net.DecompressionMethods.GZip |
                System.Net.DecompressionMethods.Deflate |
                System.Net.DecompressionMethods.Brotli,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            UseProxy = false
        };
        var httpClient = new HttpClient(
            apiClient.CreateDownloadAuthorizationHandler(handler))
        {
            Timeout = TimeSpan.FromMinutes(30)
        };
        httpClient.DefaultRequestHeaders.UserAgent.Add(
            LauncherProductInfo.CreateUserAgent());
        var localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        return new LauncherUpdateServiceAdapter(
            new LauncherUpdateService(
                apiClient,
                new ResumableFileDownloader(httpClient),
                Path.Combine(localApplicationData, "Hechao", "Launcher", "updates"),
                () => Environment.ProcessPath,
                Process.Start));
    }

    internal async Task<LauncherUpdatePlan?> CheckAsync(
        CancellationToken cancellationToken)
    {
        var release = await _apiClient.GetLauncherUpdateAsync(cancellationToken);
        return release is null
            ? null
            : CreatePlan(release, LauncherProductInfo.Version);
    }

    internal static LauncherUpdatePlan? CreatePlan(
        LauncherUpdateRelease release,
        string currentVersion)
    {
        ArgumentNullException.ThrowIfNull(release);
        var current = ParseVersion(
            currentVersion,
            "The current launcher version is invalid.");
        var latest = ParseVersion(
            release.Version,
            "The launcher update version is invalid.");
        var minimum = ParseVersion(
            release.MinimumSupportedVersion,
            "The minimum launcher version is invalid.");
        if (latest < minimum)
        {
            throw new InvalidDataException(
                "The launcher update version is older than its minimum supported version.");
        }

        if (latest <= current)
        {
            return null;
        }

        if (release.InstallerBytes is < 1024 * 1024 or > MaximumInstallerBytes ||
            !IsSha256(release.InstallerSha256) ||
            release.ReleaseNotes.Length > 2000 ||
            !Uri.TryCreate(release.InstallerUrl, UriKind.Absolute, out var installerUri) ||
            !IsSafeDownloadUri(installerUri))
        {
            throw new InvalidDataException(
                "The launcher update metadata is invalid.");
        }

        return new LauncherUpdatePlan(
            current,
            latest,
            minimum,
            release.InstallerBytes,
            release.InstallerSha256.ToLowerInvariant(),
            release.PublishedAt,
            release.ReleaseNotes.Trim(),
            installerUri);
    }

    internal async Task<bool> DownloadAndLaunchUpdaterAsync(
        LauncherUpdatePlan plan,
        IProgress<LauncherUpdateDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var version = plan.LatestVersion.ToString(3);
        var updateDirectory = Path.Combine(_updateRoot, version);
        Directory.CreateDirectory(updateDirectory);
        var installerPath = Path.Combine(
            updateDirectory,
            $"Hechao-Launcher-Setup-{version}-win-x64.exe");
        var manifestFile = new ClientManifestFile(
            Path.GetFileName(installerPath),
            plan.InstallerBytes,
            plan.InstallerSha256,
            plan.InstallerUri.AbsoluteUri,
            Required: true);
        var downloadProgress = progress is null
            ? null
            : new InlineProgress<FileDownloadProgress>(value =>
                progress.Report(new LauncherUpdateDownloadProgress(
                    value.BytesDownloaded,
                    value.TotalBytes)));
        await _downloader.DownloadAsync(
            manifestFile,
            installerPath,
            downloadProgress,
            cancellationToken);

        var processPath = _processPathProvider();
        if (string.IsNullOrWhiteSpace(processPath) ||
            !File.Exists(processPath))
        {
            throw new InvalidOperationException(
                "The running launcher executable cannot be located.");
        }

        var updaterPath = Path.Combine(
            updateDirectory,
            "Hechao.Launcher.Updater.exe");
        File.Copy(processPath, updaterPath, overwrite: true);
        var startInfo = LauncherUpdateBootstrap.CreateStartInfo(
            updaterPath,
            Environment.ProcessId,
            installerPath,
            plan.InstallerBytes,
            plan.InstallerSha256,
            version);
        return _processStarter(startInfo) is not null;
    }

    private static Version ParseVersion(string value, string error)
    {
        if (!Version.TryParse(value, out var version) ||
            version.Major < 0 ||
            version.Minor < 0 ||
            version.Build < 0 ||
            version.Revision >= 0 ||
            !string.Equals(
                version.ToString(3),
                value,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(error);
        }

        return version;
    }

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(Uri.IsHexDigit);

    private static bool IsSafeDownloadUri(Uri value) =>
        string.IsNullOrEmpty(value.UserInfo) &&
        string.IsNullOrEmpty(value.Fragment) &&
        (value.Scheme == Uri.UriSchemeHttps ||
         (value.Scheme == Uri.UriSchemeHttp && value.IsLoopback));

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private sealed class LauncherUpdateServiceAdapter(
        LauncherUpdateService service) : ILauncherUpdateService
    {
        public Task<LauncherUpdatePlan?> CheckAsync(
            CancellationToken cancellationToken = default) =>
            service.CheckAsync(cancellationToken);

        public Task<bool> DownloadAndLaunchUpdaterAsync(
            LauncherUpdatePlan plan,
            IProgress<LauncherUpdateDownloadProgress>? progress = null,
            CancellationToken cancellationToken = default) =>
            service.DownloadAndLaunchUpdaterAsync(
                plan,
                progress,
                cancellationToken);
    }
}

internal sealed record LauncherUpdateBootstrapCommand(
    int ParentProcessId,
    string InstallerPath,
    long InstallerBytes,
    string InstallerSha256,
    string Version);

internal static class LauncherUpdateBootstrap
{
    private const string ApplyArgument = "--apply-launcher-update";
    private static readonly TimeSpan ParentExitTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan InstallerTimeout = TimeSpan.FromMinutes(5);

    internal static ProcessStartInfo CreateStartInfo(
        string updaterPath,
        int parentProcessId,
        string installerPath,
        long installerBytes,
        string installerSha256,
        string version)
    {
        var startInfo = new ProcessStartInfo(updaterPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(updaterPath)!
        };
        startInfo.ArgumentList.Add(ApplyArgument);
        startInfo.ArgumentList.Add(parentProcessId.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add(installerPath);
        startInfo.ArgumentList.Add(installerBytes.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add(installerSha256);
        startInfo.ArgumentList.Add(version);
        return startInfo;
    }

    internal static bool TryParse(
        IReadOnlyList<string> arguments,
        out LauncherUpdateBootstrapCommand? command)
    {
        command = null;
        if (arguments.Count != 6 ||
            !string.Equals(arguments[0], ApplyArgument, StringComparison.Ordinal) ||
            !int.TryParse(
                arguments[1],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var parentProcessId) ||
            parentProcessId <= 0 ||
            !long.TryParse(
                arguments[3],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var installerBytes) ||
            installerBytes is < 1024 * 1024 or > 512L * 1024 * 1024 ||
            arguments[4].Length != 64 ||
            !arguments[4].All(Uri.IsHexDigit) ||
            !Version.TryParse(arguments[5], out var version) ||
            version.Revision >= 0 ||
            !string.Equals(version.ToString(3), arguments[5], StringComparison.Ordinal))
        {
            return false;
        }

        var installerPath = Path.GetFullPath(arguments[2]);
        var expectedFileName =
            $"Hechao-Launcher-Setup-{arguments[5]}-win-x64.exe";
        if (!string.Equals(
                Path.GetFileName(installerPath),
                expectedFileName,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        command = new LauncherUpdateBootstrapCommand(
            parentProcessId,
            installerPath,
            installerBytes,
            arguments[4].ToLowerInvariant(),
            arguments[5]);
        return true;
    }

    internal static async Task<int> ExecuteAsync(
        LauncherUpdateBootstrapCommand command,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await WaitForParentExitAsync(
                command.ParentProcessId,
                cancellationToken);
            if (!await FileHashing.MatchesAsync(
                    command.InstallerPath,
                    command.InstallerBytes,
                    command.InstallerSha256,
                    cancellationToken))
            {
                throw new InvalidDataException(
                    "The staged launcher installer failed integrity verification.");
            }

            using var installer = Process.Start(new ProcessStartInfo(
                command.InstallerPath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                ArgumentList = { "/S" }
            }) ?? throw new InvalidOperationException(
                "The launcher installer could not be started.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            timeout.CancelAfter(InstallerTimeout);
            await installer.WaitForExitAsync(timeout.Token);
            if (installer.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"The launcher installer exited with code {installer.ExitCode}.");
            }

            var installedPath = ReadInstalledLauncherPath();
            if (installedPath is null)
            {
                throw new FileNotFoundException(
                    "The installed launcher executable was not found.");
            }

            Process.Start(new ProcessStartInfo(installedPath)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(installedPath)!
            });
            TryDelete(command.InstallerPath);
            return 0;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                InvalidDataException or InvalidOperationException or
                System.ComponentModel.Win32Exception or OperationCanceledException)
        {
            TryWriteFailure(exception.Message);
            var installedPath = ReadInstalledLauncherPath();
            if (installedPath is not null)
            {
                Process.Start(new ProcessStartInfo(installedPath)
                {
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(installedPath)!
                });
            }

            return 1;
        }
    }

    private static async Task WaitForParentExitAsync(
        int processId,
        CancellationToken cancellationToken)
    {
        Process? parent;
        try
        {
            parent = Process.GetProcessById(processId);
        }
        catch (ArgumentException)
        {
            return;
        }

        using (parent)
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                   cancellationToken))
        {
            timeout.CancelAfter(ParentExitTimeout);
            await parent.WaitForExitAsync(timeout.Token);
        }
    }

    private static string? ReadInstalledLauncherPath()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
            @"Software\Hechao\Launcher");
        var installDirectory = key?.GetValue("InstallDir") as string;
        if (string.IsNullOrWhiteSpace(installDirectory))
        {
            return null;
        }

        var path = Path.Combine(
            Path.GetFullPath(installDirectory),
            "Hechao.Launcher.exe");
        return File.Exists(path) ? path : null;
    }

    private static void TryWriteFailure(string message)
    {
        try
        {
            var localApplicationData = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);
            var path = Path.Combine(
                localApplicationData,
                "Hechao",
                "Launcher",
                "updates",
                "last-update-error.log");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(
                path,
                $"{DateTimeOffset.UtcNow:O} {message}{Environment.NewLine}");
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}

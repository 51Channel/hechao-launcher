using System.IO;
using System.Net.Http;
using System.Text.Json;
using CmlLib.Core;
using CmlLib.Core.FileExtractors;
using CmlLib.Core.Java;
using CmlLib.Core.Rules;
using CmlLib.Core.VersionLoader;
using Hechao.Distribution;

namespace Hechao.Launcher.Services;

internal sealed record ProfileJavaInstallProgress(
    double Percent,
    string CurrentPath);

internal interface IProfileJavaRuntimeService
{
    Task<bool> IsReadyAsync(
        string dataRoot,
        string profileId,
        CancellationToken cancellationToken = default);

    Task InstallAsync(
        string dataRoot,
        string profileId,
        IProgress<ProfileJavaInstallProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

internal sealed class ProfileJavaRuntimeService(HttpClient httpClient)
    : IProfileJavaRuntimeService
{
    private const int RuntimeStateSchemaVersion = 1;
    private const string RuntimeStateFileName = ".hechao-java.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<bool> IsReadyAsync(
        string dataRoot,
        string profileId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var layout = new ClientStorageLayout(dataRoot);
            var gameDirectory = layout.GetProfileGameDirectory(profileId);
            var metadata = await MinecraftGameLauncherService.ReadAndValidateMetadataAsync(
                gameDirectory,
                cancellationToken);
            var statePath = GetStatePath(layout, profileId);
            if (!File.Exists(statePath))
            {
                return false;
            }

            await using var stream = File.OpenRead(statePath);
            var state = await JsonSerializer.DeserializeAsync<ProfileJavaRuntimeState>(
                stream,
                JsonOptions,
                cancellationToken);
            if (state is null ||
                state.SchemaVersion != RuntimeStateSchemaVersion ||
                state.JavaMajorVersion != metadata.JavaMajorVersion ||
                string.IsNullOrWhiteSpace(state.ExecutablePath))
            {
                return false;
            }

            var runtimeRoot = layout.GetProfileRuntimeRoot(profileId);
            var executablePath = Path.GetFullPath(
                Path.Combine(runtimeRoot, state.ExecutablePath));
            return IsWithin(runtimeRoot, executablePath) && File.Exists(executablePath);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            JsonException or ManifestFormatException or ArgumentException)
        {
            return false;
        }
    }

    public async Task InstallAsync(
        string dataRoot,
        string profileId,
        IProgress<ProfileJavaInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var layout = new ClientStorageLayout(dataRoot);
        var gameDirectory = layout.GetProfileGameDirectory(profileId);
        var metadata = await MinecraftGameLauncherService.ReadAndValidateMetadataAsync(
            gameDirectory,
            cancellationToken);
        var runtimeRoot = layout.GetProfileRuntimeRoot(profileId);
        Directory.CreateDirectory(runtimeRoot);

        progress?.Report(new ProfileJavaInstallProgress(
            0,
            $"Java {metadata.JavaMajorVersion}"));
        await SeedFromExistingRuntimeAsync(
            layout,
            profileId,
            runtimeRoot,
            metadata.JavaMajorVersion,
            cancellationToken);

        var launchRuntimeRoot = ProfileRuntimePathResolver.GetLaunchRoot(
            runtimeRoot,
            profileId);
        var minecraftPath = new MinecraftPath(gameDirectory)
        {
            Runtime = launchRuntimeRoot
        };
        var parameters = MinecraftLauncherParameters.CreateDefault(
            minecraftPath,
            httpClient);
        parameters.VersionLoader = new LocalJsonVersionLoader(minecraftPath);
        var javaPathResolver = parameters.JavaPathResolver ??
            throw new ProfileJavaRuntimeException(
                "The Java path resolver is unavailable.");
        var runtimeExtractors = new FileExtractorCollection();
        runtimeExtractors.Add(new JavaFileExtractor(httpClient, javaPathResolver));
        parameters.FileExtractors = runtimeExtractors;

        var launcher = new MinecraftLauncher(parameters);
        var fileProgress = new Progress<CmlLib.Core.Installers.InstallerProgressChangedEventArgs>(
            value =>
            {
                var ratio = value.TotalTasks <= 0
                    ? 0
                    : Math.Clamp(value.ProgressedTasks / (double)value.TotalTasks, 0, 1);
                progress?.Report(new ProfileJavaInstallProgress(
                    ratio * 100,
                    $"Java {metadata.JavaMajorVersion}"));
            });
        var byteProgress = new Progress<ByteProgress>(value =>
        {
            var ratio = value.TotalBytes <= 0
                ? 0
                : Math.Clamp(value.ProgressedBytes / (double)value.TotalBytes, 0, 1);
            progress?.Report(new ProfileJavaInstallProgress(
                ratio * 100,
                $"Java {metadata.JavaMajorVersion}"));
        });

        try
        {
            var version = await launcher.GetVersionAsync(
                metadata.VersionId,
                cancellationToken);
            await launcher.InstallAsync(
                version,
                fileProgress,
                byteProgress,
                cancellationToken);

            var rulesContext = new RulesEvaluatorContext(LauncherOSRule.Current);
            var javaVersion = version.JavaVersion ??
                throw new InvalidDataException(
                    "The Minecraft version does not declare a Java runtime.");
            var javaPath = javaPathResolver.GetJavaBinaryPath(
                javaVersion,
                rulesContext);
            var validated = await JavaRuntimeValidator.ValidateAsync(
                javaPath,
                metadata.JavaMajorVersion,
                cancellationToken);
            var relativePath = Path.GetRelativePath(
                launchRuntimeRoot,
                validated.ExecutablePath);
            var physicalExecutablePath = Path.GetFullPath(
                Path.Combine(runtimeRoot, relativePath));
            if (!IsWithin(runtimeRoot, physicalExecutablePath) ||
                !File.Exists(physicalExecutablePath))
            {
                throw new InvalidDataException(
                    "The prepared Java executable is outside the profile runtime directory.");
            }

            await WriteStateAsync(
                layout,
                profileId,
                new ProfileJavaRuntimeState(
                    RuntimeStateSchemaVersion,
                    metadata.JavaMajorVersion,
                    relativePath,
                    DateTimeOffset.UtcNow),
                cancellationToken);
            progress?.Report(new ProfileJavaInstallProgress(
                100,
                $"Java {metadata.JavaMajorVersion}"));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not ProfileJavaRuntimeException)
        {
            throw new ProfileJavaRuntimeException(
                $"Unable to install Java {metadata.JavaMajorVersion} for profile {profileId}.",
                exception);
        }
    }

    private static async Task SeedFromExistingRuntimeAsync(
        ClientStorageLayout layout,
        string profileId,
        string runtimeRoot,
        int javaMajorVersion,
        CancellationToken cancellationToken)
    {
        if (Directory.EnumerateFiles(
                runtimeRoot,
                "java.exe",
                SearchOption.AllDirectories).Any())
        {
            return;
        }

        var candidates = new List<string> { layout.RuntimeRoot };
        if (Directory.Exists(layout.InstancesRoot))
        {
            candidates.AddRange(
                Directory.EnumerateDirectories(layout.InstancesRoot)
                    .Where(path =>
                        !string.Equals(
                            Path.GetFileName(path),
                            profileId,
                            StringComparison.OrdinalIgnoreCase))
                    .Select(path => Path.Combine(
                        path,
                        ClientStorageLayout.RuntimeDirectoryName)));
        }

        foreach (var candidate in candidates
                     .Where(Directory.Exists)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidateLaunchRoot = ProfileRuntimePathResolver.GetLaunchRoot(
                candidate,
                $"seed-java-{javaMajorVersion}");
            var javaExecutables = Directory.EnumerateFiles(
                candidateLaunchRoot,
                "java.exe",
                SearchOption.AllDirectories);
            var compatible = false;
            foreach (var javaExecutable in javaExecutables)
            {
                try
                {
                    await JavaRuntimeValidator.ValidateAsync(
                        javaExecutable,
                        javaMajorVersion,
                        cancellationToken);
                    compatible = true;
                    break;
                }
                catch (JavaRuntimeValidationException)
                {
                }
            }

            if (!compatible)
            {
                continue;
            }

            await CopyDirectoryAsync(candidate, runtimeRoot, cancellationToken);
            return;
        }
    }

    private static async Task CopyDirectoryAsync(
        string sourceRoot,
        string destinationRoot,
        CancellationToken cancellationToken)
    {
        var source = new DirectoryInfo(sourceRoot);
        if ((source.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("A Java runtime source cannot be a directory link.");
        }

        foreach (var directory in source.EnumerateDirectories(
                     "*",
                     SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException("A Java runtime source contains a directory link.");
            }

            Directory.CreateDirectory(Path.Combine(
                destinationRoot,
                Path.GetRelativePath(sourceRoot, directory.FullName)));
        }

        foreach (var file in source.EnumerateFiles("*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destination = Path.Combine(
                destinationRoot,
                Path.GetRelativePath(sourceRoot, file.FullName));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using var input = new FileStream(
                file.FullName,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var output = new FileStream(
                destination,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await input.CopyToAsync(output, cancellationToken);
        }
    }

    private static async Task WriteStateAsync(
        ClientStorageLayout layout,
        string profileId,
        ProfileJavaRuntimeState state,
        CancellationToken cancellationToken)
    {
        var statePath = GetStatePath(layout, profileId);
        var temporaryPath = statePath + ".tmp";
        await using (var stream = new FileStream(
                         temporaryPath,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         16 * 1024,
                         FileOptions.Asynchronous))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                state,
                JsonOptions,
                cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        File.Move(temporaryPath, statePath, overwrite: true);
    }

    private static string GetStatePath(
        ClientStorageLayout layout,
        string profileId) =>
        Path.Combine(layout.GetProfileRoot(profileId), RuntimeStateFileName);

    private static bool IsWithin(string rootPath, string candidatePath)
    {
        var root = Path.GetFullPath(rootPath);
        var candidate = Path.GetFullPath(candidatePath);
        var prefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        return candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record ProfileJavaRuntimeState(
        int SchemaVersion,
        int JavaMajorVersion,
        string ExecutablePath,
        DateTimeOffset InstalledAt);
}

public sealed class ProfileJavaRuntimeException : IOException
{
    public ProfileJavaRuntimeException(string message)
        : base(message)
    {
    }

    public ProfileJavaRuntimeException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

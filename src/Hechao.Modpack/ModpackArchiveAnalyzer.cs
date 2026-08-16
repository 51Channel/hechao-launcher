using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Hechao.Modpack;

public static partial class ModpackArchiveAnalyzer
{
    private const int MaximumMetadataBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(
        JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly string[] ClientMarkers =
    [
        ".minecraft/", "assets/", "versions/", "launcher_profiles.json", "options.txt",
        "mmc-pack.json", "instance.cfg"
    ];

    private static readonly string[] ServerMarkers =
    [
        "server.properties", "eula.txt", "fabric-server-launch.jar",
        "user_jvm_args.txt", "run.bat", "run.sh", "start.bat", "start.ps1"
    ];

    private static readonly string[] SharedRoots =
    [
        "mods/", "config/", "defaultconfigs/", "kubejs/", "scripts/",
        "resourcepacks/", "datapacks/"
    ];

    private static readonly string[] IgnoredRoots =
    [
        "logs/", "crash-reports/", "screenshots/", "saves/", ".git/",
        ".idea/", ".vscode/"
    ];

    public static Task<ModpackAnalysisResult> AnalyzeAndSplitAsync(
        string sourceArchivePath,
        string outputDirectory,
        ModpackInspectionLimits? limits = null,
        CancellationToken cancellationToken = default) =>
        AnalyzeCoreAsync(
            sourceArchivePath,
            outputDirectory,
            limits,
            cancellationToken);

    public static Task<ModpackAnalysisResult> InspectAsync(
        string sourceArchivePath,
        ModpackInspectionLimits? limits = null,
        CancellationToken cancellationToken = default) =>
        AnalyzeCoreAsync(
            sourceArchivePath,
            outputDirectory: null,
            limits,
            cancellationToken);

    private static async Task<ModpackAnalysisResult> AnalyzeCoreAsync(
        string sourceArchivePath,
        string? outputDirectory,
        ModpackInspectionLimits? limits,
        CancellationToken cancellationToken)
    {
        limits ??= new ModpackInspectionLimits();
        limits.Validate();
        var sourcePath = Path.GetFullPath(sourceArchivePath);
        var outputRoot = outputDirectory is null
            ? null
            : Path.GetFullPath(outputDirectory);
        var clientArchivePath = outputRoot is null
            ? null
            : Path.Combine(outputRoot, "client.zip");
        var serverArchivePath = outputRoot is null
            ? null
            : Path.Combine(outputRoot, "server.zip");
        if (outputRoot is not null)
        {
            Directory.CreateDirectory(outputRoot);
            File.Delete(clientArchivePath!);
            File.Delete(serverArchivePath!);
        }

        using var archive = ZipFile.OpenRead(sourcePath);
        var issues = new List<ModpackIssue>();
        if (archive.Entries.Count > limits.MaximumEntries)
        {
            throw new InvalidDataException("The modpack contains too many archive entries.");
        }

        var entries = archive.Entries
            .Where(entry => !string.IsNullOrEmpty(entry.Name))
            .ToArray();
        var wrapper = DetectWrapper(entries);
        var rawPaths = new Dictionary<ZipArchiveEntry, string>();
        var uniqueSourcePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long expandedBytes = 0;
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                SafeZipExtractor.ValidateEntry(entry, limits, ref expandedBytes);
                var normalized = SafeArchivePath.Normalize(
                    entry.FullName,
                    limits.MaximumPathLength);
                if (wrapper is not null &&
                    normalized.StartsWith(wrapper + "/", StringComparison.OrdinalIgnoreCase))
                {
                    normalized = normalized[(wrapper.Length + 1)..];
                }

                if (!uniqueSourcePaths.Add(normalized))
                {
                    issues.Add(new ModpackIssue(
                        "PATH_COLLISION",
                        ModpackIssueSeverity.Blocking,
                        "压缩包中存在 Windows 下会冲突的同名路径。",
                        normalized));
                    continue;
                }

                rawPaths.Add(entry, normalized);
            }
            catch (Exception exception) when (
                exception is InvalidDataException or OverflowException)
            {
                issues.Add(new ModpackIssue(
                    "UNSAFE_ARCHIVE_ENTRY",
                    ModpackIssueSeverity.Blocking,
                    exception.Message,
                    entry.FullName));
            }
        }

        var descriptor = TryReadDescriptor(archive, rawPaths, issues);
        var paths = descriptor is null
            ? CanonicalizeDetectedSideRoots(rawPaths, issues)
            : rawPaths;
        var layout = DetectLayout(paths.Values, descriptor);
        var metadata = DetectMetadata(
            archive,
            paths,
            descriptor,
            layout,
            Path.GetFileNameWithoutExtension(sourcePath),
            issues);
        var classified = paths
            .Select(item => new ClassifiedEntry(
                item.Key,
                item.Value,
                Classify(item.Value, layout, descriptor)))
            .ToArray();

        AddLayoutIssues(classified, issues);
        AddSharedModWarning(classified, issues);
        var serverDeployment = InspectServerDeployment(
            classified,
            descriptor?.ServerCore,
            issues);

        var files = new List<ModpackFileRecord>(classified.Length * 2);
        ModpackArchivePart? clientPart = null;
        ModpackArchivePart? serverPart = null;
        if (outputRoot is null)
        {
            clientPart = DescribePart(classified, ModpackFileSide.Client);
            serverPart = DescribePart(classified, ModpackFileSide.Server);
        }
        else
        {
            try
            {
                clientPart = await CreatePartAsync(
                    clientArchivePath!,
                    classified,
                    ModpackFileSide.Client,
                    files,
                    cancellationToken);
                serverPart = await CreatePartAsync(
                    serverArchivePath!,
                    classified,
                    ModpackFileSide.Server,
                    files,
                    cancellationToken);
            }
            catch
            {
                File.Delete(clientArchivePath!);
                File.Delete(serverArchivePath!);
                throw;
            }
        }

        if (clientPart is null)
        {
            issues.Add(new ModpackIssue(
                "CLIENT_PART_MISSING",
                ModpackIssueSeverity.Blocking,
                "没有识别到可分发的完整客户端内容。"));
        }

        if (serverPart is null)
        {
            issues.Add(new ModpackIssue(
                "SERVER_PART_MISSING",
                ModpackIssueSeverity.Blocking,
                "没有识别到可部署的服务端内容。"));
        }

        return new ModpackAnalysisResult(
            layout,
            metadata,
            clientPart,
            serverPart,
            files,
            issues)
        {
            ServerDeployment = serverDeployment,
            SharedFileCount = classified.Count(entry =>
                entry.Side == ModpackFileSide.Shared)
        };
    }

    private static ModpackArchivePart? DescribePart(
        IReadOnlyList<ClassifiedEntry> entries,
        ModpackFileSide requestedSide)
    {
        var selected = entries
            .Where(entry => entry.Side == requestedSide ||
                            entry.Side == ModpackFileSide.Shared)
            .ToArray();
        if (selected.Length == 0)
        {
            return null;
        }

        long expandedBytes = 0;
        foreach (var item in selected)
        {
            expandedBytes = checked(expandedBytes + item.Entry.Length);
        }

        return new ModpackArchivePart(
            string.Empty,
            string.Empty,
            0,
            expandedBytes,
            selected.Length);
    }

    private static async Task<ModpackArchivePart?> CreatePartAsync(
        string outputPath,
        IReadOnlyList<ClassifiedEntry> entries,
        ModpackFileSide requestedSide,
        ICollection<ModpackFileRecord> files,
        CancellationToken cancellationToken)
    {
        var selected = entries
            .Where(entry => entry.Side == requestedSide ||
                            entry.Side == ModpackFileSide.Shared)
            .ToArray();
        if (selected.Length == 0)
        {
            return null;
        }

        var outputPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long expandedBytes = 0;
        await using (var stream = new FileStream(
                         outputPath,
                         FileMode.CreateNew,
                         FileAccess.ReadWrite,
                         FileShare.None,
                         128 * 1024,
                         FileOptions.Asynchronous | FileOptions.SequentialScan))
        using (var output = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var item in selected)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var targetPath = GetTargetPath(item, requestedSide);
                if (!outputPaths.Add(targetPath))
                {
                    throw new InvalidDataException(
                        $"Multiple source files map to the same package path: {targetPath}");
                }

                var targetEntry = output.CreateEntry(
                    targetPath,
                    CompressionLevel.NoCompression);
                targetEntry.LastWriteTime = item.Entry.LastWriteTime;
                await using var input = item.Entry.Open();
                await using var destination = targetEntry.Open();
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var copied = await SafeZipExtractor.CopyAndHashAsync(
                    input,
                    destination,
                    hash,
                    item.Entry.Length,
                    cancellationToken);
                expandedBytes = checked(expandedBytes + copied);
                files.Add(new ModpackFileRecord(
                    item.SourcePath,
                    targetPath,
                    requestedSide,
                    copied,
                    Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant()));
            }
        }

        var archiveBytes = new FileInfo(outputPath).Length;
        var archiveSha256 = await ComputeSha256Async(outputPath, cancellationToken);
        return new ModpackArchivePart(
            outputPath,
            archiveSha256,
            archiveBytes,
            expandedBytes,
            selected.Length);
    }

    private static string GetTargetPath(
        ClassifiedEntry entry,
        ModpackFileSide requestedSide)
    {
        var path = entry.SourcePath;
        var prefixes = requestedSide == ModpackFileSide.Client
            ? new[] { "client/", ".minecraft/" }
            : new[] { "server/" };
        foreach (var prefix in prefixes)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return SafeArchivePath.Normalize(path[prefix.Length..]);
            }
        }

        if (path.StartsWith("shared/", StringComparison.OrdinalIgnoreCase))
        {
            return SafeArchivePath.Normalize(path["shared/".Length..]);
        }

        return SafeArchivePath.Normalize(path);
    }

    private static ModpackFileSide Classify(
        string path,
        ModpackLayoutKind layout,
        HechaoModpackDescriptor? descriptor)
    {
        if (descriptor is not null)
        {
            if (StartsWithRoot(path, descriptor.ClientRoot))
            {
                return ModpackFileSide.Client;
            }

            if (StartsWithRoot(path, descriptor.ServerRoot))
            {
                return ModpackFileSide.Server;
            }

            if (StartsWithRoot(path, descriptor.SharedRoot))
            {
                return ModpackFileSide.Shared;
            }
        }

        if (path.Equals("hechao-pack.json", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("manifest.json", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("modrinth.index.json", StringComparison.OrdinalIgnoreCase))
        {
            return ModpackFileSide.Ignored;
        }

        if (path.StartsWith("client/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(".minecraft/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("overrides/", StringComparison.OrdinalIgnoreCase))
        {
            return ModpackFileSide.Client;
        }

        if (path.StartsWith("server/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("server-overrides/", StringComparison.OrdinalIgnoreCase))
        {
            return ModpackFileSide.Server;
        }

        if (path.StartsWith("shared/", StringComparison.OrdinalIgnoreCase) ||
            SharedRoots.Any(root => path.StartsWith(root, StringComparison.OrdinalIgnoreCase)))
        {
            return layout switch
            {
                ModpackLayoutKind.ClientOnly or ModpackLayoutKind.CurseForge or
                    ModpackLayoutKind.Modrinth => ModpackFileSide.Client,
                ModpackLayoutKind.ServerOnly => ModpackFileSide.Server,
                _ => ModpackFileSide.Shared
            };
        }

        if (IgnoredRoots.Any(root => path.StartsWith(root, StringComparison.OrdinalIgnoreCase)))
        {
            return ModpackFileSide.Ignored;
        }

        if (IsClientMarker(path))
        {
            return ModpackFileSide.Client;
        }

        if (IsServerMarker(path) || IsRootServerJar(path))
        {
            return ModpackFileSide.Server;
        }

        return layout switch
        {
            ModpackLayoutKind.ClientOnly or ModpackLayoutKind.CurseForge or
                ModpackLayoutKind.Modrinth => ModpackFileSide.Client,
            ModpackLayoutKind.ServerOnly => ModpackFileSide.Server,
            _ => ModpackFileSide.Shared
        };
    }

    private static ModpackLayoutKind DetectLayout(
        IEnumerable<string> paths,
        HechaoModpackDescriptor? descriptor)
    {
        var values = paths.ToArray();
        if (descriptor is not null ||
            values.Any(path => path.StartsWith("client/", StringComparison.OrdinalIgnoreCase)) &&
            values.Any(path => path.StartsWith("server/", StringComparison.OrdinalIgnoreCase)))
        {
            return ModpackLayoutKind.Canonical;
        }

        if (values.Any(path => path.Equals("modrinth.index.json", StringComparison.OrdinalIgnoreCase)))
        {
            return ModpackLayoutKind.Modrinth;
        }

        if (values.Any(path => path.Equals("manifest.json", StringComparison.OrdinalIgnoreCase)))
        {
            return ModpackLayoutKind.CurseForge;
        }

        var hasClient = values.Any(IsClientMarker);
        var hasServer = values.Any(IsServerLayoutMarker);
        return (hasClient, hasServer) switch
        {
            (true, true) => ModpackLayoutKind.Combined,
            (true, false) => ModpackLayoutKind.ClientOnly,
            (false, true) => ModpackLayoutKind.ServerOnly,
            _ => ModpackLayoutKind.Unknown
        };
    }

    private static ModpackDetectedMetadata DetectMetadata(
        ZipArchive archive,
        IReadOnlyDictionary<ZipArchiveEntry, string> paths,
        HechaoModpackDescriptor? descriptor,
        ModpackLayoutKind layout,
        string archiveName,
        ICollection<ModpackIssue> issues)
    {
        if (descriptor is not null)
        {
            ValidateDescriptor(descriptor, issues);
            return new ModpackDetectedMetadata(
                Slug(descriptor.Id),
                descriptor.DisplayName.Trim(),
                descriptor.Version.Trim(),
                descriptor.MinecraftVersion.Trim(),
                descriptor.JavaMajorVersion,
                NormalizeLoader(descriptor.Loader),
                descriptor.LoaderVersion.Trim(),
                TryReadMaximumPlayers(archive, paths),
                DetectLaunchPath(paths.Values));
        }

        string displayName = archiveName;
        string version = DetectSemanticVersion(archiveName) ?? "1.0.0";
        string minecraftVersion = string.Empty;
        string loader = "Unknown";
        string loaderVersion = string.Empty;

        var modrinth = TryReadJson(archive, paths, "modrinth.index.json", issues);
        if (modrinth is not null)
        {
            displayName = GetString(modrinth.RootElement, "name") ?? displayName;
            version = GetString(modrinth.RootElement, "versionId") ?? version;
            if (modrinth.RootElement.TryGetProperty("dependencies", out var dependencies))
            {
                minecraftVersion = GetString(dependencies, "minecraft") ?? minecraftVersion;
                (loader, loaderVersion) = DetectLoader(dependencies);
            }

            issues.Add(new ModpackIssue(
                "MODRINTH_REMOTE_FILES",
                ModpackIssueSeverity.Blocking,
                "Modrinth 档案中的远程文件尚未下载；请上传包含完整 client/server 目录的整合包。"));
        }

        var curseForge = TryReadJson(archive, paths, "manifest.json", issues);
        if (curseForge is not null &&
            curseForge.RootElement.TryGetProperty("minecraft", out var minecraft))
        {
            displayName = GetString(curseForge.RootElement, "name") ?? displayName;
            version = GetString(curseForge.RootElement, "version") ?? version;
            minecraftVersion = GetString(minecraft, "version") ?? minecraftVersion;
            if (minecraft.TryGetProperty("modLoaders", out var loaders) &&
                loaders.ValueKind == JsonValueKind.Array)
            {
                var id = loaders.EnumerateArray()
                    .Select(item => GetString(item, "id"))
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
                if (id is not null)
                {
                    (loader, loaderVersion) = ParseLoaderId(id);
                }
            }

            issues.Add(new ModpackIssue(
                "CURSEFORGE_REMOTE_FILES",
                ModpackIssueSeverity.Blocking,
                "CurseForge 导出包只包含项目引用；请上传包含完整 client/server 目录的整合包。"));
        }

        if (string.IsNullOrWhiteSpace(minecraftVersion))
        {
            minecraftVersion = DetectMinecraftVersion(paths.Values, archiveName) ?? "Unknown";
        }

        if (loader == "Unknown")
        {
            (loader, loaderVersion) = DetectLoaderFromPaths(paths.Values);
        }

        if (layout == ModpackLayoutKind.Unknown)
        {
            issues.Add(new ModpackIssue(
                "LAYOUT_UNKNOWN",
                ModpackIssueSeverity.Blocking,
                "无法确定整合包布局；请使用 client、server、shared 三个顶层目录。"));
        }

        if (minecraftVersion == "Unknown")
        {
            issues.Add(new ModpackIssue(
                "MINECRAFT_VERSION_UNKNOWN",
                ModpackIssueSeverity.Blocking,
                "无法识别 Minecraft 版本，请在 hechao-pack.json 中声明。"));
        }

        if (loader == "Unknown")
        {
            issues.Add(new ModpackIssue(
                "LOADER_UNKNOWN",
                ModpackIssueSeverity.Blocking,
                "无法识别加载器，请在 hechao-pack.json 中声明。"));
        }

        return new ModpackDetectedMetadata(
            Slug($"{displayName}-{loader}-{minecraftVersion}"),
            displayName,
            version,
            minecraftVersion,
            InferJavaMajorVersion(minecraftVersion),
            loader,
            loaderVersion,
            TryReadMaximumPlayers(archive, paths),
            DetectLaunchPath(paths.Values));
    }

    private static HechaoModpackDescriptor? TryReadDescriptor(
        ZipArchive archive,
        IReadOnlyDictionary<ZipArchiveEntry, string> paths,
        ICollection<ModpackIssue> issues)
    {
        var document = TryReadJson(archive, paths, "hechao-pack.json", issues);
        if (document is null)
        {
            return null;
        }

        try
        {
            return document.RootElement.Deserialize<HechaoModpackDescriptor>(JsonOptions);
        }
        catch (JsonException exception)
        {
            issues.Add(new ModpackIssue(
                "DESCRIPTOR_INVALID",
                ModpackIssueSeverity.Blocking,
                $"hechao-pack.json 无法解析：{exception.Message}",
                "hechao-pack.json"));
            return null;
        }
    }

    private static JsonDocument? TryReadJson(
        ZipArchive archive,
        IReadOnlyDictionary<ZipArchiveEntry, string> paths,
        string expectedPath,
        ICollection<ModpackIssue> issues)
    {
        var entry = paths.FirstOrDefault(item =>
            item.Value.Equals(expectedPath, StringComparison.OrdinalIgnoreCase)).Key;
        if (entry is null)
        {
            return null;
        }

        if (entry.Length is <= 0 or > MaximumMetadataBytes)
        {
            issues.Add(new ModpackIssue(
                "METADATA_SIZE_INVALID",
                ModpackIssueSeverity.Blocking,
                "整合包元数据文件大小无效。",
                expectedPath));
            return null;
        }

        try
        {
            using var stream = entry.Open();
            return JsonDocument.Parse(stream, new JsonDocumentOptions
            {
                MaxDepth = 64,
                CommentHandling = JsonCommentHandling.Disallow,
                AllowTrailingCommas = false
            });
        }
        catch (JsonException exception)
        {
            issues.Add(new ModpackIssue(
                "METADATA_INVALID",
                ModpackIssueSeverity.Blocking,
                $"整合包元数据无法解析：{exception.Message}",
                expectedPath));
            return null;
        }
    }

    private static int? TryReadMaximumPlayers(
        ZipArchive archive,
        IReadOnlyDictionary<ZipArchiveEntry, string> paths)
    {
        var entry = paths.FirstOrDefault(item =>
            item.Value.Equals("server.properties", StringComparison.OrdinalIgnoreCase) ||
            item.Value.EndsWith("/server.properties", StringComparison.OrdinalIgnoreCase)).Key;
        if (entry is null || entry.Length is <= 0 or > 1024 * 1024)
        {
            return null;
        }

        using var reader = new StreamReader(
            entry.Open(),
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            leaveOpen: false);
        while (reader.ReadLine() is { } line)
        {
            if (!line.StartsWith("max-players=", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return int.TryParse(
                line["max-players=".Length..].Trim(),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var maximumPlayers) && maximumPlayers is >= 1 and <= 1000
                ? maximumPlayers
                : null;
        }

        return null;
    }

    private static void ValidateDescriptor(
        HechaoModpackDescriptor descriptor,
        ICollection<ModpackIssue> issues)
    {
        if (descriptor.SchemaVersion != 1 ||
            string.IsNullOrWhiteSpace(descriptor.Id) ||
            string.IsNullOrWhiteSpace(descriptor.DisplayName) ||
            string.IsNullOrWhiteSpace(descriptor.Version) ||
            string.IsNullOrWhiteSpace(descriptor.MinecraftVersion) ||
            descriptor.JavaMajorVersion is < 8 or > 30 ||
            string.IsNullOrWhiteSpace(descriptor.Loader) ||
            string.IsNullOrWhiteSpace(descriptor.LoaderVersion))
        {
            issues.Add(new ModpackIssue(
                "DESCRIPTOR_INCOMPLETE",
                ModpackIssueSeverity.Blocking,
                "hechao-pack.json 缺少有效的版本、加载器或 Java 元数据。",
                "hechao-pack.json"));
        }
    }

    private static void AddLayoutIssues(
        IReadOnlyList<ClassifiedEntry> entries,
        ICollection<ModpackIssue> issues)
    {
        var rejected = entries.Where(item => item.Side == ModpackFileSide.Rejected).ToArray();
        foreach (var item in rejected.Take(20))
        {
            issues.Add(new ModpackIssue(
                "FILE_REJECTED",
                ModpackIssueSeverity.Blocking,
                "文件不能安全地归入客户端或服务端。",
                item.SourcePath));
        }
    }

    private static void AddSharedModWarning(
        IReadOnlyList<ClassifiedEntry> entries,
        ICollection<ModpackIssue> issues)
    {
        var sharedJars = entries.Count(item =>
            item.Side == ModpackFileSide.Shared &&
            item.SourcePath.StartsWith("mods/", StringComparison.OrdinalIgnoreCase) &&
            item.SourcePath.EndsWith(".jar", StringComparison.OrdinalIgnoreCase));
        if (sharedJars > 0)
        {
            issues.Add(new ModpackIssue(
                "SHARED_MODS_REQUIRE_VERIFICATION",
                ModpackIssueSeverity.Warning,
                $"{sharedJars} 个模组被同时分配到客户端和服务端，发布前仍需专用服务端启动验证。"));
        }
    }

    private static ServerDeploymentInspection? InspectServerDeployment(
        IReadOnlyList<ClassifiedEntry> entries,
        string? declaredCore,
        ICollection<ModpackIssue> issues)
    {
        if (!entries.Any(entry => entry.Side == ModpackFileSide.Server))
        {
            return null;
        }

        var serverFiles = new Dictionary<string, ZipArchiveEntry>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries.Where(entry =>
                     entry.Side is ModpackFileSide.Server or ModpackFileSide.Shared))
        {
            var targetPath = GetTargetPath(entry, ModpackFileSide.Server);
            serverFiles.TryAdd(targetPath, entry.Entry);
        }

        var inspection = ServerDeploymentComplianceInspector.Inspect(
            serverFiles,
            declaredCore);
        foreach (var check in inspection.Checks.Where(check =>
                     check.Status != DeploymentCheckStatus.Passed))
        {
            issues.Add(new ModpackIssue(
                check.Code,
                check.Status == DeploymentCheckStatus.Blocking
                    ? ModpackIssueSeverity.Blocking
                    : ModpackIssueSeverity.Warning,
                check.Message,
                check.Path));
        }

        return inspection;
    }

    private static string? DetectWrapper(IEnumerable<ZipArchiveEntry> entries)
    {
        var paths = entries
            .Select(entry => entry.FullName.Replace('\\', '/').Trim('/'))
            .Where(path => path.Contains('/'))
            .ToArray();
        if (paths.Length == 0)
        {
            return null;
        }

        var first = paths[0].Split('/')[0];
        if (new[] { "client", "server", "shared", ".minecraft", "overrides", "server-overrides" }
            .Contains(first, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        return paths.All(path => path.StartsWith(first + "/", StringComparison.OrdinalIgnoreCase))
            ? first
            : null;
    }

    private static Dictionary<ZipArchiveEntry, string> CanonicalizeDetectedSideRoots(
        IReadOnlyDictionary<ZipArchiveEntry, string> paths,
        ICollection<ModpackIssue> issues)
    {
        var roots = DetectSideRoots(paths.Values);
        if (roots is null)
        {
            return paths.ToDictionary(item => item.Key, item => item.Value);
        }

        var result = new Dictionary<ZipArchiveEntry, string>();
        var uniquePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in paths)
        {
            var normalized = CanonicalizeSideRoot(item.Value, roots);
            if (!uniquePaths.Add(normalized))
            {
                issues.Add(new ModpackIssue(
                    "PATH_COLLISION",
                    ModpackIssueSeverity.Blocking,
                    "客户端与服务端目录归一化后存在 Windows 下会冲突的同名路径。",
                    normalized));
                continue;
            }

            result.Add(item.Key, normalized);
        }

        return result;
    }

    private static DetectedSideRoots? DetectSideRoots(IEnumerable<string> paths)
    {
        var groups = paths
            .Select(path =>
            {
                var separator = path.IndexOf('/');
                return separator <= 0
                    ? null
                    : new
                    {
                        Root = path[..separator],
                        RelativePath = path[(separator + 1)..]
                    };
            })
            .Where(item => item is not null)
            .GroupBy(item => item!.Root, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                Root = group.Key,
                Paths = group.Select(item => item!.RelativePath).ToArray()
            })
            .ToArray();
        var clientRoots = groups
            .Where(group => group.Paths.Any(IsClientMarker))
            .Select(group => group.Root)
            .ToArray();
        var serverRoots = groups
            .Where(group => group.Paths.Any(IsServerLayoutMarker))
            .Select(group => group.Root)
            .ToArray();

        return clientRoots.Length == 1 && serverRoots.Length == 1 &&
               !clientRoots[0].Equals(serverRoots[0], StringComparison.OrdinalIgnoreCase)
            ? new DetectedSideRoots(clientRoots[0], serverRoots[0])
            : null;
    }

    private static string CanonicalizeSideRoot(string path, DetectedSideRoots roots)
    {
        if (StartsWithRoot(path, roots.ClientRoot))
        {
            var relativePath = path[(roots.ClientRoot.Length + 1)..];
            const string minecraftRoot = ".minecraft/";
            if (relativePath.StartsWith(minecraftRoot, StringComparison.OrdinalIgnoreCase))
            {
                relativePath = relativePath[minecraftRoot.Length..];
            }

            return SafeArchivePath.Normalize("client/" + relativePath);
        }

        if (StartsWithRoot(path, roots.ServerRoot))
        {
            return SafeArchivePath.Normalize(
                "server/" + path[(roots.ServerRoot.Length + 1)..]);
        }

        return path;
    }

    private static bool StartsWithRoot(string path, string root)
    {
        var normalizedRoot = root.Replace('\\', '/').Trim('/');
        return normalizedRoot.Length > 0 &&
               path.StartsWith(normalizedRoot + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsClientMarker(string path) =>
        ClientMarkers.Any(marker =>
            marker.EndsWith('/')
                ? path.StartsWith(marker, StringComparison.OrdinalIgnoreCase)
                : path.Equals(marker, StringComparison.OrdinalIgnoreCase));

    private static bool IsServerMarker(string path) =>
        ServerMarkers.Any(marker =>
            path.Equals(marker, StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith('/' + marker, StringComparison.OrdinalIgnoreCase));

    private static bool IsServerJar(string path)
    {
        var fileName = Path.GetFileName(path);
        return fileName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) &&
               (fileName.StartsWith("paper-", StringComparison.OrdinalIgnoreCase) ||
                fileName.StartsWith("purpur-", StringComparison.OrdinalIgnoreCase) ||
                fileName.StartsWith("server", StringComparison.OrdinalIgnoreCase) ||
                fileName.StartsWith("minecraft_server", StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals("forge.jar", StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals("neoforge.jar", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsRootServerJar(string path) =>
        !path.Contains('/') && IsServerJar(path);

    private static bool IsServerLayoutMarker(string path) =>
        ServerMarkers.Any(marker => path.Equals(marker, StringComparison.OrdinalIgnoreCase)) ||
        IsRootServerJar(path);

    private static string? DetectLaunchPath(IEnumerable<string> paths)
    {
        var values = paths.ToArray();
        foreach (var preferred in new[]
                 {
                     "start.bat", "run.bat", "start.ps1",
                     "fabric-server-launch.jar", "server.jar"
                 })
        {
            var match = values.FirstOrDefault(path =>
                path.Equals(preferred, StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith('/' + preferred, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }

        return values.FirstOrDefault(IsServerJar);
    }

    private static (string Loader, string Version) DetectLoader(JsonElement dependencies)
    {
        foreach (var property in new[]
                 {
                     ("neoforge", "NeoForge"),
                     ("forge", "Forge"),
                     ("fabric-loader", "Fabric"),
                     ("quilt-loader", "Quilt")
                 })
        {
            var version = GetString(dependencies, property.Item1);
            if (!string.IsNullOrWhiteSpace(version))
            {
                return (property.Item2, version);
            }
        }

        return ("Vanilla", "Vanilla");
    }

    private static (string Loader, string Version) ParseLoaderId(string value)
    {
        var separator = value.IndexOf('-');
        if (separator <= 0 || separator == value.Length - 1)
        {
            return (NormalizeLoader(value), string.Empty);
        }

        return (
            NormalizeLoader(value[..separator]),
            value[(separator + 1)..]);
    }

    private static (string Loader, string Version) DetectLoaderFromPaths(
        IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            var normalized = path.Replace('\\', '/');
            var neoForge = NeoForgePath().Match(normalized);
            if (neoForge.Success)
            {
                return ("NeoForge", neoForge.Groups[1].Value);
            }

            var forge = ForgePath().Match(normalized);
            if (forge.Success)
            {
                return ("Forge", forge.Groups[1].Value);
            }

            if (normalized.EndsWith("fabric-server-launch.jar", StringComparison.OrdinalIgnoreCase) ||
                normalized.EndsWith("fabric-loader.jar", StringComparison.OrdinalIgnoreCase))
            {
                return ("Fabric", string.Empty);
            }

            if (normalized.Contains("paper", StringComparison.OrdinalIgnoreCase))
            {
                return ("Paper", string.Empty);
            }

            if (normalized.Contains("purpur", StringComparison.OrdinalIgnoreCase))
            {
                return ("Purpur", string.Empty);
            }
        }

        return ("Unknown", string.Empty);
    }

    private static string? DetectMinecraftVersion(
        IEnumerable<string> paths,
        string archiveName)
    {
        foreach (var candidate in paths.Append(archiveName))
        {
            var match = MinecraftVersion().Match(candidate);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
        }

        return null;
    }

    private static int InferJavaMajorVersion(string minecraftVersion)
    {
        if (!Version.TryParse(minecraftVersion, out var version))
        {
            return 21;
        }

        if (version.Major > 1 || version.Minor >= 20 && version.Build >= 5 || version.Minor >= 21)
        {
            return 21;
        }

        return version.Minor >= 18 ? 17 : 8;
    }

    private static string NormalizeLoader(string value) =>
        value.Trim().ToLowerInvariant() switch
        {
            "neoforge" => "NeoForge",
            "forge" => "Forge",
            "fabric" or "fabric-loader" => "Fabric",
            "quilt" or "quilt-loader" => "Quilt",
            "paper" => "Paper",
            "purpur" => "Purpur",
            "vanilla" => "Vanilla",
            _ => value.Trim()
        };

    private static string? DetectSemanticVersion(string value)
    {
        var matches = SemanticVersion().Matches(value);
        return matches.Count == 0 ? null : matches[^1].Groups[1].Value;
    }

    private static string Slug(string value)
    {
        var normalized = SlugInvalid().Replace(value.Trim().ToLowerInvariant(), "-")
            .Trim('-');
        if (normalized.Length < 2)
        {
            normalized = "modpack";
        }

        return normalized.Length <= 64 ? normalized : normalized[..64].TrimEnd('-');
    }

    private static string? GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(
            await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
    }

    private sealed record ClassifiedEntry(
        ZipArchiveEntry Entry,
        string SourcePath,
        ModpackFileSide Side);

    private sealed record DetectedSideRoots(string ClientRoot, string ServerRoot);

    [GeneratedRegex(@"libraries/net/neoforged/neoforge/([^/]+)/", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NeoForgePath();

    [GeneratedRegex(@"libraries/net/minecraftforge/forge/[^/]*-([^/]+)/", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ForgePath();

    [GeneratedRegex(@"(?<!\d)(1\.\d{1,2}(?:\.\d{1,2})?)(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex MinecraftVersion();

    [GeneratedRegex(@"(?<!\d)(\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?)(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex SemanticVersion();

    [GeneratedRegex("[^a-z0-9._-]+", RegexOptions.CultureInvariant)]
    private static partial Regex SlugInvalid();
}

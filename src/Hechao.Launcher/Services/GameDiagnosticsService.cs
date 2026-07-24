using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hechao.Distribution;

namespace Hechao.Launcher.Services;

public sealed record GameExitRecord(
    Guid Id,
    string ProfileId,
    int ProcessId,
    int? ExitCode,
    DateTimeOffset StartedAt,
    DateTimeOffset ExitedAt);

public sealed record GameDiagnosticBundleRequest(
    string DataRoot,
    string ProfileId,
    GameExitRecord? LastExit,
    IReadOnlyCollection<string> SensitiveValues);

public sealed record GameDiagnosticBundleResult(
    string BundlePath,
    bool IncludedLatestLog,
    bool IncludedCrashReport,
    long Size);

public interface IGameDiagnosticsService
{
    string DiagnosticsDirectory { get; }

    GameExitRecord? LoadLatestExit();

    Task RecordExitAsync(
        GameExitRecord record,
        CancellationToken cancellationToken = default);

    Task<GameDiagnosticBundleResult> CreateBundleAsync(
        GameDiagnosticBundleRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class JsonGameDiagnosticsService : IGameDiagnosticsService
{
    private const int HistoryLimit = 20;
    private const int BundleRetentionLimit = 10;
    private const int MaximumTextBytes = 512 * 1024;
    private const string DiagnosticFilePrefix = "Hechao-Diagnostic-";
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static readonly Regex[] SensitivePatterns =
    [
        CreateRegex(@"(?i)\bBearer\s+[A-Za-z0-9._~+/=-]{8,}"),
        CreateRegex(@"(?i)\bXBL3\.0\s+x=[^;\s]+;[^\s""']+"),
        CreateRegex(
            @"(?i)(?:--accessToken\s+|(?:access[_-]?token|refresh[_-]?token|authorization|client[_-]?secret|identity[_-]?token|password)[""']?\s*[:=]\s*[""']?)[^""'\s,}]+"),
        CreateRegex(@"\beyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\b"),
        CreateRegex(@"(?i)\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b"),
        CreateRegex(@"(?i)\b[0-9a-f]{32}\b"),
        CreateRegex(@"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", RegexOptions.IgnoreCase),
        CreateRegex(@"\b(?:\d{1,3}\.){3}\d{1,3}\b"),
        CreateRegex(@"\b\d{14,20}\b")
    ];

    private readonly string _historyPath;
    private readonly SemaphoreSlim _historyGate = new(1, 1);

    public JsonGameDiagnosticsService()
        : this(GetDefaultLauncherDataDirectory())
    {
    }

    internal JsonGameDiagnosticsService(string launcherDataDirectory)
    {
        var normalizedRoot = Path.GetFullPath(launcherDataDirectory);
        _historyPath = Path.Combine(normalizedRoot, "game-exits.json");
        DiagnosticsDirectory = Path.Combine(normalizedRoot, "diagnostics");
    }

    public string DiagnosticsDirectory { get; }

    public GameExitRecord? LoadLatestExit() =>
        LoadHistory().OrderByDescending(record => record.ExitedAt).FirstOrDefault();

    public async Task RecordExitAsync(
        GameExitRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        ManifestValidator.ValidateProfileId(record.ProfileId);
        if (record.ProcessId <= 0 || record.ExitedAt < record.StartedAt)
        {
            throw new ArgumentException("The game exit record is invalid.", nameof(record));
        }

        await _historyGate.WaitAsync(cancellationToken);
        try
        {
            var history = LoadHistory()
                .Where(existing => existing.Id != record.Id)
                .Prepend(record)
                .OrderByDescending(existing => existing.ExitedAt)
                .Take(HistoryLimit)
                .ToArray();
            await WriteJsonAtomicallyAsync(_historyPath, history, cancellationToken);
        }
        finally
        {
            _historyGate.Release();
        }
    }

    public async Task<GameDiagnosticBundleResult> CreateBundleAsync(
        GameDiagnosticBundleRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ManifestValidator.ValidateProfileId(request.ProfileId);
        ArgumentNullException.ThrowIfNull(request.SensitiveValues);
        if (request.LastExit is not null &&
            !string.Equals(
                request.LastExit.ProfileId,
                request.ProfileId,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The exit record does not belong to the requested profile.",
                nameof(request));
        }

        var layout = new ClientStorageLayout(request.DataRoot);
        var profileDirectory = layout.GetProfileRoot(request.ProfileId);
        var gameDirectory = layout.GetProfileGameDirectory(request.ProfileId);
        EnsureSafeDirectory(profileDirectory);
        EnsureSafeDirectory(gameDirectory);

        Directory.CreateDirectory(DiagnosticsDirectory);
        EnsureSafeDirectory(DiagnosticsDirectory);
        var createdAt = DateTimeOffset.UtcNow;
        var bundleName =
            $"{DiagnosticFilePrefix}{createdAt:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.zip";
        var bundlePath = Path.Combine(DiagnosticsDirectory, bundleName);
        var temporaryPath = bundlePath + ".tmp";
        var redactor = new DiagnosticTextRedactor(
            layout.DataRoot,
            request.SensitiveValues);

        var latestLogPath = GetSafeFile(
            Path.Combine(gameDirectory, "logs"),
            "latest.log");
        var crashReportPath = FindLatestSafeCrashReport(
            Path.Combine(gameDirectory, "crash-reports"));
        var includedLatestLog = false;
        var includedCrashReport = false;

        try
        {
            using (var archive = ZipFile.Open(temporaryPath, ZipArchiveMode.Create))
            {
                var metadata = new DiagnosticMetadata(
                    SchemaVersion: 1,
                    CreatedAt: createdAt,
                    LauncherVersion: LauncherProductInfo.Version,
                    ProfileId: request.ProfileId,
                    InstalledProfileVersion: ReadInstalledProfileVersion(layout, request.ProfileId),
                    OperatingSystem: RuntimeInformation.OSDescription,
                    ProcessArchitecture: RuntimeInformation.ProcessArchitecture.ToString(),
                    Framework: RuntimeInformation.FrameworkDescription,
                    LastExit: request.LastExit is null
                        ? null
                        : new DiagnosticExitMetadata(
                            request.LastExit.ProcessId,
                            request.LastExit.ExitCode,
                            request.LastExit.StartedAt,
                            request.LastExit.ExitedAt));
                await WriteTextEntryAsync(
                    archive,
                    "diagnostic.json",
                    JsonSerializer.Serialize(metadata, JsonOptions),
                    cancellationToken);
                await WriteTextEntryAsync(
                    archive,
                    "README.txt",
                    "此诊断包由玩家主动生成，仅包含脱敏后的启动信息、latest.log 和最新崩溃报告。" +
                    "不会包含世界存档、账号密码或 Microsoft/Minecraft 访问令牌，也不会自动上传。",
                    cancellationToken);

                if (latestLogPath is not null)
                {
                    var latestLog = await ReadTailTextAsync(latestLogPath, cancellationToken);
                    await WriteTextEntryAsync(
                        archive,
                        "logs/latest.log",
                        redactor.Redact(latestLog),
                        cancellationToken);
                    includedLatestLog = true;
                }

                if (crashReportPath is not null)
                {
                    var crashReport = await ReadTailTextAsync(crashReportPath, cancellationToken);
                    await WriteTextEntryAsync(
                        archive,
                        $"crash-reports/{Path.GetFileName(crashReportPath)}",
                        redactor.Redact(crashReport),
                        cancellationToken);
                    includedCrashReport = true;
                }
            }

            File.Move(temporaryPath, bundlePath, overwrite: false);
            TrimOldBundles(bundlePath);
            return new GameDiagnosticBundleResult(
                bundlePath,
                includedLatestLog,
                includedCrashReport,
                new FileInfo(bundlePath).Length);
        }
        catch
        {
            TryDeleteFile(temporaryPath);
            throw;
        }
    }

    private IReadOnlyList<GameExitRecord> LoadHistory()
    {
        if (!File.Exists(_historyPath))
        {
            return [];
        }

        try
        {
            var file = new FileInfo(_historyPath);
            if (file.Length is <= 0 or > 256 * 1024)
            {
                return [];
            }

            return JsonSerializer.Deserialize<GameExitRecord[]>(
                       File.ReadAllText(_historyPath),
                       JsonOptions) ??
                   [];
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    private static string? ReadInstalledProfileVersion(
        ClientStorageLayout layout,
        string profileId)
    {
        var statePath = Path.Combine(
            layout.GetProfileRoot(profileId),
            ClientStorageLayout.InstallStateFileName);
        if (!File.Exists(statePath))
        {
            return null;
        }

        try
        {
            var stateFile = new FileInfo(statePath);
            if (stateFile.Length is <= 0 or > 64 * 1024)
            {
                return null;
            }

            var state = JsonSerializer.Deserialize<InstalledProfileState>(
                File.ReadAllText(statePath),
                JsonOptions);
            return state?.Version;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static string? GetSafeFile(string directory, string fileName)
    {
        if (!Directory.Exists(directory))
        {
            return null;
        }

        EnsureSafeDirectory(directory);
        var path = Path.Combine(directory, fileName);
        if (!File.Exists(path))
        {
            return null;
        }

        EnsureSafeFile(path);
        return path;
    }

    private static string? FindLatestSafeCrashReport(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return null;
        }

        EnsureSafeDirectory(directory);
        foreach (var path in Directory
                     .EnumerateFiles(directory, "crash-*.txt", SearchOption.TopDirectoryOnly)
                     .OrderByDescending(File.GetLastWriteTimeUtc))
        {
            EnsureSafeFile(path);
            return path;
        }

        return null;
    }

    private static async Task<string> ReadTailTextAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var truncated = stream.Length > MaximumTextBytes;
        if (truncated)
        {
            stream.Seek(-MaximumTextBytes, SeekOrigin.End);
        }

        var length = checked((int)Math.Min(stream.Length, MaximumTextBytes));
        var buffer = new byte[length];
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(
                buffer.AsMemory(offset, buffer.Length - offset),
                cancellationToken);
            if (read == 0)
            {
                break;
            }

            offset += read;
        }

        var text = Encoding.UTF8.GetString(buffer, 0, offset);
        if (truncated)
        {
            var firstLineBreak = text.IndexOf('\n');
            if (firstLineBreak >= 0 && firstLineBreak + 1 < text.Length)
            {
                text = text[(firstLineBreak + 1)..];
            }

            text = "[Earlier log content omitted]\n" + text;
        }

        return text;
    }

    private static async Task WriteTextEntryAsync(
        ZipArchive archive,
        string entryName,
        string content,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        entry.LastWriteTime = DateTimeOffset.UtcNow;
        await using var stream = entry.Open();
        await using var writer = new StreamWriter(
            stream,
            Utf8WithoutBom,
            16 * 1024,
            leaveOpen: false);
        await writer.WriteAsync(content.AsMemory(), cancellationToken);
        await writer.FlushAsync(cancellationToken);
    }

    private static async Task WriteJsonAtomicallyAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".tmp";
        try
        {
            await File.WriteAllTextAsync(
                temporaryPath,
                JsonSerializer.Serialize(value, JsonOptions),
                Utf8WithoutBom,
                cancellationToken);
            File.Move(temporaryPath, path, overwrite: true);
        }
        catch
        {
            TryDeleteFile(temporaryPath);
            throw;
        }
    }

    private void TrimOldBundles(string currentBundlePath)
    {
        try
        {
            foreach (var path in Directory
                         .EnumerateFiles(
                             DiagnosticsDirectory,
                             DiagnosticFilePrefix + "*.zip",
                             SearchOption.TopDirectoryOnly)
                         .Where(path => !string.Equals(
                             path,
                             currentBundlePath,
                             StringComparison.OrdinalIgnoreCase))
                         .OrderByDescending(File.GetLastWriteTimeUtc)
                         .Skip(BundleRetentionLimit - 1))
            {
                TryDeleteFile(path);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void EnsureSafeDirectory(string path)
    {
        var directory = new DirectoryInfo(path);
        if (!directory.Exists)
        {
            throw new DirectoryNotFoundException(path);
        }

        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                "The diagnostic source contains an unsupported directory link.");
        }
    }

    private static void EnsureSafeFile(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists || (file.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                "The diagnostic source contains an unsupported file link.");
        }
    }

    private static Regex CreateRegex(
        string pattern,
        RegexOptions options = RegexOptions.IgnoreCase) =>
        new(
            pattern,
            options | RegexOptions.Compiled | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));

    private static string GetDefaultLauncherDataDirectory()
    {
        var localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localApplicationData, "Hechao", "Launcher");
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record DiagnosticMetadata(
        int SchemaVersion,
        DateTimeOffset CreatedAt,
        string LauncherVersion,
        string ProfileId,
        string? InstalledProfileVersion,
        string OperatingSystem,
        string ProcessArchitecture,
        string Framework,
        DiagnosticExitMetadata? LastExit);

    private sealed record DiagnosticExitMetadata(
        int ProcessId,
        int? ExitCode,
        DateTimeOffset StartedAt,
        DateTimeOffset ExitedAt);

    private sealed class DiagnosticTextRedactor
    {
        private readonly string[] _sensitiveValues;

        public DiagnosticTextRedactor(
            string dataRoot,
            IReadOnlyCollection<string> sensitiveValues)
        {
            var values = new List<string>
            {
                ClientStorageLayout.NormalizeRoot(dataRoot),
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                Environment.UserName,
                Environment.MachineName
            };
            values.AddRange(sensitiveValues);
            _sensitiveValues = values
                .Where(value => !string.IsNullOrWhiteSpace(value) && value.Trim().Length >= 3)
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(value => value.Length)
                .ToArray();
        }

        public string Redact(string content)
        {
            var redacted = content;
            foreach (var sensitiveValue in _sensitiveValues)
            {
                redacted = redacted.Replace(
                    sensitiveValue,
                    "[REDACTED]",
                    StringComparison.OrdinalIgnoreCase);
                redacted = redacted.Replace(
                    sensitiveValue.Replace('\\', '/'),
                    "[REDACTED]",
                    StringComparison.OrdinalIgnoreCase);
            }

            foreach (var pattern in SensitivePatterns)
            {
                redacted = pattern.Replace(redacted, "[REDACTED]");
            }

            return redacted;
        }
    }
}

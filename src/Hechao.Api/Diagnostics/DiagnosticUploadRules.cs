using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hechao.Contracts;

namespace Hechao.Api.Diagnostics;

public static partial class DiagnosticUploadRules
{
    public const string UploadTokenHeaderName = "X-Hechao-Diagnostic-Token";
    private const int MaximumArchiveEntries = 4;
    private const long MaximumUncompressedBytes = 2 * 1024 * 1024;
    private const int MaximumMetadataBytes = 64 * 1024;

    public static Dictionary<string, string[]> ValidateCreateRequest(
        DiagnosticUploadCreateRequest request,
        DiagnosticUploadOptions options)
    {
        var errors = new Dictionary<string, string[]>();
        if (!ProfileIdRegex().IsMatch(request.ProfileId ?? string.Empty))
        {
            errors["profileId"] = ["客户端档案 ID 无效。"];
        }

        if (request.Size is <= 0 || request.Size > options.MaximumBytes)
        {
            errors["size"] = [$"诊断包必须小于 {options.MaximumBytes} 字节。"];
        }

        if (!Sha256Regex().IsMatch(request.Sha256 ?? string.Empty))
        {
            errors["sha256"] = ["诊断包 SHA-256 无效。"];
        }

        if (string.IsNullOrWhiteSpace(request.LauncherVersion) ||
            request.LauncherVersion.Length > 40 ||
            request.LauncherVersion.Any(char.IsControl))
        {
            errors["launcherVersion"] = ["启动器版本无效。"];
        }

        return errors;
    }

    public static string CreateUploadToken()
    {
        Span<byte> token = stackalloc byte[32];
        RandomNumberGenerator.Fill(token);
        return Convert.ToBase64String(token)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static string HashUploadToken(string token) =>
        Convert.ToHexString(
                SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)))
            .ToLowerInvariant();

    public static bool IsValidUploadToken(string? token) =>
        token is { Length: 43 } && UploadTokenRegex().IsMatch(token);

    public static async Task ValidateArchiveAsync(
        string path,
        string expectedProfileId,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        if (archive.Entries.Count is < 2 or > MaximumArchiveEntries)
        {
            throw new InvalidDataException("诊断包条目数量无效。");
        }

        long uncompressedBytes = 0;
        ZipArchiveEntry? metadataEntry = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!seen.Add(entry.FullName) ||
                !IsAllowedEntry(entry.FullName) ||
                entry.Length < 0)
            {
                throw new InvalidDataException("诊断包包含无效条目。");
            }

            if (entry.Length > MaximumUncompressedBytes ||
                uncompressedBytes > MaximumUncompressedBytes - entry.Length)
            {
                throw new InvalidDataException("诊断包解压后过大。");
            }
            uncompressedBytes += entry.Length;

            if (entry.FullName == "diagnostic.json")
            {
                metadataEntry = entry;
            }
        }

        if (!seen.Contains("README.txt") ||
            metadataEntry is null ||
            metadataEntry.Length is <= 0 or > MaximumMetadataBytes)
        {
            throw new InvalidDataException("诊断包缺少有效元数据。");
        }

        try
        {
            await using var metadataStream = metadataEntry.Open();
            using var document = await JsonDocument.ParseAsync(
                metadataStream,
                cancellationToken: cancellationToken);
            var root = document.RootElement;
            if (!root.TryGetProperty("schemaVersion", out var schemaVersion) ||
                schemaVersion.ValueKind != JsonValueKind.Number ||
                !schemaVersion.TryGetInt32(out var parsedSchemaVersion) ||
                parsedSchemaVersion != 1 ||
                !root.TryGetProperty("profileId", out var profileId) ||
                profileId.ValueKind != JsonValueKind.String ||
                !string.Equals(
                    profileId.GetString(),
                    expectedProfileId,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "诊断包元数据与上传档案不匹配。");
            }
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("诊断包元数据不是有效 JSON。", exception);
        }
    }

    private static bool IsAllowedEntry(string name) =>
        name is "diagnostic.json" or "README.txt" or "logs/latest.log" ||
        CrashReportEntryRegex().IsMatch(name);

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{1,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex ProfileIdRegex();

    [GeneratedRegex("^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Regex();

    [GeneratedRegex("^[A-Za-z0-9_-]{43}$", RegexOptions.CultureInvariant)]
    private static partial Regex UploadTokenRegex();

    [GeneratedRegex(
        "^crash-reports/crash-[A-Za-z0-9._-]{1,120}\\.txt$",
        RegexOptions.CultureInvariant)]
    private static partial Regex CrashReportEntryRegex();
}

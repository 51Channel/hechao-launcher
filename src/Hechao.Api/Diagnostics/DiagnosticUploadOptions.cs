namespace Hechao.Api.Diagnostics;

public sealed class DiagnosticUploadOptions
{
    public const string SectionName = "DiagnosticUploads";

    public string StorageRoot { get; set; } = GetDefaultStorageRoot();
    public int UploadTokenMinutes { get; set; } = 10;
    public int RetentionDays { get; set; } = 14;
    public long MaximumBytes { get; set; } = 8 * 1024 * 1024;
    public int MaximumUploadsPerDay { get; set; } = 5;
    public long MaximumBytesPerDay { get; set; } = 40 * 1024 * 1024;
    public int MaximumActiveUploads { get; set; } = 10;
    public int CleanupMinutes { get; set; } = 60;

    public bool HasValidStorageRoot() =>
        !string.IsNullOrWhiteSpace(StorageRoot) &&
        Path.IsPathFullyQualified(StorageRoot);

    private static string GetDefaultStorageRoot() =>
        OperatingSystem.IsWindows()
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Hechao",
                "LauncherApi",
                "diagnostics")
            : "/var/lib/hechao-launcher-api/diagnostics";
}

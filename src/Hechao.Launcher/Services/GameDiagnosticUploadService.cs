using System.IO;
using System.Security.Cryptography;
using Hechao.Contracts;
using Hechao.Distribution;

namespace Hechao.Launcher.Services;

public interface IGameDiagnosticUploadService
{
    Task<DiagnosticUploadReceipt> UploadAsync(
        GameDiagnosticBundleResult bundle,
        string profileId,
        CancellationToken cancellationToken = default);
}

public sealed class GameDiagnosticUploadService(LauncherApiClient apiClient)
    : IGameDiagnosticUploadService
{
    public async Task<DiagnosticUploadReceipt> UploadAsync(
        GameDiagnosticBundleResult bundle,
        string profileId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ManifestValidator.ValidateProfileId(profileId);
        var path = Path.GetFullPath(bundle.BundlePath);
        var file = new FileInfo(path);
        if (!file.Exists ||
            file.Extension != ".zip" ||
            (file.Attributes & FileAttributes.ReparsePoint) != 0 ||
            file.Length <= 0 ||
            file.Length != bundle.Size)
        {
            throw new InvalidDataException(
                "The local diagnostic bundle changed before upload.");
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var sha256 = Convert.ToHexString(
                await SHA256.HashDataAsync(stream, cancellationToken))
            .ToLowerInvariant();
        stream.Position = 0;
        var authorization = await apiClient.CreateDiagnosticUploadAsync(
            new DiagnosticUploadCreateRequest(
                profileId,
                file.Length,
                sha256,
                LauncherProductInfo.Version),
            cancellationToken);
        return await apiClient.UploadDiagnosticAsync(
            authorization,
            stream,
            file.Length,
            cancellationToken);
    }
}

namespace Hechao.Contracts;

public sealed record DiagnosticUploadCreateRequest(
    string ProfileId,
    long Size,
    string Sha256,
    string LauncherVersion);

public sealed record DiagnosticUploadAuthorizationResponse(
    Guid UploadId,
    string UploadToken,
    DateTimeOffset UploadTokenExpiresAt,
    long MaximumBytes);

public sealed record DiagnosticUploadReceipt(
    Guid UploadId,
    string ProfileId,
    long Size,
    string Sha256,
    DateTimeOffset UploadedAt,
    DateTimeOffset ExpiresAt);

public sealed record AdminDiagnosticUploadRecord(
    Guid UploadId,
    Guid UserId,
    string AccountDisplayName,
    string ProfileId,
    string LauncherVersion,
    long Size,
    string Sha256,
    DateTimeOffset UploadedAt,
    DateTimeOffset ExpiresAt);

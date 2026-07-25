using Hechao.Contracts;

namespace Hechao.Api.Authentication;

public sealed record ForumAccountRegisterRequest(
    string Username,
    string DisplayName,
    string Email,
    string Password);

public sealed record ForumAccountAuthenticateRequest(
    string UsernameOrEmail,
    string Password);

public sealed record ForumLegacyAccountImportRequest(
    string ForumUserId,
    string Username,
    string DisplayName,
    string Email,
    string PasswordHash,
    bool IsDisabled,
    DateTimeOffset CreatedAt);

public sealed record ForumAccountPasswordChangeRequest(
    Guid UserId,
    string CurrentPassword,
    string NewPassword);

public sealed record ForumAccountPasswordResetRequest(
    Guid UserId,
    string NewPassword);

public sealed record ForumAccountProfileUpdateRequest(
    Guid UserId,
    string DisplayName);

public sealed record ForumLegacyAccountImportResponse(
    HechaoAccount Account,
    bool Created);

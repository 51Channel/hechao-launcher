namespace Hechao.Contracts;

public sealed record AdminBrowserTicketResponse(
    string BrowserUrl,
    DateTimeOffset ExpiresAt);

public sealed record AdminBrowserRedeemRequest(string Ticket);

public sealed record AdminWebSessionStatus(
    AuthenticatedPlayer Player,
    bool MfaConfigured,
    bool MfaVerified,
    DateTimeOffset ExpiresAt);

public sealed record AdminMfaEnrollmentResponse(
    string SecretKey,
    string OtpAuthUri,
    string QrCodeDataUri,
    DateTimeOffset ExpiresAt);

public sealed record AdminMfaCodeRequest(string Code);

public sealed record AdminMfaVerificationResponse(
    bool Verified,
    DateTimeOffset VerifiedAt,
    IReadOnlyList<string>? RecoveryCodes = null,
    bool RecoveryCodeUsed = false);

public sealed record AdminCsrfTokenResponse(string RequestToken);

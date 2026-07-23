using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Hechao.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Hechao.Api.Authentication;

public sealed class LauncherSessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    AuthenticationRepository repository)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "LauncherSession";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!BearerTokenReader.TryRead(Request, out var accessToken))
        {
            return Request.Headers.ContainsKey("Authorization")
                ? AuthenticateResult.Fail("Invalid authorization header.")
                : AuthenticateResult.NoResult();
        }

        var session = await repository.AuthenticateAsync(accessToken, Context.RequestAborted);
        if (session is null)
        {
            return AuthenticateResult.Fail("The launcher session is invalid or expired.");
        }

        var account = session.Account;
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, account.UserId.ToString("D")),
            new(ClaimTypes.Name, account.DisplayName),
            new(ClaimTypes.Role, account.AccessTier.ToString()),
            new(LauncherClaimTypes.SessionId, session.SessionId.ToString("D")),
            new(LauncherClaimTypes.Username, account.Username),
            new(LauncherClaimTypes.AccountCreatedAt, account.CreatedAt.ToString("O", CultureInfo.InvariantCulture)),
            new(LauncherClaimTypes.LuckPermsPrimaryGroup, account.LuckPermsPrimaryGroup),
            new(LauncherClaimTypes.AccessTier, account.AccessTier.ToString())
        };

        if (!string.IsNullOrWhiteSpace(account.Email))
        {
            claims.Add(new Claim(ClaimTypes.Email, account.Email));
        }

        if (account.MinecraftUuid is { } minecraftUuid &&
            !string.IsNullOrWhiteSpace(account.MinecraftName))
        {
            claims.Add(new Claim(LauncherClaimTypes.MinecraftUuid, minecraftUuid.ToString("D")));
            claims.Add(new Claim(LauncherClaimTypes.MinecraftName, account.MinecraftName));
        }

        if (account.LuckPermsSyncedAt is { } syncedAt)
        {
            claims.Add(new Claim(
                LauncherClaimTypes.LuckPermsSyncedAt,
                syncedAt.ToString("O", CultureInfo.InvariantCulture)));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        return AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }
}

public static class LauncherClaimTypes
{
    public const string SessionId = "hechao:session_id";
    public const string Username = "hechao:username";
    public const string AccountCreatedAt = "hechao:account_created_at";
    public const string MinecraftUuid = "hechao:minecraft_uuid";
    public const string MinecraftName = "hechao:minecraft_name";
    public const string LuckPermsPrimaryGroup = "hechao:luckperms_primary_group";
    public const string LuckPermsSyncedAt = "hechao:luckperms_synced_at";
    public const string AccessTier = "hechao:access_tier";
}

public static class LauncherPrincipalExtensions
{
    public static HechaoAccount? GetAccount(this ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true ||
            !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ||
            !Enum.TryParse<AccessTier>(
                principal.FindFirstValue(LauncherClaimTypes.AccessTier),
                ignoreCase: true,
                out var accessTier))
        {
            return null;
        }

        var username = principal.FindFirstValue(LauncherClaimTypes.Username);
        var displayName = principal.FindFirstValue(ClaimTypes.Name);
        var primaryGroup = principal.FindFirstValue(LauncherClaimTypes.LuckPermsPrimaryGroup);
        if (string.IsNullOrWhiteSpace(username) ||
            string.IsNullOrWhiteSpace(displayName) ||
            string.IsNullOrWhiteSpace(primaryGroup))
        {
            return null;
        }

        if (!DateTimeOffset.TryParse(
                principal.FindFirstValue(LauncherClaimTypes.AccountCreatedAt),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var createdAt))
        {
            return null;
        }

        Guid? minecraftUuid = null;
        var rawMinecraftUuid = principal.FindFirstValue(LauncherClaimTypes.MinecraftUuid);
        if (Guid.TryParse(rawMinecraftUuid, out var parsedMinecraftUuid))
        {
            minecraftUuid = parsedMinecraftUuid;
        }

        var minecraftName = principal.FindFirstValue(LauncherClaimTypes.MinecraftName);
        DateTimeOffset? syncedAt = null;
        var rawSyncedAt = principal.FindFirstValue(LauncherClaimTypes.LuckPermsSyncedAt);
        if (!string.IsNullOrWhiteSpace(rawSyncedAt) &&
            DateTimeOffset.TryParse(
                rawSyncedAt,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsedSyncedAt))
        {
            syncedAt = parsedSyncedAt;
        }

        return new HechaoAccount(
            userId,
            username,
            displayName,
            principal.FindFirstValue(ClaimTypes.Email),
            minecraftUuid,
            minecraftName,
            primaryGroup,
            accessTier,
            syncedAt,
            createdAt);
    }

    public static AuthenticatedPlayer? GetPlayer(this ClaimsPrincipal principal)
    {
        var account = principal.GetAccount();
        return account?.MinecraftUuid is { } minecraftUuid &&
               !string.IsNullOrWhiteSpace(account.MinecraftName)
            ? new AuthenticatedPlayer(
                account.UserId,
                minecraftUuid,
                account.MinecraftName,
                account.LuckPermsPrimaryGroup,
                account.AccessTier,
                account.LuckPermsSyncedAt)
            : null;
    }
}

public static class BearerTokenReader
{
    public static bool TryRead(HttpRequest request, out string accessToken)
    {
        accessToken = string.Empty;
        var value = request.Headers.Authorization.ToString();
        if (!AuthenticationHeaderValue.TryParse(value, out var header) ||
            !string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(header.Parameter))
        {
            return false;
        }

        accessToken = header.Parameter;
        return true;
    }
}

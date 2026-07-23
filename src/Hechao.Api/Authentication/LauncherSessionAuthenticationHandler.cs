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

        var player = session.Player;
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, player.UserId.ToString("D")),
            new(ClaimTypes.Name, player.MinecraftName),
            new(ClaimTypes.Role, player.AccessTier.ToString()),
            new(LauncherClaimTypes.SessionId, session.SessionId.ToString("D")),
            new(LauncherClaimTypes.MinecraftUuid, player.MinecraftUuid.ToString("D")),
            new(LauncherClaimTypes.LuckPermsPrimaryGroup, player.LuckPermsPrimaryGroup),
            new(LauncherClaimTypes.AccessTier, player.AccessTier.ToString())
        };

        if (player.LuckPermsSyncedAt is { } syncedAt)
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
    public const string MinecraftUuid = "hechao:minecraft_uuid";
    public const string LuckPermsPrimaryGroup = "hechao:luckperms_primary_group";
    public const string LuckPermsSyncedAt = "hechao:luckperms_synced_at";
    public const string AccessTier = "hechao:access_tier";
}

public static class LauncherPrincipalExtensions
{
    public static AuthenticatedPlayer? GetPlayer(this ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true ||
            !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ||
            !Guid.TryParse(principal.FindFirstValue(LauncherClaimTypes.MinecraftUuid), out var minecraftUuid) ||
            !Enum.TryParse<AccessTier>(
                principal.FindFirstValue(LauncherClaimTypes.AccessTier),
                ignoreCase: true,
                out var accessTier))
        {
            return null;
        }

        var minecraftName = principal.FindFirstValue(ClaimTypes.Name);
        var primaryGroup = principal.FindFirstValue(LauncherClaimTypes.LuckPermsPrimaryGroup);
        if (string.IsNullOrWhiteSpace(minecraftName) || string.IsNullOrWhiteSpace(primaryGroup))
        {
            return null;
        }

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

        return new AuthenticatedPlayer(
            userId,
            minecraftUuid,
            minecraftName,
            primaryGroup,
            accessTier,
            syncedAt);
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

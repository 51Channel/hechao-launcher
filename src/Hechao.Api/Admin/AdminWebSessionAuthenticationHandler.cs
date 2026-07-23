using System.Security.Claims;
using System.Text.Encodings.Web;
using Hechao.Api.Authentication;
using Hechao.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Hechao.Api.Admin;

public sealed class AdminWebSessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    AdminWebSessionRepository repository)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "AdminWebSession";
    public const string CookieName = "__Host-HechaoAdmin";
    private static readonly object StateKey = new();

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Cookies.TryGetValue(CookieName, out var sessionToken))
        {
            return AuthenticateResult.NoResult();
        }

        var state = await repository.AuthenticateAsync(
            sessionToken,
            Context.RequestAborted);
        if (state is null)
        {
            return AuthenticateResult.Fail("The administrator session is invalid or expired.");
        }

        Context.Items[StateKey] = state;
        var player = state.Player;
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, player.UserId.ToString("D")),
            new(ClaimTypes.Name, player.MinecraftName),
            new(ClaimTypes.Role, nameof(AccessTier.Administrator)),
            new(LauncherClaimTypes.MinecraftUuid, player.MinecraftUuid.ToString("D")),
            new(LauncherClaimTypes.LuckPermsPrimaryGroup, player.LuckPermsPrimaryGroup),
            new(LauncherClaimTypes.AccessTier, player.AccessTier.ToString()),
            new(AdminWebClaimTypes.SessionId, state.SessionId.ToString("D"))
        };

        if (player.LuckPermsSyncedAt is { } syncedAt)
        {
            claims.Add(new Claim(
                LauncherClaimTypes.LuckPermsSyncedAt,
                syncedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture)));
        }

        if (state.MfaVerified)
        {
            claims.Add(new Claim(AdminWebClaimTypes.AuthenticationMethod, "mfa"));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        return AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    public static AdminWebAuthenticationState? GetState(HttpContext context)
    {
        return context.Items.TryGetValue(StateKey, out var value)
            ? value as AdminWebAuthenticationState
            : null;
    }
}

public static class AdminWebClaimTypes
{
    public const string SessionId = "hechao:admin_session_id";
    public const string AuthenticationMethod = "amr";
}

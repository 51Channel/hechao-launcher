using Hechao.Api.Authentication;
using Hechao.Contracts;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Extensions.Options;

namespace Hechao.Api.Admin;

public static class AdminWebEndpoints
{
    public static void MapAdminWebEndpoints(this WebApplication app)
    {
        var auth = app.MapGroup("/v1/admin-auth");

        auth.MapPost("/tickets", CreateTicketAsync)
            .RequireAuthorization(AdminAuthorization.BootstrapPolicyName)
            .RequireRateLimiting("admin-auth");

        auth.MapPost("/redeem", RedeemTicketAsync)
            .AddEndpointFilter<AdminWebHostFilter>()
            .RequireRateLimiting("admin-auth");

        var session = auth.MapGroup(string.Empty)
            .AddEndpointFilter<AdminWebHostFilter>()
            .RequireAuthorization(AdminAuthorization.WebSessionPolicyName);
        session.MapGet("/session", GetSession);
        session.MapGet("/csrf", GetCsrfToken);
        session.MapPost("/mfa/enrollment", BeginMfaEnrollmentAsync)
            .AddEndpointFilter<AdminAntiforgeryFilter>()
            .RequireRateLimiting("admin-mfa");
        session.MapPost("/mfa/enrollment/confirm", CompleteMfaEnrollmentAsync)
            .AddEndpointFilter<AdminAntiforgeryFilter>()
            .RequireRateLimiting("admin-mfa");
        session.MapPost("/mfa/verify", VerifyMfaAsync)
            .AddEndpointFilter<AdminAntiforgeryFilter>()
            .RequireRateLimiting("admin-mfa");
        session.MapPost("/logout", LogoutAsync)
            .AddEndpointFilter<AdminAntiforgeryFilter>();
    }

    private static async Task<IResult> CreateTicketAsync(
        AdminWebSessionRepository repository,
        IOptions<AdminWebOptions> options,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled || !options.Value.TryGetPublicBaseUri(out var publicBaseUri))
        {
            return Results.Problem(
                title: "管理后台尚未启用",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var player = context.User.GetPlayer();
        if (player?.AccessTier != AccessTier.Administrator)
        {
            return Results.Forbid();
        }

        var ticket = await repository.CreateLoginTicketAsync(
            player.UserId,
            context.Connection.RemoteIpAddress,
            context.Request.Headers.UserAgent.ToString(),
            cancellationToken);
        if (ticket is null)
        {
            return Results.Forbid();
        }

        var browserUrl = new UriBuilder(publicBaseUri)
        {
            Path = "/admin/",
            Fragment = $"ticket={Uri.EscapeDataString(ticket.Token)}"
        }.Uri.AbsoluteUri;
        return Results.Ok(new AdminBrowserTicketResponse(
            browserUrl,
            ticket.ExpiresAt));
    }

    private static async Task<IResult> RedeemTicketAsync(
        AdminBrowserRedeemRequest request,
        AdminWebSessionRepository repository,
        IOptions<AdminWebOptions> options,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
        {
            return Results.Problem(
                title: "管理后台尚未启用",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var result = await repository.RedeemLoginTicketAsync(
            request.Ticket,
            context.Connection.RemoteIpAddress,
            context.Request.Headers.UserAgent.ToString(),
            cancellationToken);
        if (result.Status != AdminTicketRedeemStatus.Success ||
            result.SessionToken is null ||
            result.State is null)
        {
            return Results.Problem(
                title: "登录链接无效",
                detail: result.Status == AdminTicketRedeemStatus.SourceMismatch
                    ? "浏览器网络地址与启动器不一致，请返回启动器重新打开。"
                    : "登录链接已过期或已使用，请返回启动器重新打开。",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        SetSessionCookie(context, result.SessionToken, result.State.ExpiresAt);
        return Results.Ok(ToStatus(result.State));
    }

    private static IResult GetSession(HttpContext context)
    {
        var state = AdminWebSessionAuthenticationHandler.GetState(context);
        return state is null
            ? Results.Unauthorized()
            : Results.Ok(ToStatus(state));
    }

    private static IResult GetCsrfToken(
        HttpContext context,
        IAntiforgery antiforgery)
    {
        var tokens = antiforgery.GetAndStoreTokens(context);
        return string.IsNullOrWhiteSpace(tokens.RequestToken)
            ? Results.Problem(
                title: "无法生成页面安全令牌",
                statusCode: StatusCodes.Status500InternalServerError)
            : Results.Ok(new AdminCsrfTokenResponse(tokens.RequestToken));
    }

    private static async Task<IResult> BeginMfaEnrollmentAsync(
        AdminWebSessionRepository repository,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var state = AdminWebSessionAuthenticationHandler.GetState(context);
        if (state is null)
        {
            return Results.Unauthorized();
        }

        var result = await repository.BeginMfaEnrollmentAsync(
            state,
            context.Connection.RemoteIpAddress,
            cancellationToken);
        return result.Status switch
        {
            AdminMfaOperationStatus.Success when result.Enrollment is not null =>
                Results.Ok(new AdminMfaEnrollmentResponse(
                    result.Enrollment.SecretKey,
                    result.Enrollment.OtpAuthUri,
                    result.Enrollment.QrCodeDataUri,
                    result.Enrollment.ExpiresAt)),
            AdminMfaOperationStatus.AlreadyConfigured => Results.Conflict(new
            {
                message = "此管理员账号已配置双重验证。"
            }),
            _ => Results.Problem(
                title: "无法创建双重验证设置",
                statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private static async Task<IResult> CompleteMfaEnrollmentAsync(
        AdminMfaCodeRequest request,
        AdminWebSessionRepository repository,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var state = AdminWebSessionAuthenticationHandler.GetState(context);
        if (state is null)
        {
            return Results.Unauthorized();
        }

        var result = await repository.CompleteMfaEnrollmentAsync(
            state,
            request.Code,
            context.Connection.RemoteIpAddress,
            cancellationToken);
        return MapMfaVerificationResult(result);
    }

    private static async Task<IResult> VerifyMfaAsync(
        AdminMfaCodeRequest request,
        AdminWebSessionRepository repository,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var state = AdminWebSessionAuthenticationHandler.GetState(context);
        if (state is null)
        {
            return Results.Unauthorized();
        }

        var result = await repository.VerifyMfaAsync(
            state,
            request.Code,
            context.Connection.RemoteIpAddress,
            cancellationToken);
        return MapMfaVerificationResult(result);
    }

    private static async Task<IResult> LogoutAsync(
        AdminWebSessionRepository repository,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var state = AdminWebSessionAuthenticationHandler.GetState(context);
        if (state is not null)
        {
            await repository.RevokeSessionAsync(
                state,
                context.Connection.RemoteIpAddress,
                cancellationToken);
        }

        context.Response.Cookies.Delete(
            AdminWebSessionAuthenticationHandler.CookieName,
            CreateCookieOptions(expiresAt: null));
        return Results.NoContent();
    }

    private static IResult MapMfaVerificationResult(
        AdminMfaVerificationResult result)
    {
        return result.Status switch
        {
            AdminMfaOperationStatus.Success when result.VerifiedAt is not null =>
                Results.Ok(new AdminMfaVerificationResponse(
                    Verified: true,
                    result.VerifiedAt.Value,
                    result.RecoveryCodes,
                    result.RecoveryCodeUsed)),
            AdminMfaOperationStatus.InvalidCode => Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["code"] = ["验证码或恢复码无效。"]
                }),
            AdminMfaOperationStatus.Expired => Results.Conflict(new
            {
                message = "双重验证设置已过期，请重新生成。"
            }),
            AdminMfaOperationStatus.NotConfigured => Results.Conflict(new
            {
                message = "此管理员账号尚未配置双重验证。"
            }),
            AdminMfaOperationStatus.InvalidSession => Results.Unauthorized(),
            AdminMfaOperationStatus.Unavailable => Results.Problem(
                title: "双重验证密钥暂时不可用",
                statusCode: StatusCodes.Status503ServiceUnavailable),
            _ => Results.Problem(
                title: "双重验证失败",
                statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private static AdminWebSessionStatus ToStatus(
        AdminWebAuthenticationState state)
    {
        return new AdminWebSessionStatus(
            state.Player,
            state.MfaConfigured,
            state.MfaVerified,
            state.ExpiresAt);
    }

    private static void SetSessionCookie(
        HttpContext context,
        string sessionToken,
        DateTimeOffset expiresAt)
    {
        context.Response.Cookies.Append(
            AdminWebSessionAuthenticationHandler.CookieName,
            sessionToken,
            CreateCookieOptions(expiresAt));
    }

    private static CookieOptions CreateCookieOptions(DateTimeOffset? expiresAt)
    {
        return new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            IsEssential = true,
            Expires = expiresAt
        };
    }
}

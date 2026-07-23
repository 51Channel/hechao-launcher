using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Hechao.Api.Admin;
using Hechao.Api.Authentication;
using Hechao.Api.Catalog;
using Hechao.Api.Database;
using Hechao.Api.Distribution;
using Hechao.Api.LuckPerms;
using Hechao.Api.Monitoring;
using Hechao.Api.Velocity;
using Hechao.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost
    .UseUrls(builder.Configuration["urls"] ?? "http://127.0.0.1:8090")
    .ConfigureKestrel(options => options.AddServerHeader = false);

builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOptions<LauncherAuthenticationOptions>()
    .Bind(builder.Configuration.GetSection(LauncherAuthenticationOptions.SectionName))
    .Validate(
        options => options.AccessTokenMinutes is >= 5 and <= 60,
        "Authentication:AccessTokenMinutes must be between 5 and 60.")
    .Validate(
        options => options.RefreshTokenDays is >= 1 and <= 90,
        "Authentication:RefreshTokenDays must be between 1 and 90.")
    .Validate(
        options => string.IsNullOrEmpty(options.InternalSyncTokenSha256) ||
                   Regex.IsMatch(options.InternalSyncTokenSha256, "^[0-9a-fA-F]{64}$"),
        "Authentication:InternalSyncTokenSha256 must be empty or a SHA-256 hex digest.")
    .ValidateOnStart();
builder.Services.AddOptions<VelocityAuthorizationOptions>()
    .Bind(builder.Configuration.GetSection(VelocityAuthorizationOptions.SectionName))
    .Validate(
        options => string.IsNullOrEmpty(options.InternalTokenSha256) ||
                   Regex.IsMatch(options.InternalTokenSha256, "^[0-9a-fA-F]{64}$"),
        "VelocityAuthorization:InternalTokenSha256 must be empty or a SHA-256 hex digest.")
    .Validate(
        options => options.LaunchGrantMinutes is >= 2 and <= 30,
        "VelocityAuthorization:LaunchGrantMinutes must be between 2 and 30.")
    .Validate(
        options => options.MaximumLuckPermsAgeMinutes is >= 5 and <= 1440,
        "VelocityAuthorization:MaximumLuckPermsAgeMinutes must be between 5 and 1440.")
    .ValidateOnStart();
builder.Services.AddOptions<ServerHeartbeatOptions>()
    .Bind(builder.Configuration.GetSection(ServerHeartbeatOptions.SectionName))
    .Validate(
        options => string.IsNullOrEmpty(options.InternalTokenSha256) ||
                   Regex.IsMatch(options.InternalTokenSha256, "^[0-9a-fA-F]{64}$"),
        "ServerHeartbeats:InternalTokenSha256 must be empty or a SHA-256 hex digest.")
    .Validate(
        options => options.FreshnessSeconds is >= 60 and <= 900,
        "ServerHeartbeats:FreshnessSeconds must be between 60 and 900.")
    .ValidateOnStart();
builder.Services.AddOptions<DistributionOptions>()
    .Bind(builder.Configuration.GetSection(DistributionOptions.SectionName))
    .Validate(
        options => options.MaximumManifestBytes is >= 1024 and <= 16 * 1024 * 1024,
        "Distribution:MaximumManifestBytes must be between 1 KiB and 16 MiB.")
    .Validate(
        options => !options.HasAnyOssConfiguration || options.HasCompleteOssConfiguration,
        "Distribution OSS region, bucket, and endpoint must be configured together.")
    .Validate(
        options => options.PresignedUrlSeconds is >= 60 and <= 900,
        "Distribution:PresignedUrlSeconds must be between 60 and 900.")
    .Validate(IsValidOssConfiguration, "Distribution OSS configuration is invalid.")
    .ValidateOnStart();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor |
                               ForwardedHeaders.XForwardedHost |
                               ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    options.KnownProxies.Add(IPAddress.Loopback);
    options.KnownProxies.Add(IPAddress.IPv6Loopback);
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "local",
            _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 6000,
                QueueLimit = 0,
                Window = TimeSpan.FromMinutes(1)
            }));
    options.AddPolicy("authentication", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "local",
            _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 10,
                QueueLimit = 0,
                Window = TimeSpan.FromMinutes(1)
            }));
    options.AddPolicy("internal-sync", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "local",
            _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 30,
                QueueLimit = 0,
                Window = TimeSpan.FromMinutes(1)
            }));
    options.AddPolicy("internal-velocity", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "local",
            _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 1200,
                QueueLimit = 0,
                Window = TimeSpan.FromMinutes(1)
            }));
    options.AddPolicy("internal-heartbeats", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "local",
            _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 120,
                QueueLimit = 0,
                Window = TimeSpan.FromMinutes(1)
            }));
    options.AddPolicy("downloads", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.User.FindFirstValue(ClaimTypes.NameIdentifier) ??
            context.Connection.RemoteIpAddress?.ToString() ??
            "anonymous",
            _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 600,
                QueueLimit = 0,
                Window = TimeSpan.FromMinutes(1)
            }));
    options.AddPolicy("catalog", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.User.FindFirstValue(ClaimTypes.NameIdentifier) ??
            context.Connection.RemoteIpAddress?.ToString() ??
            "anonymous",
            _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 120,
                QueueLimit = 0,
                Window = TimeSpan.FromMinutes(1)
            }));
    options.AddPolicy("admin", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.User.FindFirstValue(ClaimTypes.NameIdentifier) ??
            context.Connection.RemoteIpAddress?.ToString() ??
            "anonymous",
            _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 240,
                QueueLimit = 0,
                Window = TimeSpan.FromMinutes(1)
            }));
});

var databaseConnectionString = builder.Configuration.GetConnectionString("LauncherDatabase");
if (string.IsNullOrWhiteSpace(databaseConnectionString))
{
    throw new InvalidOperationException("ConnectionStrings:LauncherDatabase is required.");
}

var connectionStringBuilder = new NpgsqlConnectionStringBuilder(databaseConnectionString)
{
    ApplicationName = "hechao-launcher-api",
    Timeout = 5,
    CommandTimeout = 10,
    MinPoolSize = 0,
    MaxPoolSize = 20,
    KeepAlive = 30
};
builder.Services.AddSingleton(NpgsqlDataSource.Create(connectionStringBuilder.ConnectionString));
builder.Services.AddSingleton<DatabaseMigrator>();
builder.Services.AddSingleton<CatalogRepository>();
builder.Services.AddSingleton<AdminCatalogRepository>();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<ProfileManifestStore>();
builder.Services.AddSingleton<OssPresignedUrlFactory>();
builder.Services.AddSingleton<SessionTokenGenerator>();
builder.Services.AddSingleton<AuthenticationRepository>();
builder.Services.AddSingleton<InternalSyncTokenValidator>();
builder.Services.AddSingleton<LuckPermsSyncRepository>();
builder.Services.AddSingleton<VelocityAuthorizationTokenValidator>();
builder.Services.AddSingleton<VelocityAuthorizationRepository>();
builder.Services.AddSingleton<ServerHeartbeatTokenValidator>();
builder.Services.AddSingleton<ServerHeartbeatRepository>();
builder.Services.AddHttpClient<MinecraftServicesClient>(client =>
{
    client.BaseAddress = new Uri("https://api.minecraftservices.com/");
    client.Timeout = TimeSpan.FromSeconds(10);
    client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Hechao.Launcher.Api", "0.7.0"));
    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
});
builder.Services
    .AddAuthentication(LauncherSessionAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, LauncherSessionAuthenticationHandler>(
        LauncherSessionAuthenticationHandler.SchemeName,
        _ => { });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(
        AdminAuthorization.PolicyName,
        policy => policy
            .RequireAuthenticatedUser()
            .RequireRole(nameof(AccessTier.Administrator)));
});

var app = builder.Build();

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.Use(async (context, next) =>
{
    context.Response.Headers["Cache-Control"] = "no-store";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["X-Request-Id"] = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
    await next();
});
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

var serviceVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0";

await app.Services.GetRequiredService<DatabaseMigrator>().ApplyAsync();

app.MapGet("/healthz", () => Results.Ok(new
{
    service = "hechao-launcher-api",
    status = "ok",
    version = serviceVersion,
    checkedAt = DateTimeOffset.UtcNow
})).DisableRateLimiting();

app.MapGet("/readyz", CheckReadinessAsync).DisableRateLimiting();

app.MapPost("/v1/auth/minecraft/exchange", ExchangeMinecraftSessionAsync)
    .RequireRateLimiting("authentication");
app.MapPost("/v1/auth/refresh", RefreshSessionAsync)
    .RequireRateLimiting("authentication");
app.MapPost("/v1/auth/logout", LogoutAsync)
    .RequireAuthorization();
app.MapGet("/v1/me", GetCurrentPlayer)
    .RequireAuthorization();
app.MapPost("/v1/velocity/launch-grants", CreateVelocityLaunchGrantAsync)
    .RequireAuthorization()
    .RequireRateLimiting("authentication");
app.MapPost("/v1/internal/velocity/authorize", AuthorizeVelocityConnectionAsync)
    .RequireRateLimiting("internal-velocity");
app.MapPost("/v1/internal/luckperms/snapshot", ImportLuckPermsSnapshotAsync)
    .RequireRateLimiting("internal-sync");
app.MapPost("/v1/internal/server-heartbeats", ImportServerHeartbeatsAsync)
    .RequireRateLimiting("internal-heartbeats");
app.MapGet("/v1/catalog", GetCatalogAsync)
    .RequireRateLimiting("catalog");
app.MapGet("/v1/profiles/{profileId}/manifest", GetProfileManifestAsync)
    .RequireAuthorization()
    .RequireRateLimiting("catalog");
app.MapGet(
        "/v1/profiles/{profileId}/objects/{prefix}/{objectSha256}",
        GetProfileObjectAsync)
    .RequireAuthorization()
    .RequireRateLimiting("downloads");

var adminApi = app.MapGroup("/v1/admin")
    .RequireAuthorization(AdminAuthorization.PolicyName)
    .RequireRateLimiting("admin");
adminApi.MapGet("/catalog/servers", GetAdminServersAsync);
adminApi.MapGet("/catalog/servers/{serverId}", GetAdminServerAsync);
adminApi.MapGet("/catalog/client-profiles", GetAdminClientProfilesAsync);
adminApi.MapPost("/catalog/servers", CreateAdminServerAsync);
adminApi.MapPut("/catalog/servers/{serverId}", UpdateAdminServerAsync);
adminApi.MapPut("/catalog/servers/{serverId}/visibility", SetAdminServerVisibilityAsync);
adminApi.MapGet("/audit-logs", GetAdminAuditLogsAsync);

await app.RunAsync();

async Task<IResult> CheckReadinessAsync(
    NpgsqlDataSource dataSource,
    ILogger<Program> logger,
    CancellationToken cancellationToken)
{
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeout.CancelAfter(TimeSpan.FromSeconds(2));

    try
    {
        await using var command = dataSource.CreateCommand("SELECT 1");
        await command.ExecuteScalarAsync(timeout.Token);
        return Results.Ok(new
        {
            service = "hechao-launcher-api",
            status = "ready",
            version = serviceVersion,
            database = "ready",
            checkedAt = DateTimeOffset.UtcNow
        });
    }
    catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
    {
        logger.LogWarning(exception, "Database readiness check timed out.");
    }
    catch (NpgsqlException exception)
    {
        logger.LogWarning(exception, "Database readiness check failed.");
    }

    return Results.Json(new
    {
        service = "hechao-launcher-api",
        status = "not_ready",
        version = serviceVersion,
        database = "unavailable",
        checkedAt = DateTimeOffset.UtcNow
    }, statusCode: StatusCodes.Status503ServiceUnavailable);
}

async Task<IResult> ExchangeMinecraftSessionAsync(
    MinecraftSessionExchangeRequest request,
    MinecraftServicesClient minecraftServices,
    AuthenticationRepository authenticationRepository,
    HttpContext context,
    CancellationToken cancellationToken)
{
    try
    {
        var identity = await minecraftServices.VerifyAsync(request.MinecraftAccessToken, cancellationToken);
        var response = await authenticationRepository.CreateSessionAsync(
            identity,
            context.Connection.RemoteIpAddress,
            context.Request.Headers.UserAgent.ToString(),
            cancellationToken);
        return Results.Ok(response);
    }
    catch (MinecraftVerificationException exception)
    {
        return exception.Failure switch
        {
            MinecraftVerificationFailure.InvalidToken => AuthenticationProblem(
                StatusCodes.Status401Unauthorized,
                "Minecraft 登录凭据无效或已过期。"),
            MinecraftVerificationFailure.NoJavaEntitlement => AuthenticationProblem(
                StatusCodes.Status403Forbidden,
                "该 Microsoft 账号没有可用的 Minecraft: Java Edition 权益。"),
            MinecraftVerificationFailure.NoJavaProfile => AuthenticationProblem(
                StatusCodes.Status403Forbidden,
                "该 Microsoft 账号尚未创建 Minecraft: Java Edition 档案。"),
            _ => AuthenticationProblem(
                StatusCodes.Status503ServiceUnavailable,
                "暂时无法向 Minecraft 服务验证账号，请稍后重试。")
        };
    }
}

async Task<IResult> RefreshSessionAsync(
    RefreshSessionRequest request,
    AuthenticationRepository repository,
    CancellationToken cancellationToken)
{
    var response = await repository.RefreshSessionAsync(request.RefreshToken, cancellationToken);
    return response is null
        ? AuthenticationProblem(StatusCodes.Status401Unauthorized, "登录会话已过期，请重新登录 Microsoft 账号。")
        : Results.Ok(response);
}

async Task<IResult> LogoutAsync(
    AuthenticationRepository repository,
    HttpContext context,
    CancellationToken cancellationToken)
{
    if (BearerTokenReader.TryRead(context.Request, out var accessToken))
    {
        await repository.RevokeSessionAsync(accessToken, cancellationToken);
    }

    return Results.NoContent();
}

IResult GetCurrentPlayer(HttpContext context)
{
    var player = context.User.GetPlayer();
    return player is null
        ? AuthenticationProblem(StatusCodes.Status401Unauthorized, "登录会话无效。")
        : Results.Ok(player);
}

async Task<IResult> CreateVelocityLaunchGrantAsync(
    VelocityLaunchGrantRequest request,
    VelocityAuthorizationRepository repository,
    HttpContext context,
    CancellationToken cancellationToken)
{
    var player = context.User.GetPlayer();
    if (player is null)
    {
        return AuthenticationProblem(StatusCodes.Status401Unauthorized, "登录会话无效。");
    }

    var serverId = request.ServerId;
    if (string.IsNullOrWhiteSpace(serverId) ||
        !Regex.IsMatch(serverId, "^[a-z0-9][a-z0-9._-]{1,63}$"))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["serverId"] = ["服务器 ID 无效。"]
        });
    }

    var result = await repository.CreateLaunchGrantAsync(
        player,
        serverId,
        context.Connection.RemoteIpAddress,
        cancellationToken);
    return result.Grant is null
        ? Results.Problem(
            title: "进服授权失败",
            detail: result.Message,
            statusCode: StatusCodes.Status403Forbidden)
        : Results.Ok(result.Grant);
}

async Task<IResult> AuthorizeVelocityConnectionAsync(
    VelocityAuthorizationRequest request,
    VelocityAuthorizationTokenValidator tokenValidator,
    VelocityAuthorizationRepository repository,
    HttpContext context,
    CancellationToken cancellationToken)
{
    if (!tokenValidator.IsConfigured)
    {
        return Results.Problem(
            title: "Velocity 授权尚未配置",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    var suppliedToken = context.Request.Headers["X-Hechao-Velocity-Token"].ToString();
    if (!tokenValidator.IsValid(suppliedToken))
    {
        return AuthenticationProblem(StatusCodes.Status401Unauthorized, "Velocity 内部凭据无效。");
    }

    var validationProblem = ValidateVelocityAuthorizationRequest(request, out var remoteAddress);
    if (validationProblem is not null)
    {
        return validationProblem;
    }

    var response = await repository.AuthorizeAsync(
        request,
        remoteAddress,
        cancellationToken);
    return Results.Ok(response);
}

async Task<IResult> ImportLuckPermsSnapshotAsync(
    LuckPermsSnapshotRequest request,
    InternalSyncTokenValidator tokenValidator,
    LuckPermsSyncRepository repository,
    HttpContext context,
    CancellationToken cancellationToken)
{
    if (!tokenValidator.IsConfigured)
    {
        return Results.Problem(
            title: "LuckPerms 同步尚未配置",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    var suppliedToken = context.Request.Headers["X-Hechao-Sync-Token"].ToString();
    if (!tokenValidator.IsValid(suppliedToken))
    {
        return AuthenticationProblem(StatusCodes.Status401Unauthorized, "内部同步凭据无效。");
    }

    var validationProblem = ValidateLuckPermsSnapshot(request);
    if (validationProblem is not null)
    {
        return validationProblem;
    }

    var response = await repository.ImportAsync(request, cancellationToken);
    return Results.Ok(response);
}

async Task<IResult> ImportServerHeartbeatsAsync(
    ServerHeartbeatBatchRequest request,
    ServerHeartbeatTokenValidator tokenValidator,
    ServerHeartbeatRepository repository,
    HttpContext context,
    CancellationToken cancellationToken)
{
    if (!tokenValidator.IsConfigured)
    {
        return Results.Problem(
            title: "Server heartbeat ingestion is not configured.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    var suppliedToken = context.Request.Headers["X-Hechao-Heartbeat-Token"].ToString();
    if (!tokenValidator.IsValid(suppliedToken))
    {
        return Results.Problem(
            title: "Server heartbeat authentication failed.",
            statusCode: StatusCodes.Status401Unauthorized);
    }

    var validationErrors = ServerHeartbeatRules.Validate(request, DateTimeOffset.UtcNow);
    if (validationErrors.Count > 0)
    {
        return Results.ValidationProblem(validationErrors);
    }

    try
    {
        var response = await repository.ImportAsync(request, cancellationToken);
        return Results.Ok(response);
    }
    catch (UnknownVelocityTargetsException exception)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["servers"] =
            [
                $"Unknown Velocity targets: {string.Join(", ", exception.Targets)}"
            ]
        });
    }
}

async Task<IResult> GetCatalogAsync(
    CatalogRepository repository,
    IOptions<LauncherAuthenticationOptions> authenticationOptions,
    HttpContext context,
    CancellationToken cancellationToken)
{
    var player = context.User.GetPlayer();
    var hasAuthorizationHeader = context.Request.Headers.ContainsKey("Authorization");
    if (player is null && (hasAuthorizationHeader || authenticationOptions.Value.EnforceCatalogAuthentication))
    {
        return AuthenticationProblem(StatusCodes.Status401Unauthorized, "请先使用 Microsoft 正版账号登录。 ");
    }

    var snapshot = await repository.GetSnapshotAsync(
        player?.UserId,
        player?.AccessTier,
        cancellationToken);
    return Results.Ok(snapshot);
}

async Task<IResult> GetProfileManifestAsync(
    string profileId,
    CatalogRepository catalogRepository,
    ProfileManifestStore manifestStore,
    HttpContext context,
    CancellationToken cancellationToken)
{
    var player = context.User.GetPlayer();
    if (player is null)
    {
        return AuthenticationProblem(StatusCodes.Status401Unauthorized, "请先使用 Microsoft 正版账号登录。 ");
    }

    var profile = await catalogRepository.GetAccessibleProfileAsync(
        player.UserId,
        player.AccessTier,
        profileId,
        cancellationToken);
    if (profile is null)
    {
        return Results.NotFound();
    }

    if (string.IsNullOrWhiteSpace(profile.Sha256))
    {
        return Results.Problem(
            title: "客户端配置包尚未发布",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    var manifest = await manifestStore.ReadPublishedAsync(
        profileId,
        profile.Sha256,
        cancellationToken);
    return manifest is null
        ? Results.NotFound()
        : Results.Bytes(manifest.Envelope, "application/vnd.hechao.signed-manifest+json");
}

async Task<IResult> GetProfileObjectAsync(
    string profileId,
    string prefix,
    string objectSha256,
    CatalogRepository catalogRepository,
    ProfileManifestStore manifestStore,
    OssPresignedUrlFactory urlFactory,
    HttpContext context,
    CancellationToken cancellationToken)
{
    if (!Regex.IsMatch(objectSha256, "^[0-9a-f]{64}$", RegexOptions.CultureInvariant) ||
        !string.Equals(prefix, objectSha256[..2], StringComparison.Ordinal))
    {
        return Results.NotFound();
    }

    var player = context.User.GetPlayer();
    if (player is null)
    {
        return AuthenticationProblem(StatusCodes.Status401Unauthorized, "请先使用 Microsoft 正版账号登录。");
    }

    var profile = await catalogRepository.GetAccessibleProfileAsync(
        player.UserId,
        player.AccessTier,
        profileId,
        cancellationToken);
    if (profile is null || string.IsNullOrWhiteSpace(profile.Sha256))
    {
        return Results.NotFound();
    }

    var manifest = await manifestStore.ReadPublishedAsync(
        profileId,
        profile.Sha256,
        cancellationToken);
    if (manifest is null || !manifest.ObjectDigests.Contains(objectSha256))
    {
        return Results.NotFound();
    }

    var downloadUrl = urlFactory.TryCreateGetUrl(objectSha256);
    return downloadUrl is null
        ? Results.Problem(
            title: "下载分发服务尚未就绪",
            statusCode: StatusCodes.Status503ServiceUnavailable)
        : Results.Redirect(downloadUrl);
}

async Task<IResult> GetAdminServersAsync(
    AdminCatalogRepository repository,
    CancellationToken cancellationToken)
{
    return Results.Ok(await repository.GetServersAsync(cancellationToken));
}

async Task<IResult> GetAdminServerAsync(
    string serverId,
    AdminCatalogRepository repository,
    CancellationToken cancellationToken)
{
    if (!AdminServerRules.IsValidServerId(serverId))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["serverId"] = ["服务器 ID 无效。"]
        });
    }

    var server = await repository.GetServerAsync(serverId, cancellationToken);
    return server is null ? Results.NotFound() : Results.Ok(server);
}

async Task<IResult> GetAdminClientProfilesAsync(
    AdminCatalogRepository repository,
    CancellationToken cancellationToken)
{
    return Results.Ok(await repository.GetClientProfilesAsync(cancellationToken));
}

async Task<IResult> CreateAdminServerAsync(
    AdminServerCreateRequest request,
    AdminCatalogRepository repository,
    HttpContext context,
    CancellationToken cancellationToken)
{
    var errors = AdminServerRules.Validate(request);
    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors);
    }

    var actor = context.User.GetPlayer();
    if (actor?.AccessTier != AccessTier.Administrator)
    {
        return Results.Forbid();
    }

    var result = await repository.CreateServerAsync(
        request,
        actor.UserId,
        context.Connection.RemoteIpAddress,
        cancellationToken);
    return result.Status switch
    {
        AdminCatalogMutationStatus.Success => Results.Created(
            $"/v1/admin/catalog/servers/{result.Server!.Id}",
            result.Server),
        AdminCatalogMutationStatus.DuplicateId => Results.Conflict(new
        {
            message = "服务器 ID 已存在。"
        }),
        AdminCatalogMutationStatus.ClientProfileNotFound => Results.ValidationProblem(
            new Dictionary<string, string[]>
            {
                ["clientProfileId"] = ["客户端档案不存在或未启用。"]
            }),
        _ => Results.Problem(
            title: "服务器目录创建失败",
            statusCode: StatusCodes.Status500InternalServerError)
    };
}

async Task<IResult> UpdateAdminServerAsync(
    string serverId,
    AdminServerUpdateRequest request,
    AdminCatalogRepository repository,
    HttpContext context,
    CancellationToken cancellationToken)
{
    if (!AdminServerRules.IsValidServerId(serverId))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["serverId"] = ["服务器 ID 无效。"]
        });
    }

    var errors = AdminServerRules.Validate(request);
    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors);
    }

    var actor = context.User.GetPlayer();
    if (actor?.AccessTier != AccessTier.Administrator)
    {
        return Results.Forbid();
    }

    var result = await repository.UpdateServerAsync(
        serverId,
        request,
        actor.UserId,
        context.Connection.RemoteIpAddress,
        cancellationToken);
    return MapAdminMutationResult(result);
}

async Task<IResult> SetAdminServerVisibilityAsync(
    string serverId,
    AdminServerVisibilityRequest request,
    AdminCatalogRepository repository,
    HttpContext context,
    CancellationToken cancellationToken)
{
    if (!AdminServerRules.IsValidServerId(serverId))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["serverId"] = ["服务器 ID 无效。"]
        });
    }

    var errors = AdminServerRules.Validate(request);
    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors);
    }

    var actor = context.User.GetPlayer();
    if (actor?.AccessTier != AccessTier.Administrator)
    {
        return Results.Forbid();
    }

    var result = await repository.SetServerVisibilityAsync(
        serverId,
        request,
        actor.UserId,
        context.Connection.RemoteIpAddress,
        cancellationToken);
    return MapAdminMutationResult(result);
}

async Task<IResult> GetAdminAuditLogsAsync(
    long? beforeId,
    int? limit,
    AdminCatalogRepository repository,
    CancellationToken cancellationToken)
{
    var pageSize = limit ?? 100;
    if (pageSize is < 1 or > 200 || beforeId is <= 0)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["pagination"] = ["limit 必须在 1 到 200 之间，beforeId 必须为正整数。"]
        });
    }

    return Results.Ok(await repository.GetAuditLogsAsync(
        beforeId,
        pageSize,
        cancellationToken));
}

IResult MapAdminMutationResult(AdminCatalogMutationResult result)
{
    return result.Status switch
    {
        AdminCatalogMutationStatus.Success => Results.Ok(result.Server),
        AdminCatalogMutationStatus.NotFound => Results.NotFound(),
        AdminCatalogMutationStatus.RevisionConflict => Results.Conflict(new
        {
            message = "服务器目录已被其他管理员修改，请刷新后重试。",
            current = result.Server
        }),
        AdminCatalogMutationStatus.ClientProfileNotFound => Results.ValidationProblem(
            new Dictionary<string, string[]>
            {
                ["clientProfileId"] = ["客户端档案不存在或未启用。"]
            }),
        _ => Results.Problem(
            title: "服务器目录更新失败",
            statusCode: StatusCodes.Status500InternalServerError)
    };
}

IResult? ValidateLuckPermsSnapshot(LuckPermsSnapshotRequest request)
{
    var now = DateTimeOffset.UtcNow;
    if (request.Players.Count is < 1 or > 5000 ||
        request.CapturedAt < now.AddHours(-1) ||
        request.CapturedAt > now.AddMinutes(5))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["snapshot"] = ["快照为空、过大或时间戳不在允许范围内。"]
        });
    }

    if (request.Players.Select(player => player.MinecraftUuid).Distinct().Count() != request.Players.Count)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["players"] = ["快照包含重复的 Minecraft UUID。"]
        });
    }

    if (request.Players.Any(player =>
            !Regex.IsMatch(player.MinecraftName, "^[A-Za-z0-9_]{3,16}$") ||
            !Regex.IsMatch(player.PrimaryGroup, "^[a-z0-9][a-z0-9._-]{0,63}$")))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["players"] = ["快照包含无效的玩家名或 LuckPerms 主组。"]
        });
    }

    return null;
}

IResult? ValidateVelocityAuthorizationRequest(
    VelocityAuthorizationRequest request,
    out IPAddress? remoteAddress)
{
    remoteAddress = null;
    if (request.MinecraftUuid == Guid.Empty ||
        !Regex.IsMatch(request.MinecraftName ?? string.Empty, "^[A-Za-z0-9_]{3,16}$") ||
        !Regex.IsMatch(request.VelocityTarget ?? string.Empty, "^[a-z0-9][a-z0-9._-]{0,63}$") ||
        !Regex.IsMatch(request.ProxyInstance ?? string.Empty, "^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$"))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["request"] = ["玩家名、Velocity 目标或代理实例名称无效。"]
        });
    }

    if (!string.IsNullOrWhiteSpace(request.RemoteAddress) &&
        !IPAddress.TryParse(request.RemoteAddress, out remoteAddress))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["remoteAddress"] = ["玩家来源 IP 地址无效。"]
        });
    }

    return null;
}

IResult AuthenticationProblem(int statusCode, string detail)
{
    return Results.Problem(
        title: "身份验证失败",
        detail: detail,
        statusCode: statusCode);
}

bool IsValidOssConfiguration(DistributionOptions options)
{
    if (!options.HasAnyOssConfiguration)
    {
        return true;
    }

    if (!options.HasCompleteOssConfiguration ||
        !Regex.IsMatch(options.OssRegion, "^[a-z0-9-]{3,63}$", RegexOptions.CultureInvariant) ||
        !Regex.IsMatch(options.OssBucket, "^[a-z0-9-]{3,63}$", RegexOptions.CultureInvariant) ||
        options.OssObjectPrefix.Length > 256 ||
        options.OssObjectPrefix.Contains('\\'))
    {
        return false;
    }

    var segments = options.OssObjectPrefix.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
    if (segments.Any(segment => segment is "." or ".." || segment.Any(char.IsControl)))
    {
        return false;
    }

    return Uri.TryCreate(options.OssEndpoint, UriKind.Absolute, out var endpoint) &&
           endpoint.Scheme == Uri.UriSchemeHttps &&
           string.IsNullOrEmpty(endpoint.UserInfo) &&
           string.IsNullOrEmpty(endpoint.Query) &&
           string.IsNullOrEmpty(endpoint.Fragment);
}

public partial class Program;

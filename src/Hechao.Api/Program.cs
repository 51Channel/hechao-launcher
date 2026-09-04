using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Mail;
using System.Reflection;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Hechao.Api.Admin;
using Hechao.Api.Authentication;
using Hechao.Api.Catalog;
using Hechao.Api.Database;
using Hechao.Api.Diagnostics;
using Hechao.Api.Distribution;
using Hechao.Api.LuckPerms;
using Hechao.Api.Monitoring;
using Hechao.Api.PackageImports;
using Hechao.Api.ServerControl;
using Hechao.Api.Telemetry;
using Hechao.Api.Velocity;
using Hechao.Contracts;
using Hechao.Distribution;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);

builder.WebHost
    .UseUrls(builder.Configuration["urls"] ?? "http://127.0.0.1:8090")
    .ConfigureKestrel(options => options.AddServerHeader = false);

builder.Services.AddProblemDetails();
builder.Services.Configure<PasswordHasherOptions>(options =>
    options.IterationCount = 100_000);
builder.Services.AddSingleton<
    IPasswordHasher<HechaoAccountPasswordSubject>,
    PasswordHasher<HechaoAccountPasswordSubject>>();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOptions<AdminWebOptions>()
    .Bind(builder.Configuration.GetSection(AdminWebOptions.SectionName))
    .Validate(
        options => options.TicketSeconds is >= 30 and <= 300,
        "AdminWeb:TicketSeconds must be between 30 and 300.")
    .Validate(
        options => options.SessionMinutes is >= 10 and <= 120,
        "AdminWeb:SessionMinutes must be between 10 and 120.")
    .Validate(
        options => options.EnrollmentMinutes is >= 5 and <= 30,
        "AdminWeb:EnrollmentMinutes must be between 5 and 30.")
    .Validate(
        options => options.TrustedDeviceDays is >= 1 and <= 90,
        "AdminWeb:TrustedDeviceDays must be between 1 and 90.")
    .Validate(
        options => options.TryGetPublicBaseUri(out _),
        "AdminWeb:PublicBaseUrl must be an HTTPS origin or loopback origin.")
    .Validate(
        options => !options.Enabled ||
                   Path.IsPathFullyQualified(options.DataProtectionKeyPath),
        "AdminWeb:DataProtectionKeyPath must be absolute when the admin console is enabled.")
    .ValidateOnStart();
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
builder.Services.AddOptions<ForumAccountBridgeOptions>()
    .Bind(builder.Configuration.GetSection(ForumAccountBridgeOptions.SectionName))
    .Validate(
        options => string.IsNullOrEmpty(options.InternalTokenSha256) ||
                   Regex.IsMatch(options.InternalTokenSha256, "^[0-9a-fA-F]{64}$"),
        "ForumAccountBridge:InternalTokenSha256 must be empty or a SHA-256 hex digest.")
    .ValidateOnStart();
builder.Services.AddOptions<ForumSessionRevocationOptions>()
    .Bind(builder.Configuration.GetSection(ForumSessionRevocationOptions.SectionName))
    .Validate(
        options => !options.Enabled || options.TryGetBaseUri(out _),
        "ForumSessionRevocation:BaseUrl must be a loopback HTTP origin.")
    .Validate(
        options => !options.Enabled || options.HasValidToken(),
        "ForumSessionRevocation:InternalToken must contain 32 to 256 non-whitespace characters.")
    .Validate(
        options => options.DeliveryIntervalSeconds is >= 1 and <= 300,
        "ForumSessionRevocation:DeliveryIntervalSeconds must be between 1 and 300.")
    .Validate(
        options => options.RequestTimeoutSeconds is >= 1 and <= 30,
        "ForumSessionRevocation:RequestTimeoutSeconds must be between 1 and 30.")
    .Validate(
        options => options.LeaseSeconds >= options.RequestTimeoutSeconds + 5 &&
                   options.LeaseSeconds <= 300,
        "ForumSessionRevocation:LeaseSeconds must exceed the request timeout and be at most 300.")
    .Validate(
        options => options.BatchSize is >= 1 and <= 100,
        "ForumSessionRevocation:BatchSize must be between 1 and 100.")
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
    .Validate(
        options => options.RuntimeHistoryRetentionDays is >= 7 and <= 90,
        "ServerHeartbeats:RuntimeHistoryRetentionDays must be between 7 and 90.")
    .Validate(
        options => options.RuntimeHistoryCleanupHours is >= 1 and <= 24,
        "ServerHeartbeats:RuntimeHistoryCleanupHours must be between 1 and 24.")
    .ValidateOnStart();
builder.Services.AddOptions<ServerControlOptions>()
    .Bind(builder.Configuration.GetSection(ServerControlOptions.SectionName))
    .Validate(
        options => options.IsValid(),
        "ServerControl configuration is invalid.")
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
builder.Services.AddOptions<LauncherUpdateOptions>()
    .Bind(builder.Configuration.GetSection(LauncherUpdateOptions.SectionName))
    .Validate(
        options => options.IsValid(),
        "LauncherUpdates configuration is invalid.")
    .ValidateOnStart();
builder.Services.AddOptions<DiagnosticUploadOptions>()
    .Bind(builder.Configuration.GetSection(DiagnosticUploadOptions.SectionName))
    .Validate(
        options => options.HasValidStorageRoot(),
        "DiagnosticUploads:StorageRoot must be an absolute path.")
    .Validate(
        options => options.UploadTokenMinutes is >= 2 and <= 30,
        "DiagnosticUploads:UploadTokenMinutes must be between 2 and 30.")
    .Validate(
        options => options.RetentionDays is >= 1 and <= 90,
        "DiagnosticUploads:RetentionDays must be between 1 and 90.")
    .Validate(
        options => options.MaximumBytes is >= 1024 * 1024 and <= 64L * 1024 * 1024,
        "DiagnosticUploads:MaximumBytes must be between 1 MiB and 64 MiB.")
    .Validate(
        options => options.MaximumUploadsPerDay is >= 1 and <= 50,
        "DiagnosticUploads:MaximumUploadsPerDay must be between 1 and 50.")
    .Validate(
        options => options.MaximumBytesPerDay >= options.MaximumBytes &&
                   options.MaximumBytesPerDay <= 1024L * 1024 * 1024,
        "DiagnosticUploads:MaximumBytesPerDay is invalid.")
    .Validate(
        options => options.MaximumActiveUploads is >= 1 and <= 100,
        "DiagnosticUploads:MaximumActiveUploads must be between 1 and 100.")
    .Validate(
        options => options.CleanupMinutes is >= 5 and <= 1440,
        "DiagnosticUploads:CleanupMinutes must be between 5 and 1440.")
    .ValidateOnStart();
builder.Services.AddOptions<PackageImportOptions>()
    .Bind(builder.Configuration.GetSection(PackageImportOptions.SectionName))
    .Validate(
        options => options.IsValid(),
        "PackageImports configuration is invalid.")
    .ValidateOnStart();
builder.Services.AddOptions<LauncherTelemetryOptions>()
    .Bind(builder.Configuration.GetSection(LauncherTelemetryOptions.SectionName))
    .Validate(
        options => options.RetentionDays is >= 7 and <= 90,
        "LauncherTelemetry:RetentionDays must be between 7 and 90.")
    .Validate(
        options => options.CleanupHours is >= 1 and <= 24,
        "LauncherTelemetry:CleanupHours must be between 1 and 24.")
    .ValidateOnStart();
builder.Services.AddOptions<OperationalAlertOptions>()
    .Bind(builder.Configuration.GetSection(OperationalAlertOptions.SectionName))
    .Validate(
        options => string.IsNullOrEmpty(options.InternalTokenSha256) ||
                   Regex.IsMatch(
                       options.InternalTokenSha256,
                       "^[0-9a-fA-F]{64}$"),
        "OperationalAlerts:InternalTokenSha256 must be empty or a SHA-256 hex digest.")
    .Validate(
        options => options.EvaluationSeconds is >= 30 and <= 300,
        "OperationalAlerts:EvaluationSeconds must be between 30 and 300.")
    .Validate(
        options => options.EvaluationWindowMinutes is >= 5 and <= 60,
        "OperationalAlerts:EvaluationWindowMinutes must be between 5 and 60.")
    .Validate(
        options => options.RequestMetricsRetentionDays is >= 7 and <= 90,
        "OperationalAlerts:RequestMetricsRetentionDays must be between 7 and 90.")
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
    options.OnRejected = (context, _) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter =
                Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
        }

        return ValueTask.CompletedTask;
    };
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
    options.AddPolicy("internal-server-control", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "local",
            _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 180,
                QueueLimit = 0,
                Window = TimeSpan.FromMinutes(1)
            }));
    options.AddPolicy("internal-package-publisher", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "local",
            _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 240,
                QueueLimit = 0,
                Window = TimeSpan.FromMinutes(1)
            }));
    options.AddPolicy("internal-alerts", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "local",
            _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 120,
                QueueLimit = 0,
                Window = TimeSpan.FromMinutes(1)
            }));
    options.AddPolicy("internal-forum", context =>
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
        RateLimitPartition.GetTokenBucketLimiter(
            context.User.FindFirstValue(ClaimTypes.NameIdentifier) ??
            context.Connection.RemoteIpAddress?.ToString() ??
            "anonymous",
            _ => new TokenBucketRateLimiterOptions
            {
                AutoReplenishment = true,
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                TokenLimit = 192,
                TokensPerPeriod = 80
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
    options.AddPolicy("diagnostics", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.User.FindFirstValue(ClaimTypes.NameIdentifier) ??
            context.Connection.RemoteIpAddress?.ToString() ??
            "anonymous",
            _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 10,
                QueueLimit = 0,
                Window = TimeSpan.FromMinutes(10)
            }));
    options.AddPolicy("diagnostic-upload", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "local",
            _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 20,
                QueueLimit = 0,
                Window = TimeSpan.FromMinutes(10)
            }));
    options.AddPolicy("telemetry", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.User.FindFirstValue(ClaimTypes.NameIdentifier) ??
            context.Connection.RemoteIpAddress?.ToString() ??
            "anonymous",
            _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 60,
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
    options.AddPolicy("admin-auth", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "local",
            _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 10,
                QueueLimit = 0,
                Window = TimeSpan.FromMinutes(1)
            }));
    options.AddPolicy("admin-mfa", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ??
            "anonymous",
            _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 10,
                QueueLimit = 0,
                Window = TimeSpan.FromMinutes(5)
            }));
});

var dataProtectionBuilder = builder.Services
    .AddDataProtection()
    .SetApplicationName("Hechao.Launcher.AdminWeb");
var dataProtectionKeyPath = builder.Configuration[
    $"{AdminWebOptions.SectionName}:DataProtectionKeyPath"];
if (!string.IsNullOrWhiteSpace(dataProtectionKeyPath))
{
    dataProtectionBuilder.PersistKeysToFileSystem(
        new DirectoryInfo(dataProtectionKeyPath));
}

builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "__Host-HechaoAdminCsrf";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
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
builder.Services.AddSingleton<AdminProfileReleaseRepository>();
builder.Services.AddSingleton<AdminAccessRepository>();
builder.Services.AddSingleton<LuckPermsTierCommandRepository>();
builder.Services.AddSingleton<AdminAccountSecurityRepository>();
builder.Services.AddSingleton<AdminWebTokenGenerator>();
builder.Services.AddSingleton<AdminTotpService>();
builder.Services.AddSingleton<AdminWebSessionRepository>();
builder.Services.AddSingleton<AdminTrustedDeviceRepository>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<ProfileManifestStore>();
builder.Services.AddSingleton<DistributionTrustBundleProvider>();
builder.Services.AddSingleton<OssPresignedUrlFactory>();
builder.Services.AddSingleton<SessionTokenGenerator>();
builder.Services.AddSingleton<HechaoAccountPasswordService>();
builder.Services.AddSingleton<AuthenticationRepository>();
builder.Services.AddSingleton<ForumAccountBridgeTokenValidator>();
builder.Services.AddSingleton<ForumSessionRevocationRepository>();
builder.Services.AddSingleton<InternalSyncTokenValidator>();
builder.Services.AddSingleton<LuckPermsSyncRepository>();
builder.Services.AddSingleton<VelocityAuthorizationTokenValidator>();
builder.Services.AddSingleton<VelocityAuthorizationRepository>();
builder.Services.AddSingleton<ServerHeartbeatTokenValidator>();
builder.Services.AddSingleton<ServerHeartbeatRepository>();
builder.Services.AddSingleton<ServerRuntimeStatusRepository>();
builder.Services.AddSingleton<ServerControlTokenValidator>();
builder.Services.AddSingleton<ServerControlRepository>();
builder.Services.AddSingleton<PackageImportRepository>();
builder.Services.AddSingleton<PackageImportStorage>();
builder.Services.AddSingleton<PackageImportOrchestrationRepository>();
builder.Services.AddSingleton<PackagePublisherTokenValidator>();
builder.Services.AddSingleton<PackagePublisherCompletionService>();
builder.Services.AddHostedService<PackageImportAnalysisService>();
builder.Services.AddHostedService<PackageImportOrchestrationService>();
builder.Services.AddHostedService<ServerRuntimeSampleCleanupService>();
builder.Services.AddSingleton<ApiRequestMetricsCollector>();
builder.Services.AddSingleton<OperationalAlertTokenValidator>();
builder.Services.AddSingleton<OperationalAlertRepository>();
builder.Services.AddHostedService<ApiRequestMetricsFlushService>();
builder.Services.AddHostedService<OperationalAlertEvaluationService>();
builder.Services.AddSingleton(serviceProvider =>
    serviceProvider.GetRequiredService<IOptions<DiagnosticUploadOptions>>().Value);
builder.Services.AddSingleton<DiagnosticUploadStorage>();
builder.Services.AddSingleton<DiagnosticUploadRepository>();
builder.Services.AddHostedService<DiagnosticUploadCleanupService>();
builder.Services.AddSingleton<LauncherTelemetryRepository>();
builder.Services.AddHostedService<LauncherTelemetryCleanupService>();
builder.Services.AddHttpClient<ForumSessionRevocationClient>();
builder.Services.AddHostedService<ForumSessionRevocationDeliveryService>();
builder.Services.AddHttpClient<MinecraftServicesClient>(client =>
{
    client.BaseAddress = new Uri("https://api.minecraftservices.com/");
    client.Timeout = TimeSpan.FromSeconds(10);
    client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Hechao.Launcher.Api", "0.9.0"));
    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
});
builder.Services
    .AddAuthentication(LauncherSessionAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, LauncherSessionAuthenticationHandler>(
        LauncherSessionAuthenticationHandler.SchemeName,
        _ => { })
    .AddScheme<AuthenticationSchemeOptions, AdminWebSessionAuthenticationHandler>(
        AdminWebSessionAuthenticationHandler.SchemeName,
        _ => { });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(
        AdminAuthorization.BootstrapPolicyName,
        policy => policy
            .AddAuthenticationSchemes(LauncherSessionAuthenticationHandler.SchemeName)
            .RequireAuthenticatedUser()
            .RequireRole(nameof(AccessTier.Administrator)));
    options.AddPolicy(
        AdminAuthorization.WebSessionPolicyName,
        policy => policy
            .AddAuthenticationSchemes(AdminWebSessionAuthenticationHandler.SchemeName)
            .RequireAuthenticatedUser()
            .RequireRole(nameof(AccessTier.Administrator)));
    options.AddPolicy(
        AdminAuthorization.PolicyName,
        policy => policy
            .AddAuthenticationSchemes(AdminWebSessionAuthenticationHandler.SchemeName)
            .RequireAuthenticatedUser()
            .RequireRole(nameof(AccessTier.Administrator))
            .RequireClaim(AdminWebClaimTypes.AuthenticationMethod, "mfa"));
});

var app = builder.Build();
var adminWebOptions = app.Services
    .GetRequiredService<IOptions<AdminWebOptions>>()
    .Value;

app.UseForwardedHeaders();
app.UseMiddleware<ApiRequestMetricsMiddleware>();
app.UseExceptionHandler();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/admin") &&
        (!adminWebOptions.Enabled ||
         !adminWebOptions.IsExpectedHost(context.Request.Host)))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    context.Response.Headers["Cache-Control"] = "no-store";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["X-Request-Id"] = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
    if (context.Request.Path.StartsWithSegments("/admin"))
    {
        context.Response.Headers["Content-Security-Policy"] =
            "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; " +
            "connect-src 'self'; font-src 'self'; object-src 'none'; base-uri 'none'; " +
            "form-action 'self'; frame-ancestors 'none'";
    }

    await next();
});
app.UseMiddleware<AdminWebCanonicalPathMiddleware>();
app.UseStaticFiles();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.UseAntiforgery();

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

app.MapPost("/v1/auth/register", () => Results.Problem(
        title: "请通过赫朝社区完成注册",
        detail: "此版本起，赫朝启动器与 hechao.world 共用账号。请升级启动器或前往社区注册。",
        statusCode: StatusCodes.Status426UpgradeRequired))
    .RequireRateLimiting("authentication");
app.MapPost("/v1/auth/login", LoginHechaoAccountAsync)
    .RequireRateLimiting("authentication");
app.MapPost("/v1/auth/minecraft/link", LinkMinecraftIdentityAsync)
    .RequireAuthorization()
    .RequireRateLimiting("authentication");
app.MapPost("/v1/auth/minecraft/unlink", UnlinkMinecraftIdentityAsync)
    .RequireAuthorization()
    .RequireRateLimiting("authentication");
app.MapPost("/v1/auth/minecraft/exchange", ExchangeMinecraftSessionAsync)
    .RequireRateLimiting("authentication");
app.MapPost("/v1/auth/refresh", RefreshSessionAsync)
    .RequireRateLimiting("authentication");
app.MapPost("/v1/auth/logout", LogoutAsync)
    .RequireAuthorization();
app.MapPost("/v1/auth/logout-all", LogoutAllAsync)
    .RequireAuthorization()
    .RequireRateLimiting("authentication");
app.MapGet("/v1/me", GetCurrentAccount)
    .RequireAuthorization();
app.MapPost("/v1/telemetry/events", ImportLauncherTelemetryAsync)
    .RequireAuthorization()
    .RequireRateLimiting("telemetry");
app.MapPost("/v1/velocity/launch-grants", CreateVelocityLaunchGrantAsync)
    .RequireAuthorization()
    .RequireRateLimiting("authentication");
app.MapPost("/v1/internal/velocity/authorize", AuthorizeVelocityConnectionAsync)
    .RequireRateLimiting("internal-velocity");
app.MapPost("/v1/internal/luckperms/snapshot", ImportLuckPermsSnapshotAsync)
    .RequireRateLimiting("internal-sync");
app.MapPost(
        "/v1/internal/luckperms/tier-commands/claim",
        ClaimLuckPermsTierCommandsAsync)
    .RequireRateLimiting("internal-sync");
app.MapPost(
        "/v1/internal/luckperms/tier-commands/{commandId:guid}/complete",
        CompleteLuckPermsTierCommandAsync)
    .RequireRateLimiting("internal-sync");
app.MapPost("/v1/internal/server-heartbeats", ImportServerHeartbeatsAsync)
    .RequireRateLimiting("internal-heartbeats");
app.MapPost(
        "/v1/internal/server-control/heartbeat",
        ImportServerControlHeartbeatAsync)
    .RequireRateLimiting("internal-server-control");
app.MapPost(
        "/v1/internal/server-control/commands/claim",
        ClaimServerControlCommandsAsync)
    .RequireRateLimiting("internal-server-control");
app.MapPost(
        "/v1/internal/server-control/commands/{commandId:guid}/complete",
        CompleteServerControlCommandAsync)
    .RequireRateLimiting("internal-server-control");
app.MapServerControlPackageArchives();
app.MapPackagePublisher();
app.MapPost(
        "/v1/internal/operational-alerts/events",
        ImportOperationalAlertEventAsync)
    .RequireRateLimiting("internal-alerts");
app.MapGet(
        "/v1/internal/operational-alerts/active",
        GetActiveOperationalAlertsAsync)
    .RequireRateLimiting("internal-alerts");
app.MapPost("/v1/internal/forum/accounts/register", RegisterForumAccountAsync)
    .RequireRateLimiting("internal-forum");
app.MapPost("/v1/internal/forum/accounts/authenticate", AuthenticateForumAccountAsync)
    .RequireRateLimiting("internal-forum");
app.MapPost("/v1/internal/forum/accounts/import", ImportLegacyForumAccountAsync)
    .RequireRateLimiting("internal-forum");
app.MapPost("/v1/internal/forum/accounts/password/change", ChangeForumAccountPasswordAsync)
    .RequireRateLimiting("internal-forum");
app.MapPost("/v1/internal/forum/accounts/password/reset", ResetForumAccountPasswordAsync)
    .RequireRateLimiting("internal-forum");
app.MapPost("/v1/internal/forum/accounts/profile", UpdateForumAccountProfileAsync)
    .RequireRateLimiting("internal-forum");
app.MapGet("/v1/catalog", GetCatalogAsync)
    .RequireRateLimiting("catalog");
app.MapGet("/v1/public/activities", GetPublicActivitiesAsync)
    .RequireRateLimiting("catalog");
app.MapGet("/v1/public/launcher/latest", GetPublicLauncherRelease)
    .RequireRateLimiting("catalog");
app.MapGet("/v1/public/launcher/download", DownloadPublicLauncher)
    .RequireRateLimiting("downloads");
app.MapGet("/v1/launcher/update", GetLauncherUpdate)
    .RequireAuthorization()
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
    .AddEndpointFilter<AdminWebHostFilter>()
    .RequireAuthorization(AdminAuthorization.PolicyName)
    .RequireRateLimiting("admin");
adminApi.MapGet("/catalog/servers", GetAdminServersAsync);
adminApi.MapGet("/catalog/servers/{serverId}", GetAdminServerAsync);
adminApi.MapGet("/catalog/client-profiles", GetAdminClientProfilesAsync);
adminApi.MapGet(
    "/catalog/client-profiles/{profileId}",
    GetAdminClientProfileAsync);
adminApi.MapPost(
        "/catalog/client-profiles",
        CreateAdminClientProfileAsync)
    .AddEndpointFilter<AdminAntiforgeryFilter>();
adminApi.MapPut(
        "/catalog/client-profiles/{profileId}",
        UpdateAdminClientProfileAsync)
    .AddEndpointFilter<AdminAntiforgeryFilter>();
adminApi.MapPost(
        "/catalog/client-profiles/{profileId}/releases",
        ImportAdminClientProfileReleaseAsync)
    .AddEndpointFilter<AdminAntiforgeryFilter>();
adminApi.MapPut(
        "/catalog/client-profiles/{profileId}/channels/{channel}",
        SetAdminClientProfileChannelAsync)
    .AddEndpointFilter<AdminAntiforgeryFilter>();
adminApi.MapPost(
        "/catalog/client-profiles/{profileId}/channels/{channel}/rollback",
        RollbackAdminClientProfileChannelAsync)
    .AddEndpointFilter<AdminAntiforgeryFilter>();
adminApi.MapPut(
        "/catalog/client-profiles/{profileId}/releases/{manifestSha256}/pause",
        SetAdminClientProfileReleasePauseAsync)
    .AddEndpointFilter<AdminAntiforgeryFilter>();
adminApi.MapPost("/catalog/servers", CreateAdminServerAsync)
    .AddEndpointFilter<AdminAntiforgeryFilter>();
adminApi.MapPut("/catalog/servers/{serverId}", UpdateAdminServerAsync)
    .AddEndpointFilter<AdminAntiforgeryFilter>();
adminApi.MapPut("/catalog/servers/{serverId}/visibility", SetAdminServerVisibilityAsync)
    .AddEndpointFilter<AdminAntiforgeryFilter>();
adminApi.MapGet("/users", SearchAdminUsersAsync);
adminApi.MapGet("/users/{userId:guid}/access-preview", GetAdminUserAccessPreviewAsync);
adminApi.MapGet("/users/{userId:guid}/security", GetAdminUserSecurityAsync);
adminApi.MapPut(
        "/users/{userId:guid}/access-tier",
        QueueAdminUserAccessTierChangeAsync)
    .AddEndpointFilter<AdminAntiforgeryFilter>();
adminApi.MapPost("/users/{userId:guid}/account/disable", DisableAdminUserAccountAsync)
    .AddEndpointFilter<AdminAntiforgeryFilter>();
adminApi.MapPost("/users/{userId:guid}/account/enable", EnableAdminUserAccountAsync)
    .AddEndpointFilter<AdminAntiforgeryFilter>();
adminApi.MapPost("/users/{userId:guid}/sessions/revoke-all", RevokeAllAdminUserSessionsAsync)
    .AddEndpointFilter<AdminAntiforgeryFilter>();
adminApi.MapPost(
        "/users/{userId:guid}/sessions/{sessionId:guid}/revoke",
        RevokeAdminUserSessionAsync)
    .AddEndpointFilter<AdminAntiforgeryFilter>();
adminApi.MapPut("/users/{userId:guid}/minecraft-ban", SetAdminMinecraftIdentityBanAsync)
    .AddEndpointFilter<AdminAntiforgeryFilter>();
adminApi.MapDelete("/users/{userId:guid}/minecraft-ban", RevokeAdminMinecraftIdentityBanAsync)
    .AddEndpointFilter<AdminAntiforgeryFilter>();
adminApi.MapPut(
        "/users/{userId:guid}/access-rules/{serverId}",
        UpsertAdminServerAccessRuleAsync)
    .AddEndpointFilter<AdminAntiforgeryFilter>();
adminApi.MapDelete(
        "/users/{userId:guid}/access-rules/{serverId}",
        DeleteAdminServerAccessRuleAsync)
    .AddEndpointFilter<AdminAntiforgeryFilter>();
adminApi.MapGet("/audit-logs", GetAdminAuditLogsAsync);
adminApi.MapGet("/telemetry/summary", GetAdminLauncherTelemetrySummaryAsync);
adminApi.MapGet("/server-runtime/summary", GetAdminServerRuntimeSummaryAsync);
adminApi.MapGet("/server-control/overview", GetAdminServerControlOverviewAsync);
adminApi.MapGet(
    "/server-control/targets/{serverId}",
    GetAdminServerControlTargetAsync);
adminApi.MapGet(
    "/server-control/operations/{operationId:guid}",
    GetAdminServerControlOperationAsync);
adminApi.MapPost(
        "/server-control/targets/{serverId}/operations",
        QueueAdminServerControlOperationAsync)
    .AddEndpointFilter<AdminAntiforgeryFilter>();
adminApi.MapGet("/operational-alerts", GetAdminOperationalAlertsAsync);
adminApi.MapPost(
        "/operational-alerts/{fingerprint}/acknowledge",
        AcknowledgeAdminOperationalAlertAsync)
    .AddEndpointFilter<AdminAntiforgeryFilter>();
app.MapDiagnosticUploads(adminApi);
adminApi.MapAdminPackageImports();

app.MapAdminWebEndpoints();
app.MapFallbackToFile("/admin/{*path:nonfile}", "admin/index.html");

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

async Task<IResult> ImportLauncherTelemetryAsync(
    LauncherTelemetryBatchRequest request,
    LauncherTelemetryRepository repository,
    TimeProvider timeProvider,
    HttpContext context,
    CancellationToken cancellationToken)
{
    var account = context.User.GetAccount();
    if (account is null)
    {
        return AuthenticationProblem(
            StatusCodes.Status401Unauthorized,
            "登录会话无效。");
    }

    var errors = LauncherTelemetryRules.Validate(
        request,
        timeProvider.GetUtcNow());
    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors);
    }

    return Results.Ok(await repository.ImportAsync(
        account.UserId,
        request,
        cancellationToken));
}

async Task<IResult> LoginHechaoAccountAsync(
    HechaoAccountLoginRequest request,
    AuthenticationRepository authenticationRepository,
    HttpContext context,
    CancellationToken cancellationToken)
{
    var usernameOrEmail = request.UsernameOrEmail?.Trim().ToLowerInvariant() ?? string.Empty;
    var password = request.Password ?? string.Empty;
    if (usernameOrEmail.Length is < 3 or > 254 ||
        password.Length is < 1 or > 128)
    {
        return AuthenticationProblem(
            StatusCodes.Status401Unauthorized,
            "赫朝账号或密码不正确。");
    }

    var response = await authenticationRepository.LoginAccountAsync(
        usernameOrEmail,
        password,
        context.Connection.RemoteIpAddress,
        context.Request.Headers.UserAgent.ToString(),
        cancellationToken);
    return response is null
        ? AuthenticationProblem(
            StatusCodes.Status401Unauthorized,
            "赫朝账号或密码不正确。")
        : Results.Ok(response);
}

async Task<IResult> RegisterForumAccountAsync(
    ForumAccountRegisterRequest request,
    ForumAccountBridgeTokenValidator tokenValidator,
    AuthenticationRepository authenticationRepository,
    HttpContext context,
    CancellationToken cancellationToken)
{
    var authorizationProblem = ValidateForumBridgeRequest(context, tokenValidator);
    if (authorizationProblem is not null)
    {
        return authorizationProblem;
    }

    var username = request.Username?.Trim().ToLowerInvariant() ?? string.Empty;
    var displayName = request.DisplayName?.Trim() ?? string.Empty;
    var email = request.Email?.Trim().ToLowerInvariant() ?? string.Empty;
    var password = request.Password ?? string.Empty;
    var errors = ValidateHechaoAccountRegistration(
        username,
        displayName,
        password,
        email);
    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors);
    }

    try
    {
        var account = await authenticationRepository.RegisterForumAccountAsync(
            username,
            displayName,
            password,
            email,
            context.Connection.RemoteIpAddress,
            cancellationToken);
        return Results.Created("/v1/me", account);
    }
    catch (HechaoAccountConflictException exception)
    {
        return AccountConflictProblem(exception);
    }
}

async Task<IResult> AuthenticateForumAccountAsync(
    ForumAccountAuthenticateRequest request,
    ForumAccountBridgeTokenValidator tokenValidator,
    AuthenticationRepository authenticationRepository,
    HttpContext context,
    CancellationToken cancellationToken)
{
    var authorizationProblem = ValidateForumBridgeRequest(context, tokenValidator);
    if (authorizationProblem is not null)
    {
        return authorizationProblem;
    }

    var usernameOrEmail = request.UsernameOrEmail?.Trim().ToLowerInvariant() ?? string.Empty;
    var password = request.Password ?? string.Empty;
    if (usernameOrEmail.Length is < 3 or > 254 ||
        password.Length is < 1 or > 128)
    {
        return AuthenticationProblem(
            StatusCodes.Status401Unauthorized,
            "赫朝账号或密码不正确。");
    }

    var account = await authenticationRepository.AuthenticateForumAccountAsync(
        usernameOrEmail,
        password,
        context.Connection.RemoteIpAddress,
        cancellationToken);
    return account is null
        ? AuthenticationProblem(
            StatusCodes.Status401Unauthorized,
            "赫朝账号或密码不正确。")
        : Results.Ok(account);
}

async Task<IResult> ImportLegacyForumAccountAsync(
    ForumLegacyAccountImportRequest request,
    ForumAccountBridgeTokenValidator tokenValidator,
    IOptions<ForumAccountBridgeOptions> bridgeOptions,
    HechaoAccountPasswordService passwordService,
    AuthenticationRepository authenticationRepository,
    HttpContext context,
    CancellationToken cancellationToken)
{
    var authorizationProblem = ValidateForumBridgeRequest(context, tokenValidator);
    if (authorizationProblem is not null)
    {
        return authorizationProblem;
    }

    if (!bridgeOptions.Value.AllowLegacyImport)
    {
        return Results.Problem(
            title: "论坛旧账号导入已关闭",
            statusCode: StatusCodes.Status403Forbidden);
    }

    var forumUserId = request.ForumUserId?.Trim() ?? string.Empty;
    var username = request.Username?.Trim().ToLowerInvariant() ?? string.Empty;
    var displayName = request.DisplayName?.Trim() ?? string.Empty;
    var email = request.Email?.Trim().ToLowerInvariant() ?? string.Empty;
    var passwordHash = request.PasswordHash ?? string.Empty;
    var errors = ValidateHechaoAccountRegistration(
        username,
        displayName,
        "LegacyPass123",
        email);
    if (!Regex.IsMatch(forumUserId, "^[1-9][0-9]{0,18}$"))
    {
        errors["forumUserId"] = ["论坛用户 ID 无效。"];
    }
    if (!passwordService.IsSupportedLegacyHash(passwordHash))
    {
        errors["passwordHash"] = ["论坛旧密码哈希格式无效。"];
    }
    if (request.CreatedAt < new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero) ||
        request.CreatedAt > DateTimeOffset.UtcNow.AddMinutes(5))
    {
        errors["createdAt"] = ["论坛账号创建时间无效。"];
    }
    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors);
    }

    try
    {
        var response = await authenticationRepository.ImportLegacyForumAccountAsync(
            forumUserId,
            username,
            displayName,
            email,
            passwordHash,
            request.IsDisabled,
            request.CreatedAt,
            context.Connection.RemoteIpAddress,
            cancellationToken);
        return Results.Ok(response);
    }
    catch (HechaoAccountConflictException exception)
    {
        return AccountConflictProblem(exception);
    }
}

async Task<IResult> ChangeForumAccountPasswordAsync(
    ForumAccountPasswordChangeRequest request,
    ForumAccountBridgeTokenValidator tokenValidator,
    AuthenticationRepository authenticationRepository,
    HttpContext context,
    CancellationToken cancellationToken)
{
    var authorizationProblem = ValidateForumBridgeRequest(context, tokenValidator);
    if (authorizationProblem is not null)
    {
        return authorizationProblem;
    }

    if (!IsValidPasswordShape(request.NewPassword))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["newPassword"] = ["密码需要 10–128 个字符，并同时包含字母和数字。"]
        });
    }

    var result = await authenticationRepository.ChangeForumAccountPasswordAsync(
        request.UserId,
        request.CurrentPassword ?? string.Empty,
        request.NewPassword,
        context.Connection.RemoteIpAddress,
        cancellationToken);
    return result switch
    {
        ForumPasswordChangeResult.Success => Results.NoContent(),
        ForumPasswordChangeResult.InvalidPassword => AuthenticationProblem(
            StatusCodes.Status403Forbidden,
            "当前密码不正确。"),
        ForumPasswordChangeResult.InvalidNewPassword => Results.ValidationProblem(
            new Dictionary<string, string[]>
            {
                ["newPassword"] = ["新密码不能与赫朝账号名相同。"]
            }),
        _ => Results.NotFound()
    };
}

async Task<IResult> ResetForumAccountPasswordAsync(
    ForumAccountPasswordResetRequest request,
    ForumAccountBridgeTokenValidator tokenValidator,
    AuthenticationRepository authenticationRepository,
    HttpContext context,
    CancellationToken cancellationToken)
{
    var authorizationProblem = ValidateForumBridgeRequest(context, tokenValidator);
    if (authorizationProblem is not null)
    {
        return authorizationProblem;
    }

    if (!IsValidPasswordShape(request.NewPassword))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["newPassword"] = ["密码需要 10–128 个字符，并同时包含字母和数字。"]
        });
    }

    return await authenticationRepository.ResetForumAccountPasswordAsync(
        request.UserId,
        request.NewPassword,
        context.Connection.RemoteIpAddress,
        cancellationToken)
        ? Results.NoContent()
        : Results.NotFound();
}

async Task<IResult> UpdateForumAccountProfileAsync(
    ForumAccountProfileUpdateRequest request,
    ForumAccountBridgeTokenValidator tokenValidator,
    AuthenticationRepository authenticationRepository,
    HttpContext context,
    CancellationToken cancellationToken)
{
    var authorizationProblem = ValidateForumBridgeRequest(context, tokenValidator);
    if (authorizationProblem is not null)
    {
        return authorizationProblem;
    }

    var displayName = request.DisplayName?.Trim() ?? string.Empty;
    if (displayName.Length is < 2 or > 32 || displayName.Any(char.IsControl))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["displayName"] = ["显示名称需要 2–32 个字符，且不能包含控制字符。"]
        });
    }

    try
    {
        var account = await authenticationRepository.UpdateForumAccountDisplayNameAsync(
            request.UserId,
            displayName,
            context.Connection.RemoteIpAddress,
            cancellationToken);
        return account is null ? Results.NotFound() : Results.Ok(account);
    }
    catch (HechaoAccountConflictException exception)
    {
        return AccountConflictProblem(exception);
    }
}

async Task<IResult> LinkMinecraftIdentityAsync(
    MinecraftIdentityLinkRequest request,
    MinecraftServicesClient minecraftServices,
    AuthenticationRepository authenticationRepository,
    HttpContext context,
    CancellationToken cancellationToken)
{
    var account = context.User.GetAccount();
    if (account is null)
    {
        return AuthenticationProblem(
            StatusCodes.Status401Unauthorized,
            "赫朝账号登录会话无效。");
    }

    try
    {
        var identity = await minecraftServices.VerifyAsync(
            request.MinecraftAccessToken,
            cancellationToken);
        var linkedAccount = await authenticationRepository.LinkMinecraftIdentityAsync(
            account.UserId,
            identity,
            context.Connection.RemoteIpAddress,
            cancellationToken);
        return Results.Ok(linkedAccount);
    }
    catch (MinecraftIdentityAlreadyLinkedException)
    {
        return AuthenticationProblem(
            StatusCodes.Status409Conflict,
            "该 Minecraft 正版身份已绑定其他赫朝账号。");
    }
    catch (HechaoAccountMinecraftLinkConflictException)
    {
        return AuthenticationProblem(
            StatusCodes.Status409Conflict,
            "该赫朝账号已经绑定其他 Minecraft 正版身份。");
    }
    catch (MinecraftIdentityBannedException)
    {
        return AuthenticationProblem(
            StatusCodes.Status403Forbidden,
            "该 Minecraft 正版身份已被管理员封禁。");
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

async Task<IResult> UnlinkMinecraftIdentityAsync(
    MinecraftIdentityUnlinkRequest request,
    AuthenticationRepository authenticationRepository,
    HttpContext context,
    CancellationToken cancellationToken)
{
    var account = context.User.GetAccount();
    if (account is null)
    {
        return AuthenticationProblem(
            StatusCodes.Status401Unauthorized,
            "赫朝账号登录会话无效。");
    }

    if (string.IsNullOrEmpty(request.CurrentPassword) ||
        request.CurrentPassword.Length > 128)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["currentPassword"] = ["请输入当前赫朝账号密码。"]
        });
    }

    var result = await authenticationRepository.UnlinkMinecraftIdentityAsync(
        account.UserId,
        request.CurrentPassword,
        context.Connection.RemoteIpAddress,
        cancellationToken);
    return result switch
    {
        MinecraftIdentityUnlinkResult.Success => Results.NoContent(),
        MinecraftIdentityUnlinkResult.InvalidPassword => AuthenticationProblem(
            StatusCodes.Status403Forbidden,
            "当前赫朝账号密码不正确。"),
        MinecraftIdentityUnlinkResult.NotLinked => AuthenticationProblem(
            StatusCodes.Status409Conflict,
            "该赫朝账号尚未绑定 Minecraft 正版身份。"),
        MinecraftIdentityUnlinkResult.IdentityBanned => AuthenticationProblem(
            StatusCodes.Status403Forbidden,
            "该 Minecraft 正版身份处于封禁状态，解除封禁前不能更换绑定。"),
        _ => AuthenticationProblem(
            StatusCodes.Status401Unauthorized,
            "赫朝账号登录会话无效。")
    };
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
    catch (MinecraftIdentityBannedException)
    {
        return AuthenticationProblem(
            StatusCodes.Status403Forbidden,
            "该 Minecraft 正版身份已被管理员封禁。");
    }
}

async Task<IResult> RefreshSessionAsync(
    RefreshSessionRequest request,
    AuthenticationRepository repository,
    CancellationToken cancellationToken)
{
    var response = await repository.RefreshSessionAsync(request.RefreshToken, cancellationToken);
    return response is null
        ? AuthenticationProblem(StatusCodes.Status401Unauthorized, "登录会话已过期，请重新登录赫朝账号。")
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

async Task<IResult> LogoutAllAsync(
    AuthenticationRepository repository,
    HttpContext context,
    CancellationToken cancellationToken)
{
    var account = context.User.GetAccount();
    if (account is null)
    {
        return AuthenticationProblem(
            StatusCodes.Status401Unauthorized,
            "赫朝账号登录会话无效。");
    }

    var response = await repository.RevokeAllSessionsAsync(
        account.UserId,
        context.Connection.RemoteIpAddress,
        cancellationToken);
    return Results.Ok(response);
}

IResult GetCurrentAccount(HttpContext context)
{
    var account = context.User.GetAccount();
    return account is null
        ? AuthenticationProblem(StatusCodes.Status401Unauthorized, "登录会话无效。")
        : Results.Ok(account);
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

async Task<IResult> ClaimLuckPermsTierCommandsAsync(
    LuckPermsTierCommandClaimRequest request,
    InternalSyncTokenValidator tokenValidator,
    LuckPermsTierCommandRepository repository,
    HttpContext context,
    CancellationToken cancellationToken)
{
    var authenticationFailure = ValidateInternalSyncToken(tokenValidator, context);
    if (authenticationFailure is not null)
    {
        return authenticationFailure;
    }

    var errors = AdminLuckPermsTierRules.Validate(request);
    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors);
    }

    return Results.Ok(await repository.ClaimAsync(
        request.AgentId.Trim(),
        request.Limit,
        DateTimeOffset.UtcNow,
        TimeSpan.FromSeconds(90),
        cancellationToken));
}

async Task<IResult> CompleteLuckPermsTierCommandAsync(
    Guid commandId,
    LuckPermsTierCommandCompletionRequest request,
    InternalSyncTokenValidator tokenValidator,
    LuckPermsTierCommandRepository repository,
    HttpContext context,
    CancellationToken cancellationToken)
{
    var authenticationFailure = ValidateInternalSyncToken(tokenValidator, context);
    if (authenticationFailure is not null)
    {
        return authenticationFailure;
    }

    var errors = AdminLuckPermsTierRules.Validate(request);
    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors);
    }

    var result = await repository.CompleteAsync(
        commandId,
        request,
        DateTimeOffset.UtcNow,
        cancellationToken);
    return result.Status switch
    {
        LuckPermsTierCompletionStatus.Success => Results.Ok(result.Command),
        LuckPermsTierCompletionStatus.CommandNotFound => Results.NotFound(),
        LuckPermsTierCompletionStatus.ClaimConflict => Results.Conflict(new
        {
            message = "等级变更命令已由其他代理接管或已经完成。",
            current = result.Command
        }),
        LuckPermsTierCompletionStatus.OutcomeMismatch => Results.Conflict(new
        {
            message = "代理回传结果与目标等级不一致。",
            current = result.Command
        }),
        _ => Results.Problem(
            title: "等级变更结果提交失败",
            statusCode: StatusCodes.Status500InternalServerError)
    };
}

IResult? ValidateInternalSyncToken(
    InternalSyncTokenValidator tokenValidator,
    HttpContext context)
{
    if (!tokenValidator.IsConfigured)
    {
        return Results.Problem(
            title: "LuckPerms 同步尚未配置",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    var suppliedToken = context.Request.Headers["X-Hechao-Sync-Token"].ToString();
    return tokenValidator.IsValid(suppliedToken)
        ? null
        : AuthenticationProblem(
            StatusCodes.Status401Unauthorized,
            "内部同步凭据无效。");
}

async Task<IResult> ImportServerControlHeartbeatAsync(
    ServerControlAgentHeartbeatRequest request,
    ServerControlTokenValidator tokenValidator,
    ServerControlRepository repository,
    HttpContext context,
    CancellationToken cancellationToken)
{
    var authenticationFailure = ValidateServerControlToken(
        request.AgentId,
        tokenValidator,
        context);
    if (authenticationFailure is not null)
    {
        return authenticationFailure;
    }

    var errors = ServerControlRules.Validate(request);
    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors);
    }

    return Results.Ok(await repository.ImportHeartbeatAsync(
        request,
        DateTimeOffset.UtcNow,
        cancellationToken));
}

async Task<IResult> ClaimServerControlCommandsAsync(
    ServerControlCommandClaimRequest request,
    ServerControlTokenValidator tokenValidator,
    ServerControlRepository repository,
    HttpContext context,
    CancellationToken cancellationToken)
{
    var authenticationFailure = ValidateServerControlToken(
        request.AgentId,
        tokenValidator,
        context);
    if (authenticationFailure is not null)
    {
        return authenticationFailure;
    }

    var errors = ServerControlRules.Validate(request);
    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors);
    }

    return Results.Ok(await repository.ClaimAsync(
        request.AgentId,
        request.Limit,
        DateTimeOffset.UtcNow,
        cancellationToken));
}

async Task<IResult> CompleteServerControlCommandAsync(
    Guid commandId,
    ServerControlCommandCompletionRequest request,
    ServerControlTokenValidator tokenValidator,
    ServerControlRepository repository,
    HttpContext context,
    CancellationToken cancellationToken)
{
    var authenticationFailure = ValidateServerControlToken(
        request.AgentId,
        tokenValidator,
        context);
    if (authenticationFailure is not null)
    {
        return authenticationFailure;
    }

    var errors = ServerControlRules.Validate(request);
    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors);
    }

    var result = await repository.CompleteAsync(
        commandId,
        request,
        DateTimeOffset.UtcNow,
        cancellationToken);
    return result.Status switch
    {
        ServerControlCompletionStatus.Success => Results.Ok(result.Operation),
        ServerControlCompletionStatus.CommandNotFound => Results.NotFound(),
        ServerControlCompletionStatus.ClaimConflict => Results.Conflict(new
        {
            message = "控制命令已由其他代理接管、租约已过期或已经完成。"
        }),
        _ => Results.Problem(
            title: "服务器控制结果提交失败",
            statusCode: StatusCodes.Status500InternalServerError)
    };
}

IResult? ValidateServerControlToken(
    string agentId,
    ServerControlTokenValidator tokenValidator,
    HttpContext context)
{
    if (!tokenValidator.IsConfigured)
    {
        return Results.Problem(
            title: "服务器控制代理尚未配置",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    var suppliedToken =
        context.Request.Headers["X-Hechao-Server-Control-Token"].ToString();
    return tokenValidator.IsValid(agentId, suppliedToken)
        ? null
        : AuthenticationProblem(
            StatusCodes.Status401Unauthorized,
            "服务器控制代理凭据无效。");
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

async Task<IResult> ImportOperationalAlertEventAsync(
    InternalOperationalAlertEventRequest request,
    OperationalAlertTokenValidator tokenValidator,
    OperationalAlertRepository repository,
    TimeProvider timeProvider,
    HttpContext context,
    CancellationToken cancellationToken)
{
    var authenticationFailure =
        ValidateOperationalAlertMonitor(tokenValidator, context);
    if (authenticationFailure is not null)
    {
        return authenticationFailure;
    }

    var errors = OperationalAlertRules.Validate(
        request,
        timeProvider.GetUtcNow());
    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors);
    }

    await repository.ApplyExternalEventAsync(request, cancellationToken);
    return Results.Accepted();
}

async Task<IResult> GetActiveOperationalAlertsAsync(
    OperationalAlertTokenValidator tokenValidator,
    OperationalAlertRepository repository,
    HttpContext context,
    CancellationToken cancellationToken)
{
    var authenticationFailure =
        ValidateOperationalAlertMonitor(tokenValidator, context);
    return authenticationFailure ??
           Results.Ok(await repository.GetActiveSnapshotAsync(
               cancellationToken));
}

IResult? ValidateOperationalAlertMonitor(
    OperationalAlertTokenValidator tokenValidator,
    HttpContext context)
{
    if (context.Connection.RemoteIpAddress is not { } remoteAddress ||
        !IPAddress.IsLoopback(remoteAddress))
    {
        return Results.NotFound();
    }

    if (!tokenValidator.IsConfigured)
    {
        return Results.Problem(
            title: "Operational alert monitor is not configured.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    var suppliedToken =
        context.Request.Headers["X-Hechao-Monitor-Token"].ToString();
    return tokenValidator.IsValid(suppliedToken)
        ? null
        : Results.Problem(
            title: "Operational alert monitor authentication failed.",
            statusCode: StatusCodes.Status401Unauthorized);
}

async Task<IResult> GetCatalogAsync(
    CatalogRepository repository,
    IOptions<LauncherAuthenticationOptions> authenticationOptions,
    HttpContext context,
    CancellationToken cancellationToken)
{
    var account = context.User.GetAccount();
    var hasAuthorizationHeader = context.Request.Headers.ContainsKey("Authorization");
    if (account is null && (hasAuthorizationHeader || authenticationOptions.Value.EnforceCatalogAuthentication))
    {
        return AuthenticationProblem(StatusCodes.Status401Unauthorized, "请先登录赫朝账号。");
    }

    var snapshot = await repository.GetSnapshotAsync(
        account?.UserId,
        account?.AccessTier,
        cancellationToken);
    return Results.Ok(snapshot);
}

async Task<IResult> GetPublicActivitiesAsync(
    CatalogRepository repository,
    CancellationToken cancellationToken)
{
    var catalog = await repository.GetSnapshotAsync(
        userId: null,
        accessTier: null,
        cancellationToken: cancellationToken);
    return Results.Ok(PublicActivityCatalogProjector.Create(catalog));
}

IResult GetPublicLauncherRelease(IOptions<LauncherUpdateOptions> options)
{
    var release = options.Value;
    if (!release.Enabled)
    {
        return Results.NoContent();
    }

    return Results.Ok(new PublicLauncherRelease(
        release.LatestVersion,
        release.InstallerBytes,
        release.InstallerSha256.ToLowerInvariant(),
        release.PublishedAt,
        release.ReleaseNotes));
}

IResult DownloadPublicLauncher(
    IOptions<LauncherUpdateOptions> options,
    OssPresignedUrlFactory urlFactory)
{
    var release = options.Value;
    if (!release.Enabled)
    {
        return Results.Problem(
            title: "启动器下载暂未开放",
            detail: "当前没有可供下载的正式版本。",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    var installerUrl = urlFactory.TryCreateLauncherInstallerUrl(
        release.LatestVersion);
    return installerUrl is null
        ? Results.Problem(
            title: "启动器下载暂时不可用",
            detail: "安装文件尚未准备完成，请稍后重试。",
            statusCode: StatusCodes.Status503ServiceUnavailable)
        : new PrivateDownloadRedirectResult(installerUrl);
}

IResult GetLauncherUpdate(
    IOptions<LauncherUpdateOptions> options,
    OssPresignedUrlFactory urlFactory)
{
    var release = options.Value;
    if (!release.Enabled)
    {
        return Results.NoContent();
    }

    var installerUrl = urlFactory.TryCreateLauncherInstallerUrl(
        release.LatestVersion);
    if (installerUrl is null)
    {
        return Results.Problem(
            title: "启动器更新暂时不可用",
            detail: "更新文件尚未准备完成，请稍后重试。",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    return Results.Ok(new LauncherUpdateRelease(
        release.LatestVersion,
        release.MinimumSupportedVersion,
        release.InstallerBytes,
        release.InstallerSha256.ToLowerInvariant(),
        release.PublishedAt,
        release.ReleaseNotes,
        installerUrl));
}

async Task<IResult> GetProfileManifestAsync(
    string profileId,
    CatalogRepository catalogRepository,
    ProfileManifestStore manifestStore,
    HttpContext context,
    CancellationToken cancellationToken)
{
    var account = context.User.GetAccount();
    if (account is null)
    {
        return AuthenticationProblem(StatusCodes.Status401Unauthorized, "请先登录赫朝账号。");
    }

    var profile = await catalogRepository.GetAccessibleProfileAsync(
        account.UserId,
        account.AccessTier,
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

    var account = context.User.GetAccount();
    if (account is null)
    {
        return AuthenticationProblem(StatusCodes.Status401Unauthorized, "请先登录赫朝账号。");
    }

    var profile = await catalogRepository.GetAccessibleProfileAsync(
        account.UserId,
        account.AccessTier,
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
        : new PrivateDownloadRedirectResult(downloadUrl);
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
    AdminProfileReleaseRepository repository,
    CancellationToken cancellationToken)
{
    return Results.Ok(await repository.GetProfilesAsync(cancellationToken));
}

async Task<IResult> GetAdminClientProfileAsync(
    string profileId,
    AdminProfileReleaseRepository repository,
    CancellationToken cancellationToken)
{
    if (!AdminProfileReleaseRules.IsValidProfileId(profileId))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["profileId"] = ["客户端档案 ID 无效。"]
        });
    }

    var detail = await repository.GetDetailAsync(profileId, cancellationToken);
    return detail is null ? Results.NotFound() : Results.Ok(detail);
}

async Task<IResult> CreateAdminClientProfileAsync(
    AdminClientProfileCreateRequest request,
    AdminProfileReleaseRepository repository,
    HttpContext context,
    CancellationToken cancellationToken)
{
    var errors = AdminProfileReleaseRules.Validate(request);
    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors);
    }

    var actor = context.User.GetPlayer();
    if (actor?.AccessTier != AccessTier.Administrator)
    {
        return Results.Forbid();
    }

    var result = await repository.CreateProfileAsync(
        request,
        actor.UserId,
        context.Connection.RemoteIpAddress,
        cancellationToken);
    if (result.Status == AdminProfileMutationStatus.DuplicateId)
    {
        return Results.Conflict(new { message = "客户端档案 ID 已存在。" });
    }

    if (result.Status != AdminProfileMutationStatus.Success)
    {
        return MapAdminProfileMutationResult(result);
    }

    var detail = await repository.GetDetailAsync(request.Id, cancellationToken);
    return Results.Created(
        $"/v1/admin/catalog/client-profiles/{request.Id}",
        detail);
}

async Task<IResult> UpdateAdminClientProfileAsync(
    string profileId,
    AdminClientProfileUpdateRequest request,
    AdminProfileReleaseRepository repository,
    HttpContext context,
    CancellationToken cancellationToken)
{
    if (!AdminProfileReleaseRules.IsValidProfileId(profileId))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["profileId"] = ["客户端档案 ID 无效。"]
        });
    }

    var errors = AdminProfileReleaseRules.Validate(request);
    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors);
    }

    var actor = context.User.GetPlayer();
    if (actor?.AccessTier != AccessTier.Administrator)
    {
        return Results.Forbid();
    }

    var result = await repository.UpdateProfileAsync(
        profileId,
        request,
        actor.UserId,
        context.Connection.RemoteIpAddress,
        cancellationToken);
    return await MapAdminProfileMutationWithDetailAsync(
        profileId,
        result,
        repository,
        cancellationToken);
}

async Task<IResult> ImportAdminClientProfileReleaseAsync(
    string profileId,
    AdminProfileReleaseRepository repository,
    ProfileManifestStore manifestStore,
    DistributionTrustBundleProvider trustBundleProvider,
    IOptions<DistributionOptions> distributionOptions,
    HttpContext context,
    CancellationToken cancellationToken)
{
    if (!AdminProfileReleaseRules.IsValidProfileId(profileId))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["profileId"] = ["客户端档案 ID 无效。"]
        });
    }

    var actor = context.User.GetPlayer();
    if (actor?.AccessTier != AccessTier.Administrator)
    {
        return Results.Forbid();
    }

    if (context.Request.ContentLength is <= 0 ||
        context.Request.ContentLength > distributionOptions.Value.MaximumManifestBytes)
    {
        return Results.Problem(
            title: "签名清单大小无效",
            detail: "请选择有效的签名 JSON 清单。",
            statusCode: StatusCodes.Status413PayloadTooLarge);
    }

    byte[] envelope;
    try
    {
        envelope = await ReadLimitedRequestBodyAsync(
            context.Request,
            distributionOptions.Value.MaximumManifestBytes,
            cancellationToken);
    }
    catch (InvalidDataException exception)
    {
        return Results.Problem(
            title: "签名清单大小无效",
            detail: exception.Message,
            statusCode: StatusCodes.Status413PayloadTooLarge);
    }

    ValidatedProfileReleaseManifest manifest;
    try
    {
        manifest = ProfileReleaseManifestValidator.Validate(
            envelope,
            profileId,
            trustBundleProvider.TrustBundle);
    }
    catch (Exception exception) when (
        exception is ManifestFormatException or
            ManifestIntegrityException or
            ManifestSignatureException or
            OverflowException)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["manifest"] = [$"签名清单验证失败：{exception.Message}"]
        });
    }

    StoredProfileManifest storedManifest;
    try
    {
        storedManifest = await manifestStore.StoreReleaseAsync(
            profileId,
            manifest.ManifestSha256,
            envelope,
            cancellationToken);
    }
    catch (Exception exception) when (
        exception is IOException or UnauthorizedAccessException)
    {
        return Results.Problem(
            title: "无法保存签名清单",
            detail: exception.Message,
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    var result = await repository.ImportReleaseAsync(
        manifest,
        actor.UserId,
        context.Connection.RemoteIpAddress,
        cancellationToken);
    if (result.Status != AdminProfileMutationStatus.Success)
    {
        manifestStore.DeleteStoredRelease(storedManifest);
        return MapAdminProfileMutationResult(result);
    }

    var detail = await repository.GetDetailAsync(profileId, cancellationToken);
    return Results.Created(
        $"/v1/admin/catalog/client-profiles/{profileId}",
        detail);
}

async Task<IResult> SetAdminClientProfileChannelAsync(
    string profileId,
    ClientProfileReleaseChannel channel,
    AdminClientProfileChannelUpdateRequest request,
    AdminProfileReleaseRepository repository,
    HttpContext context,
    CancellationToken cancellationToken)
{
    if (!AdminProfileReleaseRules.IsValidProfileId(profileId))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["profileId"] = ["客户端档案 ID 无效。"]
        });
    }

    var errors = AdminProfileReleaseRules.Validate(channel, request);
    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors);
    }

    var actor = context.User.GetPlayer();
    if (actor?.AccessTier != AccessTier.Administrator)
    {
        return Results.Forbid();
    }

    var result = await repository.SetChannelAsync(
        profileId,
        channel,
        request,
        actor.UserId,
        context.Connection.RemoteIpAddress,
        cancellationToken);
    return await MapAdminProfileMutationWithDetailAsync(
        profileId,
        result,
        repository,
        cancellationToken);
}

async Task<IResult> RollbackAdminClientProfileChannelAsync(
    string profileId,
    ClientProfileReleaseChannel channel,
    AdminClientProfileChannelRollbackRequest request,
    AdminProfileReleaseRepository repository,
    HttpContext context,
    CancellationToken cancellationToken)
{
    if (!AdminProfileReleaseRules.IsValidProfileId(profileId))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["profileId"] = ["客户端档案 ID 无效。"]
        });
    }

    var errors = AdminProfileReleaseRules.Validate(request);
    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors);
    }

    var actor = context.User.GetPlayer();
    if (actor?.AccessTier != AccessTier.Administrator)
    {
        return Results.Forbid();
    }

    var result = await repository.RollbackChannelAsync(
        profileId,
        channel,
        request,
        actor.UserId,
        context.Connection.RemoteIpAddress,
        cancellationToken);
    return await MapAdminProfileMutationWithDetailAsync(
        profileId,
        result,
        repository,
        cancellationToken);
}

async Task<IResult> SetAdminClientProfileReleasePauseAsync(
    string profileId,
    string manifestSha256,
    AdminClientProfileReleasePauseRequest request,
    AdminProfileReleaseRepository repository,
    HttpContext context,
    CancellationToken cancellationToken)
{
    if (!AdminProfileReleaseRules.IsValidProfileId(profileId) ||
        !AdminProfileReleaseRules.IsValidManifestSha256(manifestSha256))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["release"] = ["客户端档案或发布清单 SHA-256 无效。"]
        });
    }

    var errors = AdminProfileReleaseRules.Validate(request);
    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors);
    }

    var actor = context.User.GetPlayer();
    if (actor?.AccessTier != AccessTier.Administrator)
    {
        return Results.Forbid();
    }

    var result = await repository.SetReleasePauseAsync(
        profileId,
        manifestSha256,
        request,
        actor.UserId,
        context.Connection.RemoteIpAddress,
        cancellationToken);
    return await MapAdminProfileMutationWithDetailAsync(
        profileId,
        result,
        repository,
        cancellationToken);
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
        AdminCatalogMutationStatus.InfrastructureServer => Results.ValidationProblem(
            new Dictionary<string, string[]>
            {
                ["role"] = ["内部基础设施服务器不能转换为玩家服务器或恢复到玩家目录。"]
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

async Task<IResult> GetAdminLauncherTelemetrySummaryAsync(
    int? hours,
    LauncherTelemetryRepository repository,
    CancellationToken cancellationToken)
{
    var windowHours = hours ?? 24;
    if (!LauncherTelemetryRules.IsSupportedWindow(windowHours))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["hours"] = ["统计窗口只支持 24、168 或 720 小时。"]
        });
    }

    return Results.Ok(await repository.GetSummaryAsync(
        windowHours,
        cancellationToken));
}

async Task<IResult> GetAdminServerRuntimeSummaryAsync(
    ServerRuntimeStatusRepository repository,
    CancellationToken cancellationToken) =>
    Results.Ok(await repository.GetSummaryAsync(cancellationToken));

async Task<IResult> GetAdminServerControlOverviewAsync(
    ServerControlRepository repository,
    CancellationToken cancellationToken) =>
    Results.Ok(await repository.GetOverviewAsync(
        DateTimeOffset.UtcNow,
        cancellationToken));

async Task<IResult> GetAdminServerControlTargetAsync(
    string serverId,
    ServerControlRepository repository,
    CancellationToken cancellationToken)
{
    var detail = await repository.GetTargetDetailAsync(
        serverId,
        DateTimeOffset.UtcNow,
        cancellationToken);
    return detail is null ? Results.NotFound() : Results.Ok(detail);
}

async Task<IResult> GetAdminServerControlOperationAsync(
    Guid operationId,
    ServerControlRepository repository,
    CancellationToken cancellationToken)
{
    var operation = await repository.GetOperationAsync(
        operationId,
        cancellationToken);
    return operation is null ? Results.NotFound() : Results.Ok(operation);
}

async Task<IResult> QueueAdminServerControlOperationAsync(
    string serverId,
    AdminServerControlRequest request,
    ServerControlRepository repository,
    HttpContext context,
    CancellationToken cancellationToken)
{
    var errors = ServerControlRules.Validate(serverId, request);
    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors);
    }

    var actor = context.User.GetPlayer();
    if (actor?.AccessTier != AccessTier.Administrator)
    {
        return Results.Forbid();
    }

    ServerControlQueueMutationResult result;
    try
    {
        result = await repository.QueueAsync(
            serverId,
            request,
            actor.UserId,
            context.Connection.RemoteIpAddress,
            DateTimeOffset.UtcNow,
            cancellationToken);
    }
    catch (PostgresException exception) when (
        exception.SqlState == PostgresErrorCodes.SerializationFailure)
    {
        return Results.Conflict(new
        {
            message = "服务器控制状态刚刚发生变化，请刷新后重试。"
        });
    }

    return result.Status switch
    {
        ServerControlQueueStatus.Success => Results.Accepted(
            $"/v1/admin/server-control/operations/" +
            result.Result!.Operation.OperationId.ToString("D"),
            result.Result),
        ServerControlQueueStatus.FeatureDisabled => Results.Problem(
            title: "服务器控制功能尚未启用",
            statusCode: StatusCodes.Status503ServiceUnavailable),
        ServerControlQueueStatus.TargetNotFound => Results.NotFound(),
        ServerControlQueueStatus.AgentUnavailable => Results.Conflict(new
        {
            message = "该服务器的控制代理当前离线，未执行任何动作。"
        }),
        ServerControlQueueStatus.StateStale => Results.Conflict(new
        {
            message = "冲突组中存在状态过期的服务器，未执行启动。",
            servers = result.BlockingServerIds
        }),
        ServerControlQueueStatus.OperationInProgress => Results.Conflict(new
        {
            message = "相关服务器已有控制动作进行中。",
            servers = result.BlockingServerIds
        }),
        ServerControlQueueStatus.CommandNotAllowed => Results.ValidationProblem(
            new Dictionary<string, string[]>
            {
                ["consoleCommand"] = ["该命令不在此服务器的本机允许列表中。"]
            }),
        ServerControlQueueStatus.TargetOffline => Results.Conflict(new
        {
            message = "服务器未运行，不能发送控制台命令。"
        }),
        _ => Results.Problem(
            title: "服务器控制动作排队失败",
            statusCode: StatusCodes.Status500InternalServerError)
    };
}

async Task<IResult> GetAdminOperationalAlertsAsync(
    OperationalAlertRepository repository,
    CancellationToken cancellationToken) =>
    Results.Ok(await repository.GetAdminSummaryAsync(cancellationToken));

async Task<IResult> AcknowledgeAdminOperationalAlertAsync(
    string fingerprint,
    OperationalAlertRepository repository,
    HttpContext context,
    CancellationToken cancellationToken)
{
    if (!OperationalAlertRules.IsValidFingerprint(fingerprint))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["fingerprint"] = ["告警指纹格式无效。"]
        });
    }

    var actor = context.User.GetPlayer();
    if (actor?.AccessTier != AccessTier.Administrator)
    {
        return Results.Forbid();
    }

    return await repository.AcknowledgeAsync(
        fingerprint,
        actor.UserId,
        cancellationToken)
        ? Results.NoContent()
        : Results.NotFound();
}

async Task<IResult> SearchAdminUsersAsync(
    string? query,
    int? limit,
    AdminAccessRepository repository,
    CancellationToken cancellationToken)
{
    var normalizedQuery = query?.Trim() ?? string.Empty;
    var pageSize = limit ?? 50;
    var errors = AdminAccessRules.ValidateSearch(normalizedQuery, pageSize);
    return errors.Count > 0
        ? Results.ValidationProblem(errors)
        : Results.Ok(await repository.SearchUsersAsync(
            normalizedQuery,
            pageSize,
            cancellationToken));
}

async Task<IResult> GetAdminUserAccessPreviewAsync(
    Guid userId,
    AdminAccessRepository repository,
    CancellationToken cancellationToken)
{
    var preview = await repository.GetAccessPreviewAsync(userId, cancellationToken);
    return preview is null ? Results.NotFound() : Results.Ok(preview);
}

async Task<IResult> GetAdminUserSecurityAsync(
    Guid userId,
    AdminAccountSecurityRepository repository,
    CancellationToken cancellationToken)
{
    var security = await repository.GetSecurityAsync(userId, cancellationToken);
    return security is null ? Results.NotFound() : Results.Ok(security);
}

async Task<IResult> QueueAdminUserAccessTierChangeAsync(
    Guid userId,
    AdminLuckPermsTierChangeRequest request,
    LuckPermsTierCommandRepository repository,
    HttpContext context,
    CancellationToken cancellationToken)
{
    var errors = AdminLuckPermsTierRules.Validate(request);
    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors);
    }

    var actor = context.User.GetPlayer();
    if (actor?.AccessTier != AccessTier.Administrator)
    {
        return Results.Forbid();
    }

    var result = await repository.QueueAsync(
        userId,
        request,
        actor.UserId,
        context.Connection.RemoteIpAddress,
        cancellationToken);
    return result.Status switch
    {
        AdminLuckPermsTierMutationStatus.Success => Results.Accepted(
            $"/v1/admin/users/{userId:D}/security",
            result.Command),
        AdminLuckPermsTierMutationStatus.UserNotFound => Results.NotFound(),
        AdminLuckPermsTierMutationStatus.MinecraftIdentityNotLinked =>
            Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["userId"] = ["该账号尚未绑定 Minecraft 正版身份。"]
            }),
        AdminLuckPermsTierMutationStatus.SelfProtection => Results.Conflict(new
        {
            message = "不能修改当前管理员自身的全局等级。"
        }),
        AdminLuckPermsTierMutationStatus.LastAdministrator => Results.Conflict(new
        {
            message = "不能降级最后一个可用管理员。"
        }),
        AdminLuckPermsTierMutationStatus.RevisionConflict => Results.Conflict(new
        {
            message = "LuckPerms 主组已变化，请刷新后重试。",
            currentPrimaryGroup = result.CurrentPrimaryGroup
        }),
        AdminLuckPermsTierMutationStatus.CommandPending => Results.Conflict(new
        {
            message = "该玩家已有等级变更正在处理。",
            current = result.Command
        }),
        AdminLuckPermsTierMutationStatus.NoChange => Results.Conflict(new
        {
            message = "目标等级与当前等级相同。",
            currentPrimaryGroup = result.CurrentPrimaryGroup
        }),
        _ => Results.Problem(
            title: "等级变更排队失败",
            statusCode: StatusCodes.Status500InternalServerError)
    };
}

Task<IResult> DisableAdminUserAccountAsync(
    Guid userId,
    AdminSecurityReasonRequest request,
    AdminAccountSecurityRepository repository,
    HttpContext context,
    CancellationToken cancellationToken)
{
    return SetAdminUserAccountDisabledAsync(
        userId,
        isDisabled: true,
        request,
        repository,
        context,
        cancellationToken);
}

Task<IResult> EnableAdminUserAccountAsync(
    Guid userId,
    AdminSecurityReasonRequest request,
    AdminAccountSecurityRepository repository,
    HttpContext context,
    CancellationToken cancellationToken)
{
    return SetAdminUserAccountDisabledAsync(
        userId,
        isDisabled: false,
        request,
        repository,
        context,
        cancellationToken);
}

async Task<IResult> SetAdminUserAccountDisabledAsync(
    Guid userId,
    bool isDisabled,
    AdminSecurityReasonRequest request,
    AdminAccountSecurityRepository repository,
    HttpContext context,
    CancellationToken cancellationToken)
{
    var errors = AdminAccountSecurityRules.Validate(request);
    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors);
    }

    var actor = context.User.GetPlayer();
    if (actor?.AccessTier != AccessTier.Administrator)
    {
        return Results.Forbid();
    }

    var result = await repository.SetAccountDisabledAsync(
        userId,
        isDisabled,
        request.Reason,
        actor.UserId,
        context.Connection.RemoteIpAddress,
        cancellationToken);
    return MapAdminAccountSecurityMutationResult(result);
}

async Task<IResult> RevokeAllAdminUserSessionsAsync(
    Guid userId,
    AdminSecurityReasonRequest request,
    AdminAccountSecurityRepository repository,
    HttpContext context,
    CancellationToken cancellationToken)
{
    var errors = AdminAccountSecurityRules.Validate(request);
    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors);
    }

    var actor = context.User.GetPlayer();
    if (actor?.AccessTier != AccessTier.Administrator)
    {
        return Results.Forbid();
    }

    var result = await repository.RevokeAllSessionsAsync(
        userId,
        request.Reason,
        actor.UserId,
        context.Connection.RemoteIpAddress,
        cancellationToken);
    return MapAdminAccountSecurityMutationResult(result);
}

async Task<IResult> RevokeAdminUserSessionAsync(
    Guid userId,
    Guid sessionId,
    AdminSecurityReasonRequest request,
    AdminAccountSecurityRepository repository,
    HttpContext context,
    CancellationToken cancellationToken)
{
    var errors = AdminAccountSecurityRules.Validate(request);
    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors);
    }

    var actor = context.User.GetPlayer();
    if (actor?.AccessTier != AccessTier.Administrator)
    {
        return Results.Forbid();
    }

    var result = await repository.RevokeSessionAsync(
        userId,
        sessionId,
        request.Reason,
        actor.UserId,
        context.Connection.RemoteIpAddress,
        cancellationToken);
    return MapAdminAccountSecurityMutationResult(result);
}

async Task<IResult> SetAdminMinecraftIdentityBanAsync(
    Guid userId,
    AdminMinecraftIdentityBanRequest request,
    AdminAccountSecurityRepository repository,
    HttpContext context,
    CancellationToken cancellationToken)
{
    var errors = AdminAccountSecurityRules.Validate(request, DateTimeOffset.UtcNow);
    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors);
    }

    var actor = context.User.GetPlayer();
    if (actor?.AccessTier != AccessTier.Administrator)
    {
        return Results.Forbid();
    }

    var result = await repository.SetMinecraftIdentityBanAsync(
        userId,
        request,
        actor.UserId,
        context.Connection.RemoteIpAddress,
        cancellationToken);
    return MapAdminAccountSecurityMutationResult(result);
}

async Task<IResult> RevokeAdminMinecraftIdentityBanAsync(
    Guid userId,
    [FromBody] AdminMinecraftIdentityBanDeleteRequest request,
    AdminAccountSecurityRepository repository,
    HttpContext context,
    CancellationToken cancellationToken)
{
    var errors = AdminAccountSecurityRules.Validate(request);
    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors);
    }

    var actor = context.User.GetPlayer();
    if (actor?.AccessTier != AccessTier.Administrator)
    {
        return Results.Forbid();
    }

    var result = await repository.RevokeMinecraftIdentityBanAsync(
        userId,
        request,
        actor.UserId,
        context.Connection.RemoteIpAddress,
        cancellationToken);
    return MapAdminAccountSecurityMutationResult(result);
}

async Task<IResult> UpsertAdminServerAccessRuleAsync(
    Guid userId,
    string serverId,
    AdminServerAccessRuleUpsertRequest request,
    AdminAccessRepository repository,
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

    var errors = AdminAccessRules.Validate(request, DateTimeOffset.UtcNow);
    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors);
    }

    var actor = context.User.GetPlayer();
    if (actor?.AccessTier != AccessTier.Administrator)
    {
        return Results.Forbid();
    }

    var result = await repository.UpsertRuleAsync(
        userId,
        serverId,
        request,
        actor.UserId,
        context.Connection.RemoteIpAddress,
        cancellationToken);
    return MapAdminAccessMutationResult(result);
}

async Task<IResult> DeleteAdminServerAccessRuleAsync(
    Guid userId,
    string serverId,
    [FromBody] AdminServerAccessRuleDeleteRequest request,
    AdminAccessRepository repository,
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

    var errors = AdminAccessRules.Validate(request);
    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors);
    }

    var actor = context.User.GetPlayer();
    if (actor?.AccessTier != AccessTier.Administrator)
    {
        return Results.Forbid();
    }

    var result = await repository.DeleteRuleAsync(
        userId,
        serverId,
        request.ExpectedRevision,
        actor.UserId,
        context.Connection.RemoteIpAddress,
        cancellationToken);
    return MapAdminAccessMutationResult(result);
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
        AdminCatalogMutationStatus.InfrastructureServer => Results.ValidationProblem(
            new Dictionary<string, string[]>
            {
                ["role"] = ["内部基础设施服务器不能转换为玩家服务器或恢复到玩家目录。"]
            }),
        _ => Results.Problem(
            title: "服务器目录更新失败",
            statusCode: StatusCodes.Status500InternalServerError)
    };
}

async Task<IResult> MapAdminProfileMutationWithDetailAsync(
    string profileId,
    AdminProfileMutationResult result,
    AdminProfileReleaseRepository repository,
    CancellationToken cancellationToken)
{
    if (result.Status != AdminProfileMutationStatus.Success)
    {
        return MapAdminProfileMutationResult(result);
    }

    var detail = await repository.GetDetailAsync(profileId, cancellationToken);
    return detail is null ? Results.NotFound() : Results.Ok(detail);
}

IResult MapAdminProfileMutationResult(AdminProfileMutationResult result)
{
    return result.Status switch
    {
        AdminProfileMutationStatus.Success => Results.Ok(result.Detail),
        AdminProfileMutationStatus.NotFound => Results.NotFound(),
        AdminProfileMutationStatus.RevisionConflict => Results.Conflict(new
        {
            message = "客户端档案或发布通道已被其他管理员修改，请刷新后重试。"
        }),
        AdminProfileMutationStatus.DuplicateId => Results.Conflict(new
        {
            message = "客户端档案 ID 已存在。"
        }),
        AdminProfileMutationStatus.DuplicateVersion => Results.Conflict(new
        {
            message = "该档案版本已经存在，版本号不能指向另一份清单。"
        }),
        AdminProfileMutationStatus.ReleaseNotFound => Results.ValidationProblem(
            new Dictionary<string, string[]>
            {
                ["manifestSha256"] = ["所选发布不存在或不属于该客户端档案。"]
            }),
        AdminProfileMutationStatus.ReleasePaused => Results.Conflict(new
        {
            message = "已暂停的发布不能分配到发布通道。"
        }),
        AdminProfileMutationStatus.ProductionReleaseRequired =>
            Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["isActive"] = ["启用档案前必须先配置一个未暂停的正式版本。"]
            }),
        AdminProfileMutationStatus.NoRollbackTarget => Results.Conflict(new
        {
            message = "当前通道没有更早的可用版本可以回滚。"
        }),
        _ => Results.Problem(
            title: "客户端档案发布操作失败",
            statusCode: StatusCodes.Status500InternalServerError)
    };
}

IResult MapAdminAccessMutationResult(AdminAccessMutationResult result)
{
    return result.Status switch
    {
        AdminAccessMutationStatus.Success => result.Rule is null
            ? Results.NoContent()
            : Results.Ok(result.Rule),
        AdminAccessMutationStatus.NotFound => Results.NotFound(),
        AdminAccessMutationStatus.UserNotFound => Results.ValidationProblem(
            new Dictionary<string, string[]>
            {
                ["userId"] = ["玩家账号不存在。"]
            }),
        AdminAccessMutationStatus.ServerNotFound => Results.ValidationProblem(
            new Dictionary<string, string[]>
            {
                ["serverId"] = ["服务器不存在。"]
            }),
        AdminAccessMutationStatus.RevisionConflict => Results.Conflict(new
        {
            message = "单服权限规则已被其他管理员修改，请刷新后重试。",
            current = result.Rule
        }),
        _ => Results.Problem(
            title: "单服权限规则更新失败",
            statusCode: StatusCodes.Status500InternalServerError)
    };
}

async Task<byte[]> ReadLimitedRequestBodyAsync(
    HttpRequest request,
    int maximumBytes,
    CancellationToken cancellationToken)
{
    using var output = new MemoryStream(
        request.ContentLength is > 0 and <= int.MaxValue
            ? (int)request.ContentLength.Value
            : 0);
    var buffer = new byte[64 * 1024];
    while (true)
    {
        var read = await request.Body.ReadAsync(buffer, cancellationToken);
        if (read == 0)
        {
            break;
        }

        if (output.Length + read > maximumBytes)
        {
            throw new InvalidDataException(
                $"签名清单不能超过 {maximumBytes} 字节。");
        }

        await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
    }

    if (output.Length == 0)
    {
        throw new InvalidDataException("签名清单不能为空。");
    }

    return output.ToArray();
}

IResult MapAdminAccountSecurityMutationResult(
    AdminAccountSecurityMutationResult result)
{
    return result.Status switch
    {
        AdminAccountSecurityMutationStatus.Success => Results.Ok(new
        {
            security = result.Security,
            revoked = result.Revoked
        }),
        AdminAccountSecurityMutationStatus.UserNotFound => Results.NotFound(),
        AdminAccountSecurityMutationStatus.SessionNotFound => Results.NotFound(new
        {
            message = "设备会话不存在、已到期或已经撤销。"
        }),
        AdminAccountSecurityMutationStatus.MinecraftIdentityNotLinked =>
            Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["minecraftIdentity"] = ["该账号尚未绑定 Minecraft 正版身份。"]
            }),
        AdminAccountSecurityMutationStatus.MinecraftBanNotFound => Results.NotFound(new
        {
            message = "当前没有可解除的 Minecraft UUID 封禁。"
        }),
        AdminAccountSecurityMutationStatus.SelfProtection => Results.Conflict(new
        {
            message = "不能停用或封禁当前管理员自身。"
        }),
        AdminAccountSecurityMutationStatus.LastAdministrator => Results.Conflict(new
        {
            message = "不能停用最后一个有效管理员账号。"
        }),
        AdminAccountSecurityMutationStatus.RevisionConflict => Results.Conflict(new
        {
            message = "Minecraft UUID 封禁记录已被其他管理员修改，请刷新后重试。",
            current = result.CurrentBan
        }),
        _ => Results.Problem(
            title: "账号安全操作失败",
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

    if (request.SessionServerId is not null &&
        !Regex.IsMatch(request.SessionServerId, "^[a-z0-9][a-z0-9._-]{1,63}$"))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["sessionServerId"] = ["会话来源服务器 ID 无效。"]
        });
    }

    return null;
}

Dictionary<string, string[]> ValidateHechaoAccountRegistration(
    string username,
    string displayName,
    string password,
    string? email)
{
    var errors = new Dictionary<string, string[]>();
    if (!Regex.IsMatch(username, "^[a-z0-9_]{3,24}$"))
    {
        errors["username"] =
        [
            "账号名只能包含 3–24 位小写字母、数字或下划线。"
        ];
    }

    if (displayName.Length is < 2 or > 32 ||
        displayName.Any(char.IsControl))
    {
        errors["displayName"] =
        [
            "显示名称需要 2–32 个字符，且不能包含控制字符。"
        ];
    }

    if (password.Length is < 10 or > 128 ||
        !password.Any(char.IsLetter) ||
        !password.Any(char.IsDigit) ||
        string.Equals(password, username, StringComparison.OrdinalIgnoreCase))
    {
        errors["password"] =
        [
            "密码需要 10–128 个字符，并同时包含字母和数字，且不能与账号名相同。"
        ];
    }

    if (string.IsNullOrWhiteSpace(email))
    {
        errors["email"] = ["请填写用于赫朝社区的邮箱。"];
    }
    else if (!MailAddress.TryCreate(email, out var parsedEmail) ||
         !string.Equals(parsedEmail.Address, email, StringComparison.OrdinalIgnoreCase) ||
         email.Length > 254)
    {
        errors["email"] = ["邮箱格式无效。"];
    }

    return errors;
}

IResult? ValidateForumBridgeRequest(
    HttpContext context,
    ForumAccountBridgeTokenValidator tokenValidator)
{
    if (context.Connection.RemoteIpAddress is not { } remoteAddress ||
        !IPAddress.IsLoopback(remoteAddress))
    {
        return Results.NotFound();
    }

    if (!tokenValidator.IsConfigured)
    {
        return Results.Problem(
            title: "论坛账号同步尚未配置",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    var suppliedToken = context.Request.Headers["X-Hechao-Forum-Token"].ToString();
    return tokenValidator.IsValid(suppliedToken)
        ? null
        : AuthenticationProblem(
            StatusCodes.Status401Unauthorized,
            "论坛账号同步凭据无效。");
}

IResult AccountConflictProblem(HechaoAccountConflictException exception)
{
    var message = exception.Field switch
    {
        "email" => "该邮箱已绑定其他赫朝账号。",
        "displayName" => "该显示名称已被使用。",
        "forumUserId" => "该论坛账号已完成同步。",
        _ => "该赫朝账号名已被使用。"
    };
    return Results.ValidationProblem(new Dictionary<string, string[]>
    {
        [exception.Field] = [message]
    });
}

bool IsValidPasswordShape(string? password) =>
    password is { Length: >= 10 and <= 128 } &&
    password.Any(char.IsLetter) &&
    password.Any(char.IsDigit);

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

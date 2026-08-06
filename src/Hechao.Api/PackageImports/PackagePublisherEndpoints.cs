using Hechao.Contracts;

namespace Hechao.Api.PackageImports;

public static class PackagePublisherEndpoints
{
    private const string TokenHeader = "X-Hechao-Package-Publisher-Token";
    private const string AgentHeader = "X-Hechao-Package-Publisher-Agent";

    public static void MapPackagePublisher(this IEndpointRouteBuilder endpoints)
    {
        var publisher = endpoints.MapGroup(
                "/v1/internal/package-imports/publisher")
            .RequireRateLimiting("internal-package-publisher");
        publisher.MapPost("/heartbeat", HeartbeatAsync);
        publisher.MapPost("/jobs/claim", ClaimAsync);
        publisher.MapGet(
            "/jobs/{importId:guid}/client-archive",
            DownloadClientArchiveAsync);
        publisher.MapPost(
            "/jobs/{importId:guid}/progress",
            ProgressAsync);
        publisher.MapPost(
            "/jobs/{importId:guid}/complete",
            CompleteAsync);
    }

    private static async Task<IResult> ProgressAsync(
        Guid importId,
        PackagePublisherProgressRequest request,
        PackagePublisherTokenValidator tokenValidator,
        PackageImportRepository repository,
        TimeProvider timeProvider,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var authenticationFailure = Authenticate(
            request.AgentId,
            tokenValidator,
            context);
        if (authenticationFailure is not null)
        {
            return authenticationFailure;
        }

        var errors = PackageImportRules.ValidatePublisherProgress(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var result = await repository.ReportPublisherProgressAsync(
            importId,
            request,
            timeProvider.GetUtcNow(),
            cancellationToken);
        return result.Status switch
        {
            PackagePublisherMutationStatus.Success => Results.NoContent(),
            PackagePublisherMutationStatus.NotFound => Results.NotFound(),
            _ => Results.Conflict(new
            {
                message = "发布任务租约已过期、尝试次数不一致或已由其他代理接管。"
            })
        };
    }

    private static async Task<IResult> HeartbeatAsync(
        PackagePublisherHeartbeatRequest request,
        PackagePublisherTokenValidator tokenValidator,
        PackageImportRepository repository,
        TimeProvider timeProvider,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var authenticationFailure = Authenticate(
            request.AgentId,
            tokenValidator,
            context);
        if (authenticationFailure is not null)
        {
            return authenticationFailure;
        }

        var now = timeProvider.GetUtcNow();
        var errors = PackageImportRules.ValidatePublisherHeartbeat(request, now);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        await repository.RecordPublisherHeartbeatAsync(
            request,
            now,
            cancellationToken);
        return Results.Ok(new PackagePublisherHeartbeatResponse(now));
    }

    private static async Task<IResult> ClaimAsync(
        PackagePublisherClaimRequest request,
        PackagePublisherTokenValidator tokenValidator,
        PackageImportRepository repository,
        TimeProvider timeProvider,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var authenticationFailure = Authenticate(
            request.AgentId,
            tokenValidator,
            context);
        if (authenticationFailure is not null)
        {
            return authenticationFailure;
        }

        if (!PackageImportRules.IsValidPublisherAgentId(request.AgentId))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["agentId"] = ["发布代理 ID 无效。"]
            });
        }

        return Results.Ok(await repository.ClaimPublisherJobAsync(
            request.AgentId,
            timeProvider.GetUtcNow(),
            cancellationToken));
    }

    private static async Task<IResult> DownloadClientArchiveAsync(
        Guid importId,
        PackagePublisherTokenValidator tokenValidator,
        PackageImportRepository repository,
        PackageImportStorage storage,
        TimeProvider timeProvider,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var agentId = context.Request.Headers[AgentHeader].ToString();
        var authenticationFailure = Authenticate(
            agentId,
            tokenValidator,
            context);
        if (authenticationFailure is not null)
        {
            return authenticationFailure;
        }

        if (!await repository.CanOpenPublisherArchiveAsync(
                importId,
                agentId,
                timeProvider.GetUtcNow(),
                cancellationToken))
        {
            return Results.Conflict(new
            {
                message = "发布任务租约已过期或已由其他代理接管。"
            });
        }

        try
        {
            return Results.File(
                storage.OpenClientArchive(importId),
                contentType: "application/zip",
                fileDownloadName: $"{importId:D}-client.zip",
                enableRangeProcessing: true);
        }
        catch (FileNotFoundException)
        {
            return Results.NotFound();
        }
    }

    private static async Task<IResult> CompleteAsync(
        Guid importId,
        PackagePublisherCompletionRequest request,
        PackagePublisherTokenValidator tokenValidator,
        PackagePublisherCompletionService service,
        TimeProvider timeProvider,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var authenticationFailure = Authenticate(
            request.AgentId,
            tokenValidator,
            context);
        if (authenticationFailure is not null)
        {
            return authenticationFailure;
        }

        var errors = PackageImportRules.ValidatePublisherCompletion(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var result = await service.CompleteAsync(
            importId,
            request,
            timeProvider.GetUtcNow(),
            cancellationToken);
        return result.Status switch
        {
            PackagePublisherCompletionStatus.Success => Results.Ok(result.Import),
            PackagePublisherCompletionStatus.NotFound => Results.NotFound(),
            _ => Results.Conflict(new
            {
                message = "发布任务租约已过期、尝试次数不一致或已由其他代理接管。"
            })
        };
    }

    private static IResult? Authenticate(
        string agentId,
        PackagePublisherTokenValidator tokenValidator,
        HttpContext context)
    {
        if (!tokenValidator.IsConfigured)
        {
            return Results.Problem(
                title: "整合包发布代理尚未配置",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var headerAgentId = context.Request.Headers[AgentHeader].ToString();
        var suppliedToken = context.Request.Headers[TokenHeader].ToString();
        return PackageImportRules.IsValidPublisherAgentId(agentId) &&
               string.Equals(agentId, headerAgentId, StringComparison.Ordinal) &&
               tokenValidator.IsValid(suppliedToken)
            ? null
            : Results.Problem(
                title: "整合包发布代理凭据无效",
                statusCode: StatusCodes.Status401Unauthorized);
    }
}

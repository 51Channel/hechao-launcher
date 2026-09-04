using System.Net;
using Hechao.Api.Admin;
using Hechao.Api.Authentication;
using Hechao.Contracts;
using Microsoft.Extensions.Options;

namespace Hechao.Api.ActivityPlans;

public static class ActivityPlanEndpoints
{
    private const string WebsiteTokenHeader =
        "X-Hechao-Website-Activity-Token";

    public static void MapAdminActivityPlans(this RouteGroupBuilder adminApi)
    {
        var plans = adminApi.MapGroup("/activity-plans");
        plans.MapGet(string.Empty, GetAdminOverviewAsync);
        plans.MapPost(string.Empty, CreateAdminAsync)
            .AddEndpointFilter<AdminAntiforgeryFilter>();
        plans.MapPut("/{planId}", UpdateAdminAsync)
            .AddEndpointFilter<AdminAntiforgeryFilter>();
        plans.MapPost("/{planId}/publish", PublishAdminAsync)
            .AddEndpointFilter<AdminAntiforgeryFilter>();
        plans.MapPost("/{planId}/withdraw", WithdrawAdminAsync)
            .AddEndpointFilter<AdminAntiforgeryFilter>();
        plans.MapPost("/{planId}/archive", ArchiveAdminAsync)
            .AddEndpointFilter<AdminAntiforgeryFilter>();
        plans.MapPost("/{planId}/restore", RestoreAdminAsync)
            .AddEndpointFilter<AdminAntiforgeryFilter>();
        plans.MapPost("/{planId}/deploy", DeployAdminAsync)
            .AddEndpointFilter<AdminAntiforgeryFilter>();
    }

    public static void MapWebsiteActivityPlans(
        this IEndpointRouteBuilder endpoints)
    {
        var plans = endpoints
            .MapGroup("/v1/internal/website/activity-plans")
            .RequireRateLimiting("internal-website");
        plans.MapGet(string.Empty, GetWebsiteOverviewAsync);
        plans.MapPost(string.Empty, CreateWebsiteAsync);
        plans.MapPut("/{planId}", UpdateWebsiteAsync);
        plans.MapPost("/{planId}/publish", PublishWebsiteAsync);
        plans.MapPost("/{planId}/withdraw", WithdrawWebsiteAsync);
        plans.MapPost("/{planId}/archive", ArchiveWebsiteAsync);
        plans.MapPost("/{planId}/restore", RestoreWebsiteAsync);
        plans.MapPost("/{planId}/deploy", DeployWebsiteAsync);
    }

    private static async Task<IResult> GetAdminOverviewAsync(
        ActivityPlanRepository repository,
        TimeProvider timeProvider,
        CancellationToken cancellationToken) =>
        Results.Ok(await repository.GetOverviewAsync(
            timeProvider.GetUtcNow(),
            cancellationToken));

    private static async Task<IResult> CreateAdminAsync(
        AdminActivityPlanCreateRequest request,
        ActivityPlanRepository repository,
        TimeProvider timeProvider,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var actor = context.User.GetPlayer();
        return actor?.AccessTier == AccessTier.Administrator
            ? await CreateAsync(
                request,
                repository,
                actor.UserId,
                context.Connection.RemoteIpAddress,
                timeProvider.GetUtcNow(),
                cancellationToken)
            : Results.Forbid();
    }

    private static async Task<IResult> UpdateAdminAsync(
        string planId,
        AdminActivityPlanUpdateRequest request,
        ActivityPlanRepository repository,
        TimeProvider timeProvider,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var actor = context.User.GetPlayer();
        return actor?.AccessTier == AccessTier.Administrator
            ? await UpdateAsync(
                planId,
                request,
                repository,
                actor.UserId,
                context.Connection.RemoteIpAddress,
                timeProvider.GetUtcNow(),
                cancellationToken)
            : Results.Forbid();
    }

    private static Task<IResult> PublishAdminAsync(
        string planId,
        AdminActivityPlanRevisionRequest request,
        ActivityPlanRepository repository,
        TimeProvider timeProvider,
        HttpContext context,
        CancellationToken cancellationToken) =>
        ChangeStatusAdminAsync(
            planId,
            request,
            repository.PublishAsync,
            timeProvider,
            context,
            cancellationToken);

    private static Task<IResult> WithdrawAdminAsync(
        string planId,
        AdminActivityPlanRevisionRequest request,
        ActivityPlanRepository repository,
        TimeProvider timeProvider,
        HttpContext context,
        CancellationToken cancellationToken) =>
        ChangeStatusAdminAsync(
            planId,
            request,
            repository.WithdrawAsync,
            timeProvider,
            context,
            cancellationToken);

    private static async Task<IResult> ArchiveAdminAsync(
        string planId,
        AdminActivityPlanArchiveRequest request,
        ActivityPlanRepository repository,
        TimeProvider timeProvider,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var actor = context.User.GetPlayer();
        return actor?.AccessTier == AccessTier.Administrator
            ? await ArchiveAsync(
                planId,
                request,
                repository,
                actor.UserId,
                context.Connection.RemoteIpAddress,
                timeProvider.GetUtcNow(),
                cancellationToken)
            : Results.Forbid();
    }

    private static Task<IResult> RestoreAdminAsync(
        string planId,
        AdminActivityPlanRevisionRequest request,
        ActivityPlanRepository repository,
        TimeProvider timeProvider,
        HttpContext context,
        CancellationToken cancellationToken) =>
        ChangeStatusAdminAsync(
            planId,
            request,
            repository.RestoreAsync,
            timeProvider,
            context,
            cancellationToken);

    private static async Task<IResult> DeployAdminAsync(
        string planId,
        AdminActivityPlanDeployRequest request,
        ActivityPlanRepository repository,
        TimeProvider timeProvider,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var actor = context.User.GetPlayer();
        return actor?.AccessTier == AccessTier.Administrator
            ? await DeployAsync(
                planId,
                request,
                repository,
                actor.UserId,
                context.Connection.RemoteIpAddress,
                timeProvider.GetUtcNow(),
                cancellationToken)
            : Results.Forbid();
    }

    private static async Task<IResult> GetWebsiteOverviewAsync(
        ActivityPlanRepository repository,
        WebsiteActivityBridgeTokenValidator tokenValidator,
        TimeProvider timeProvider,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var failure = ValidateWebsiteRequest(context, tokenValidator);
        return failure ?? Results.Ok(await repository.GetOverviewAsync(
            timeProvider.GetUtcNow(),
            cancellationToken));
    }

    private static async Task<IResult> CreateWebsiteAsync(
        AdminActivityPlanCreateRequest request,
        ActivityPlanRepository repository,
        WebsiteActivityBridgeTokenValidator tokenValidator,
        IOptions<WebsiteActivityBridgeOptions> options,
        TimeProvider timeProvider,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var failure = ValidateWebsiteRequest(context, tokenValidator);
        return failure ?? await CreateAsync(
            request,
            repository,
            options.Value.ActorUserId,
            context.Connection.RemoteIpAddress,
            timeProvider.GetUtcNow(),
            cancellationToken);
    }

    private static async Task<IResult> UpdateWebsiteAsync(
        string planId,
        AdminActivityPlanUpdateRequest request,
        ActivityPlanRepository repository,
        WebsiteActivityBridgeTokenValidator tokenValidator,
        IOptions<WebsiteActivityBridgeOptions> options,
        TimeProvider timeProvider,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var failure = ValidateWebsiteRequest(context, tokenValidator);
        return failure ?? await UpdateAsync(
            planId,
            request,
            repository,
            options.Value.ActorUserId,
            context.Connection.RemoteIpAddress,
            timeProvider.GetUtcNow(),
            cancellationToken);
    }

    private static Task<IResult> PublishWebsiteAsync(
        string planId,
        AdminActivityPlanRevisionRequest request,
        ActivityPlanRepository repository,
        WebsiteActivityBridgeTokenValidator tokenValidator,
        IOptions<WebsiteActivityBridgeOptions> options,
        TimeProvider timeProvider,
        HttpContext context,
        CancellationToken cancellationToken) =>
        ChangeStatusWebsiteAsync(
            planId,
            request,
            repository.PublishAsync,
            tokenValidator,
            options,
            timeProvider,
            context,
            cancellationToken);

    private static Task<IResult> WithdrawWebsiteAsync(
        string planId,
        AdminActivityPlanRevisionRequest request,
        ActivityPlanRepository repository,
        WebsiteActivityBridgeTokenValidator tokenValidator,
        IOptions<WebsiteActivityBridgeOptions> options,
        TimeProvider timeProvider,
        HttpContext context,
        CancellationToken cancellationToken) =>
        ChangeStatusWebsiteAsync(
            planId,
            request,
            repository.WithdrawAsync,
            tokenValidator,
            options,
            timeProvider,
            context,
            cancellationToken);

    private static async Task<IResult> ArchiveWebsiteAsync(
        string planId,
        AdminActivityPlanArchiveRequest request,
        ActivityPlanRepository repository,
        WebsiteActivityBridgeTokenValidator tokenValidator,
        IOptions<WebsiteActivityBridgeOptions> options,
        TimeProvider timeProvider,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var failure = ValidateWebsiteRequest(context, tokenValidator);
        return failure ?? await ArchiveAsync(
            planId,
            request,
            repository,
            options.Value.ActorUserId,
            context.Connection.RemoteIpAddress,
            timeProvider.GetUtcNow(),
            cancellationToken);
    }

    private static Task<IResult> RestoreWebsiteAsync(
        string planId,
        AdminActivityPlanRevisionRequest request,
        ActivityPlanRepository repository,
        WebsiteActivityBridgeTokenValidator tokenValidator,
        IOptions<WebsiteActivityBridgeOptions> options,
        TimeProvider timeProvider,
        HttpContext context,
        CancellationToken cancellationToken) =>
        ChangeStatusWebsiteAsync(
            planId,
            request,
            repository.RestoreAsync,
            tokenValidator,
            options,
            timeProvider,
            context,
            cancellationToken);

    private static async Task<IResult> DeployWebsiteAsync(
        string planId,
        AdminActivityPlanDeployRequest request,
        ActivityPlanRepository repository,
        WebsiteActivityBridgeTokenValidator tokenValidator,
        IOptions<WebsiteActivityBridgeOptions> options,
        TimeProvider timeProvider,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var failure = ValidateWebsiteRequest(context, tokenValidator);
        return failure ?? await DeployAsync(
            planId,
            request,
            repository,
            options.Value.ActorUserId,
            context.Connection.RemoteIpAddress,
            timeProvider.GetUtcNow(),
            cancellationToken);
    }

    private static async Task<IResult> CreateAsync(
        AdminActivityPlanCreateRequest request,
        ActivityPlanRepository repository,
        Guid actorUserId,
        IPAddress? sourceIp,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var errors = ActivityPlanRules.Validate(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var result = await repository.CreateAsync(
            request,
            actorUserId,
            sourceIp,
            now,
            cancellationToken);
        return result.Status == ActivityPlanMutationStatus.Success
            ? Results.Created(
                $"/v1/admin/activity-plans/{result.Plan!.Id}",
                result.Plan)
            : MutationFailure(result);
    }

    private static async Task<IResult> UpdateAsync(
        string planId,
        AdminActivityPlanUpdateRequest request,
        ActivityPlanRepository repository,
        Guid actorUserId,
        IPAddress? sourceIp,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var errors = ActivityPlanRules.Validate(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        return MutationResult(await repository.UpdateAsync(
            planId,
            request,
            actorUserId,
            sourceIp,
            now,
            cancellationToken));
    }

    private static async Task<IResult> ChangeStatusAdminAsync(
        string planId,
        AdminActivityPlanRevisionRequest request,
        Func<string, long, Guid, IPAddress?, DateTimeOffset,
            CancellationToken, Task<ActivityPlanMutationResult>> mutation,
        TimeProvider timeProvider,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var actor = context.User.GetPlayer();
        return actor?.AccessTier == AccessTier.Administrator
            ? await ChangeStatusAsync(
                planId,
                request,
                mutation,
                actor.UserId,
                context.Connection.RemoteIpAddress,
                timeProvider.GetUtcNow(),
                cancellationToken)
            : Results.Forbid();
    }

    private static async Task<IResult> ChangeStatusWebsiteAsync(
        string planId,
        AdminActivityPlanRevisionRequest request,
        Func<string, long, Guid, IPAddress?, DateTimeOffset,
            CancellationToken, Task<ActivityPlanMutationResult>> mutation,
        WebsiteActivityBridgeTokenValidator tokenValidator,
        IOptions<WebsiteActivityBridgeOptions> options,
        TimeProvider timeProvider,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var failure = ValidateWebsiteRequest(context, tokenValidator);
        return failure ?? await ChangeStatusAsync(
            planId,
            request,
            mutation,
            options.Value.ActorUserId,
            context.Connection.RemoteIpAddress,
            timeProvider.GetUtcNow(),
            cancellationToken);
    }

    private static async Task<IResult> ChangeStatusAsync(
        string planId,
        AdminActivityPlanRevisionRequest request,
        Func<string, long, Guid, IPAddress?, DateTimeOffset,
            CancellationToken, Task<ActivityPlanMutationResult>> mutation,
        Guid actorUserId,
        IPAddress? sourceIp,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var errors = ActivityPlanRules.Validate(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        return MutationResult(await mutation(
            planId,
            request.ExpectedRevision,
            actorUserId,
            sourceIp,
            now,
            cancellationToken));
    }

    private static async Task<IResult> ArchiveAsync(
        string planId,
        AdminActivityPlanArchiveRequest request,
        ActivityPlanRepository repository,
        Guid actorUserId,
        IPAddress? sourceIp,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var errors = ActivityPlanRules.Validate(planId, request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        return MutationResult(await repository.ArchiveAsync(
            planId,
            request.ExpectedRevision,
            request.Reason,
            actorUserId,
            sourceIp,
            now,
            cancellationToken));
    }

    private static async Task<IResult> DeployAsync(
        string planId,
        AdminActivityPlanDeployRequest request,
        ActivityPlanRepository repository,
        Guid actorUserId,
        IPAddress? sourceIp,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var errors = ActivityPlanRules.Validate(planId, request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var result = await repository.DeployAsync(
            planId,
            request,
            actorUserId,
            sourceIp,
            now,
            cancellationToken);
        return result.Status == ActivityPlanMutationStatus.Success
            ? Results.Accepted(
                $"/v1/admin/server-control/operations/" +
                $"{result.Queue!.Operation.OperationId:D}",
                result.Queue)
            : DeploymentFailure(result.Status);
    }

    private static IResult MutationResult(ActivityPlanMutationResult result) =>
        result.Status == ActivityPlanMutationStatus.Success
            ? Results.Ok(result.Plan)
            : MutationFailure(result);

    private static IResult MutationFailure(ActivityPlanMutationResult result) =>
        result.Status switch
        {
            ActivityPlanMutationStatus.NotFound => Results.NotFound(),
            ActivityPlanMutationStatus.PackageNotFound =>
                Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["packageImportId"] = ["所选整合包不存在、尚未完成或不含服务端制品。"]
                }),
            ActivityPlanMutationStatus.PackageBindingRequired =>
                Results.Conflict(new
                {
                    code = "package_binding_required",
                    message = "企划尚未绑定客户端整合包。"
                }),
            ActivityPlanMutationStatus.ScheduleConflict => Results.Conflict(new
            {
                code = "schedule_conflict",
                message = "已发布企划的开放时间不能重叠；同一时间只允许一个活动。",
                conflict = result.Conflict
            }),
            ActivityPlanMutationStatus.RevisionConflict => Results.Conflict(new
            {
                code = "revision_conflict",
                message = "企划已被其他管理员修改，请刷新后重试。"
            }),
            ActivityPlanMutationStatus.InvalidState => Results.Conflict(new
            {
                code = "invalid_state",
                message = "企划当前状态不允许执行此操作。"
            }),
            ActivityPlanMutationStatus.PackageProfileArchived =>
                Results.Conflict(new
                {
                    code = "package_profile_archived",
                    message = "整合包对应的客户端档案已归档。"
                }),
            ActivityPlanMutationStatus.PackageNotProductionReady =>
                Results.Conflict(new
                {
                    code = "package_not_production_ready",
                    message = "整合包客户端尚未发布到 production 通道。"
                }),
            _ => Results.Conflict(new
            {
                code = "activity_plan_conflict",
                message = "企划状态已变化，请刷新后重试。"
            })
        };

    private static IResult DeploymentFailure(ActivityPlanMutationStatus status) =>
        status switch
        {
            ActivityPlanMutationStatus.NotFound => Results.NotFound(),
            ActivityPlanMutationStatus.RevisionConflict => Results.Conflict(new
            {
                code = "revision_conflict",
                message = "企划已被其他管理员修改，请刷新后重试。"
            }),
            ActivityPlanMutationStatus.InvalidState => Results.Conflict(new
            {
                code = "invalid_state",
                message = "已归档企划不能部署。"
            }),
            ActivityPlanMutationStatus.PackageNotFound => Results.Conflict(new
            {
                code = "package_not_found",
                message = "企划绑定的整合包不可用。"
            }),
            ActivityPlanMutationStatus.PackageBindingRequired => Results.Conflict(new
            {
                code = "package_binding_required",
                message = "企划尚未绑定客户端整合包。"
            }),
            ActivityPlanMutationStatus.PackageProfileArchived =>
                Results.Conflict(new
                {
                    code = "package_profile_archived",
                    message = "整合包对应的客户端档案已归档。"
                }),
            ActivityPlanMutationStatus.DeploymentArtifactMissing =>
                Results.Conflict(new
                {
                    code = "deployment_artifact_missing",
                    message = "服务端制品已不在暂存区，无法部署。"
                }),
            ActivityPlanMutationStatus.DeploymentTargetUnavailable =>
                Results.Conflict(new
                {
                    code = "deployment_target_unavailable",
                    message = "owl5 活动槽或服控代理当前不可用。"
                }),
            ActivityPlanMutationStatus.DeploymentTargetOnline =>
                Results.Conflict(new
                {
                    code = "deployment_target_online",
                    message = "活动服仍在运行，请先停止后再部署。"
                }),
            ActivityPlanMutationStatus.DeploymentOperationInProgress =>
                Results.Conflict(new
                {
                    code = "deployment_in_progress",
                    message = "活动槽已有进行中的服控操作。"
                }),
            _ => Results.Conflict(new
            {
                code = "activity_deployment_conflict",
                message = "部署状态已变化，请刷新后重试。"
            })
        };

    private static IResult? ValidateWebsiteRequest(
        HttpContext context,
        WebsiteActivityBridgeTokenValidator tokenValidator)
    {
        if (context.Connection.RemoteIpAddress is not { } remoteAddress ||
            !IPAddress.IsLoopback(remoteAddress))
        {
            return Results.NotFound();
        }

        if (!tokenValidator.IsConfigured)
        {
            return Results.Problem(
                title: "官网活动同步尚未配置",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var token = context.Request.Headers[WebsiteTokenHeader].ToString();
        return tokenValidator.IsValid(token)
            ? null
            : Results.Problem(
                title: "官网活动同步凭据无效",
                statusCode: StatusCodes.Status401Unauthorized);
    }
}

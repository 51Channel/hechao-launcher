using Hechao.Api.Admin;
using Hechao.Api.Authentication;
using Hechao.Contracts;
using Npgsql;

namespace Hechao.Api.ServerControl;

public static class DeploymentSlotEndpoints
{
    public static void MapAdminDeploymentSlots(
        this RouteGroupBuilder adminApi)
    {
        adminApi.MapPost(
                "/server-control/deployment-slots",
                CreateAsync)
            .AddEndpointFilter<AdminAntiforgeryFilter>();
    }

    private static async Task<IResult> CreateAsync(
        AdminCreateDeploymentSlotRequest request,
        DeploymentSlotRepository repository,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var errors = DeploymentSlotRules.Validate(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var actor = context.User.GetPlayer();
        if (actor?.AccessTier != AccessTier.Administrator)
        {
            return Results.Forbid();
        }

        DeploymentSlotCreateResult result;
        try
        {
            result = await repository.CreateAsync(
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
                message = "部署槽状态刚刚发生变化，请刷新后重试。"
            });
        }
        catch (PostgresException exception) when (
            exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return Results.Conflict(new
            {
                message = "该槽 ID 已被服务器目录或服控目标占用。"
            });
        }

        return result.Status switch
        {
            DeploymentSlotCreateStatus.Success => Results.Accepted(
                $"/v1/admin/server-control/operations/" +
                result.Result!.Operation.OperationId.ToString("D"),
                result.Result),
            DeploymentSlotCreateStatus.FeatureDisabled => Results.Problem(
                title: "服务器控制功能尚未启用",
                statusCode: StatusCodes.Status503ServiceUnavailable),
            DeploymentSlotCreateStatus.AlreadyExists => Results.Conflict(new
            {
                message = "该槽 ID 已被服务器目录或服控目标占用。"
            }),
            DeploymentSlotCreateStatus.TemplateNotFound => Results.NotFound(),
            DeploymentSlotCreateStatus.TemplateUnavailable => Results.Conflict(new
            {
                message = "模板槽未启用部署能力，或其 VPS 代理当前离线。"
            }),
            DeploymentSlotCreateStatus.LimitReached => Results.Conflict(new
            {
                message = "该 VPS 的动态部署槽已达到安全上限。"
            }),
            _ => Results.Problem(
                title: "部署槽创建排队失败",
                statusCode: StatusCodes.Status500InternalServerError)
        };
    }
}

using Hechao.Api.PackageImports;
using Microsoft.Extensions.Options;

namespace Hechao.Api.ServerControl;

public static class ServerControlPackageEndpoints
{
    private const string AgentHeader = "X-Hechao-Server-Control-Agent";
    private const string TokenHeader = "X-Hechao-Server-Control-Token";

    public static void MapServerControlPackageArchives(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/v1/internal/server-control/commands/{commandId:guid}/package-archive",
                DownloadAsync)
            .RequireRateLimiting("internal-server-control");
    }

    private static async Task<IResult> DownloadAsync(
        Guid commandId,
        ServerControlTokenValidator tokenValidator,
        ServerControlRepository repository,
        PackageImportStorage storage,
        IOptions<PackageImportOptions> packageOptions,
        TimeProvider timeProvider,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (!packageOptions.Value.Enabled)
        {
            return Results.Problem(
                title: "整合包导入未启用",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (!tokenValidator.IsConfigured)
        {
            return Results.Problem(
                title: "服务器控制代理尚未配置",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var agentId = context.Request.Headers[AgentHeader].ToString();
        var token = context.Request.Headers[TokenHeader].ToString();
        if (!ServerControlRules.IsValidAgentId(agentId) ||
            !tokenValidator.IsValid(agentId, token))
        {
            return Results.Problem(
                title: "服务器控制代理凭据无效",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var importId = await repository.GetAuthorizedPackageArchiveImportIdAsync(
            commandId,
            agentId,
            timeProvider.GetUtcNow(),
            cancellationToken);
        if (importId is null)
        {
            return Results.Conflict(new
            {
                message = "部署命令租约已过期、任务不属于该代理或导入状态已变化。"
            });
        }

        try
        {
            return Results.File(
                storage.OpenServerArchive(importId.Value),
                contentType: "application/zip",
                fileDownloadName: $"{importId:D}-server.zip",
                enableRangeProcessing: true);
        }
        catch (FileNotFoundException)
        {
            return Results.NotFound();
        }
    }
}

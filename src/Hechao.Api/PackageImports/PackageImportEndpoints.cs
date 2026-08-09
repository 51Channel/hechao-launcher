using System.Globalization;
using Hechao.Api.Admin;
using Hechao.Api.Authentication;
using Hechao.Api.ServerControl;
using Hechao.Contracts;
using Microsoft.Extensions.Options;

namespace Hechao.Api.PackageImports;

public static class PackageImportEndpoints
{
    public static void MapAdminPackageImports(this RouteGroupBuilder adminApi)
    {
        var imports = adminApi.MapGroup("/package-imports");
        imports.MapGet(string.Empty, GetRecentAsync);
        imports.MapGet("/{importId:guid}", GetAsync);
        imports.MapPost("/uploads", CreateUploadAsync)
            .AddEndpointFilter<AdminAntiforgeryFilter>();
        imports.MapMethods(
                "/{importId:guid}/content",
                [HttpMethods.Patch],
                AppendUploadAsync)
            .AddEndpointFilter<AdminAntiforgeryFilter>();
        imports.MapPost("/{importId:guid}/complete", CompleteUploadAsync)
            .AddEndpointFilter<AdminAntiforgeryFilter>();
        imports.MapPost("/{importId:guid}/confirm", ConfirmAsync)
            .AddEndpointFilter<AdminAntiforgeryFilter>();
        imports.MapPost("/{importId:guid}/cancel", CancelAsync)
            .AddEndpointFilter<AdminAntiforgeryFilter>();
    }

    private static async Task<IResult> GetRecentAsync(
        PackageImportRepository repository,
        IOptions<PackageImportOptions> options,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
        {
            return Disabled();
        }

        var imports = await repository.GetRecentAsync(50, cancellationToken);
        var publisher = await repository.GetPublisherAgentStateAsync(
            timeProvider.GetUtcNow(),
            cancellationToken);
        return Results.Ok(new AdminPackageImportListResponse(
            imports,
            publisher.Connected,
            publisher.LastSeenAt));
    }

    private static async Task<IResult> GetAsync(
        Guid importId,
        PackageImportRepository repository,
        IOptions<PackageImportOptions> options,
        CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
        {
            return Disabled();
        }

        var import = await repository.GetAsync(importId, cancellationToken);
        return import is null ? Results.NotFound() : Results.Ok(import);
    }

    private static async Task<IResult> CreateUploadAsync(
        AdminPackageUploadCreateRequest request,
        PackageImportRepository repository,
        PackageImportStorage storage,
        IOptions<PackageImportOptions> options,
        TimeProvider timeProvider,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
        {
            return Disabled();
        }

        var errors = PackageImportRules.Validate(request, options.Value);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var actor = context.User.GetPlayer();
        if (actor?.AccessTier != AccessTier.Administrator)
        {
            return Results.Forbid();
        }

        var importId = Guid.NewGuid();
        storage.Initialize(importId);
        try
        {
            var created = await repository.CreateAsync(
                importId,
                request with { FileName = request.FileName.Trim() },
                actor.UserId,
                context.Connection.RemoteIpAddress,
                timeProvider.GetUtcNow(),
                cancellationToken);
            return Results.Created(
                $"/v1/admin/package-imports/{importId:D}",
                created);
        }
        catch
        {
            storage.Delete(importId);
            throw;
        }
    }

    private static async Task<IResult> AppendUploadAsync(
        Guid importId,
        PackageImportRepository repository,
        PackageImportStorage storage,
        IOptions<PackageImportOptions> options,
        TimeProvider timeProvider,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
        {
            return Disabled();
        }

        var import = await repository.GetAsync(importId, cancellationToken);
        if (import is null)
        {
            return Results.NotFound();
        }

        if (import.Status != PackageImportStatus.Uploading)
        {
            return Results.Conflict(new
            {
                message = "此导入任务已经不能继续上传。",
                status = import.Status.ToString()
            });
        }

        if (!TryReadOffset(context.Request, out var requestedOffset))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["uploadOffset"] = ["请求必须包含有效的 Upload-Offset 标头。"]
            });
        }

        if (context.Request.ContentLength is <= 0 ||
            context.Request.ContentLength > options.Value.UploadChunkBytes)
        {
            return Results.Problem(
                title: "上传分块大小无效",
                detail: $"单个分块必须小于等于 {options.Value.UploadChunkBytes} 字节。",
                statusCode: StatusCodes.Status413PayloadTooLarge);
        }

        try
        {
            var uploadedBytes = await storage.AppendAsync(
                importId,
                requestedOffset,
                import.ExpectedUploadBytes,
                context.Request.Body,
                cancellationToken);
            var result = await repository.UpdateUploadedBytesAsync(
                importId,
                uploadedBytes,
                timeProvider.GetUtcNow(),
                cancellationToken);
            if (result.Status != PackageImportMutationStatus.Success)
            {
                return Results.Conflict(new
                {
                    message = "上传任务状态已变化，请刷新后重试。",
                    uploadedBytes
                });
            }

            return Results.Ok(new AdminPackageUploadAppendResponse(
                importId,
                uploadedBytes,
                import.ExpectedUploadBytes,
                uploadedBytes == import.ExpectedUploadBytes));
        }
        catch (PackageUploadOffsetException exception)
        {
            context.Response.Headers["Upload-Offset"] =
                exception.ActualOffset.ToString(CultureInfo.InvariantCulture);
            return Results.Conflict(new
            {
                message = "上传偏移已变化，请从服务端返回的位置继续。",
                uploadedBytes = exception.ActualOffset
            });
        }
        catch (InvalidDataException exception)
        {
            return Results.Problem(
                title: "上传分块被拒绝",
                detail: exception.Message,
                statusCode: StatusCodes.Status413PayloadTooLarge);
        }
    }

    private static async Task<IResult> CompleteUploadAsync(
        Guid importId,
        PackageImportRepository repository,
        PackageImportStorage storage,
        IOptions<PackageImportOptions> options,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
        {
            return Disabled();
        }

        var import = await repository.GetAsync(importId, cancellationToken);
        if (import is null)
        {
            return Results.NotFound();
        }

        if (import.Status != PackageImportStatus.Uploading)
        {
            return Results.Conflict(new { message = "上传任务已经完成或终止。" });
        }

        var actualBytes = storage.GetUploadedBytes(importId);
        if (actualBytes != import.UploadedBytes)
        {
            await repository.UpdateUploadedBytesAsync(
                importId,
                actualBytes,
                timeProvider.GetUtcNow(),
                cancellationToken);
        }

        try
        {
            var completed = await storage.CompleteUploadAsync(
                importId,
                import.ExpectedUploadBytes,
                cancellationToken);
            var result = await repository.MarkUploadedAsync(
                importId,
                completed.Sha256,
                timeProvider.GetUtcNow(),
                cancellationToken);
            return result.Status == PackageImportMutationStatus.Success
                ? Results.Ok(result.Import)
                : Results.Conflict(new { message = "上传任务状态已变化，请刷新。" });
        }
        catch (InvalidDataException exception)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["archive"] = [exception.Message]
            });
        }
    }

    private static async Task<IResult> ConfirmAsync(
        Guid importId,
        AdminPackageImportConfirmRequest request,
        PackageImportRepository repository,
        ServerControlRepository serverControlRepository,
        IOptions<PackageImportOptions> options,
        TimeProvider timeProvider,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
        {
            return Disabled();
        }

        var import = await repository.GetAsync(importId, cancellationToken);
        if (import is null)
        {
            return Results.NotFound();
        }

        var errors = new Dictionary<string, string[]>(
            PackageImportRules.Validate(request, import));
        var now = timeProvider.GetUtcNow();
        var publisher = await repository.GetPublisherAgentStateAsync(
            now,
            cancellationToken);
        if (!publisher.Connected)
        {
            errors["publisher"] =
                ["客户端发布代理当前不在线。"];
        }

        if (request.DeployServer)
        {
            var target = await serverControlRepository.GetTargetDetailAsync(
                request.TargetServerId,
                now,
                cancellationToken);
            if (target is null || !PackageImportRules.IsActivityTarget(target.Target))
            {
                errors["targetServerId"] =
                    ["只能部署到已启用整合包能力的 owl5 活动目标。"];
            }
            else
            {
                if (!target.Target.AgentConnected)
                {
                    errors["targetServerId"] = ["目标 VPS 服控代理当前不在线。"];
                }

                if (target.Target.Online)
                {
                    errors["targetServerId"] =
                        ["目标服务端仍在运行，请先从服控面板停止。"];
                }

                if (target.Target.ActiveOperation is not null)
                {
                    errors["targetServerId"] =
                        ["目标服务端存在进行中的服控操作，请等待完成。"];
                }
            }
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var actor = context.User.GetPlayer();
        if (actor?.AccessTier != AccessTier.Administrator)
        {
            return Results.Forbid();
        }

        var result = await repository.ConfirmAsync(
            importId,
            request,
            actor.UserId,
            context.Connection.RemoteIpAddress,
            now,
            cancellationToken);
        return result.Status == PackageImportMutationStatus.Success
            ? Results.Ok(result.Import)
            : Results.Conflict(new { message = "导入任务已变化，请刷新后重新确认。" });
    }

    private static async Task<IResult> CancelAsync(
        Guid importId,
        AdminPackageImportCancelRequest request,
        PackageImportRepository repository,
        PackageImportStorage storage,
        IOptions<PackageImportOptions> options,
        TimeProvider timeProvider,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
        {
            return Disabled();
        }

        var reason = request.Reason?.Trim() ?? string.Empty;
        if (request.ExpectedRevision <= 0 ||
            reason.Length is < 4 or > 500 ||
            reason.Any(char.IsControl))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["reason"] = ["取消原因必须为 4 至 500 个可显示字符。"]
            });
        }

        var actor = context.User.GetPlayer();
        if (actor?.AccessTier != AccessTier.Administrator)
        {
            return Results.Forbid();
        }

        var result = await repository.CancelAsync(
            importId,
            request with { Reason = reason },
            actor.UserId,
            context.Connection.RemoteIpAddress,
            timeProvider.GetUtcNow(),
            cancellationToken);
        if (result.Status != PackageImportMutationStatus.Success)
        {
            return Results.Conflict(new
            {
                message = "任务已进入不可取消阶段，或修订号已经变化。"
            });
        }

        try
        {
            storage.Delete(importId);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // The database state is authoritative; retention cleanup retries files later.
        }

        return Results.Ok(result.Import);
    }

    private static bool TryReadOffset(HttpRequest request, out long offset) =>
        long.TryParse(
            request.Headers["Upload-Offset"].ToString(),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out offset) && offset >= 0;

    private static IResult Disabled() =>
        Results.Problem(
            title: "整合包导入未启用",
            detail: "服务器尚未配置整合包暂存目录和发布代理。",
            statusCode: StatusCodes.Status503ServiceUnavailable);
}

using System.Net.Mime;
using System.Security.Cryptography;
using System.Text.Json;
using Hechao.Api.Admin;
using Hechao.Api.Authentication;
using Hechao.Contracts;

namespace Hechao.Api.Diagnostics;

public static class DiagnosticUploadEndpoints
{
    public static void MapDiagnosticUploads(
        this IEndpointRouteBuilder endpoints,
        RouteGroupBuilder adminApi)
    {
        endpoints.MapPost("/v1/diagnostics/uploads", CreateUploadAsync)
            .RequireAuthorization()
            .RequireRateLimiting("diagnostics");
        endpoints.MapPut("/v1/diagnostics/uploads/{uploadId:guid}", UploadAsync)
            .RequireRateLimiting("diagnostic-upload");

        adminApi.MapGet("/diagnostics", GetAdminUploadsAsync);
        adminApi.MapGet(
            "/diagnostics/{uploadId:guid}/download",
            DownloadForAdminAsync);
    }

    private static async Task<IResult> CreateUploadAsync(
        DiagnosticUploadCreateRequest request,
        HttpContext context,
        DiagnosticUploadRepository repository,
        DiagnosticUploadOptions options,
        CancellationToken cancellationToken)
    {
        var account = context.User.GetAccount();
        if (account is null)
        {
            return Results.Unauthorized();
        }

        var errors = DiagnosticUploadRules.ValidateCreateRequest(request, options);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var result = await repository.CreateAsync(
            account.UserId,
            request,
            context.Connection.RemoteIpAddress,
            cancellationToken);
        if (result.Authorization is not null)
        {
            return Results.Created(
                $"/v1/diagnostics/uploads/{result.Authorization.UploadId:D}",
                result.Authorization);
        }

        var detail = result.Status switch
        {
            DiagnosticUploadCreateStatus.DailyCountExceeded =>
                "今天创建的诊断上传次数已达上限，请稍后再试。",
            DiagnosticUploadCreateStatus.DailyBytesExceeded =>
                "今天创建的诊断上传总大小已达上限，请稍后再试。",
            DiagnosticUploadCreateStatus.ActiveLimitExceeded =>
                "尚未到期的诊断包已达上限，请联系管理员处理或等待自动删除。",
            _ => "暂时无法创建诊断上传。"
        };
        return Results.Problem(
            title: "诊断上传配额已用完",
            detail: detail,
            statusCode: StatusCodes.Status429TooManyRequests);
    }

    private static async Task<IResult> UploadAsync(
        Guid uploadId,
        HttpContext context,
        DiagnosticUploadRepository repository,
        DiagnosticUploadStorage storage,
        DiagnosticUploadOptions options,
        CancellationToken cancellationToken)
    {
        var token = context.Request.Headers[
            DiagnosticUploadRules.UploadTokenHeaderName].ToString();
        var contentLength = context.Request.ContentLength;
        if (!DiagnosticUploadRules.IsValidUploadToken(token) ||
            contentLength is null or <= 0 ||
            contentLength > options.MaximumBytes ||
            !IsSupportedContentType(context.Request.ContentType))
        {
            return Results.BadRequest(new
            {
                message = "上传凭据、文件大小或内容类型无效。"
            });
        }

        var ticket = await repository.BeginUploadAsync(
            uploadId,
            DiagnosticUploadRules.HashUploadToken(token),
            cancellationToken);
        if (ticket is null)
        {
            return Results.NotFound();
        }

        if (ticket.ExpectedBytes != contentLength.Value)
        {
            await FailAsync(
                repository,
                storage,
                ticket,
                "content-length-mismatch",
                context.Connection.RemoteIpAddress);
            return Results.BadRequest(new { message = "诊断包大小与授权不匹配。" });
        }

        try
        {
            var (actualBytes, actualSha256) = await SaveAndHashAsync(
                context.Request.Body,
                storage,
                ticket.UploadId,
                options.MaximumBytes,
                cancellationToken);
            if (actualBytes != ticket.ExpectedBytes ||
                !string.Equals(
                    actualSha256,
                    ticket.ExpectedSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "The diagnostic archive digest does not match its authorization.");
            }

            await DiagnosticUploadRules.ValidateArchiveAsync(
                storage.GetTemporaryPath(ticket.UploadId),
                ticket.ProfileId,
                cancellationToken);
            storage.Commit(ticket.UploadId);
            var receipt = await repository.CompleteAsync(
                ticket,
                actualBytes,
                actualSha256,
                context.Connection.RemoteIpAddress,
                cancellationToken);
            if (receipt is null)
            {
                storage.Delete(ticket.UploadId);
                return Results.Problem(
                    title: "诊断上传状态冲突",
                    statusCode: StatusCodes.Status409Conflict);
            }

            return Results.Ok(receipt);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await FailAsync(
                repository,
                storage,
                ticket,
                "request-cancelled",
                context.Connection.RemoteIpAddress);
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or
            UnauthorizedAccessException or CryptographicException or JsonException)
        {
            await FailAsync(
                repository,
                storage,
                ticket,
                "archive-validation-failed",
                context.Connection.RemoteIpAddress);
            return Results.BadRequest(new
            {
                message = "诊断包校验失败，请重新生成后再上传。"
            });
        }
    }

    private static async Task<IResult> GetAdminUploadsAsync(
        int? limit,
        DiagnosticUploadRepository repository,
        CancellationToken cancellationToken)
    {
        var pageSize = Math.Clamp(limit ?? 100, 1, 200);
        return Results.Ok(await repository.GetAdminListAsync(
            pageSize,
            cancellationToken));
    }

    private static async Task<IResult> DownloadForAdminAsync(
        Guid uploadId,
        HttpContext context,
        DiagnosticUploadRepository repository,
        DiagnosticUploadStorage storage,
        CancellationToken cancellationToken)
    {
        var actor = AdminWebSessionAuthenticationHandler.GetState(context)?.Player;
        if (actor is null)
        {
            return Results.Unauthorized();
        }

        var upload = await repository.GetForAdminDownloadAsync(
            uploadId,
            cancellationToken);
        if (upload is null || !storage.ArchiveExists(uploadId))
        {
            return Results.NotFound();
        }

        await repository.RecordAdminDownloadAsync(
            uploadId,
            actor.UserId,
            context.Connection.RemoteIpAddress,
            cancellationToken);
        return Results.File(
            storage.OpenRead(uploadId),
            MediaTypeNames.Application.Zip,
            $"Hechao-Diagnostic-{uploadId:N}.zip",
            enableRangeProcessing: false);
    }

    private static async Task<(long Bytes, string Sha256)> SaveAndHashAsync(
        Stream source,
        DiagnosticUploadStorage storage,
        Guid uploadId,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var destination = storage.CreateTemporaryFile(uploadId);
        var buffer = new byte[64 * 1024];
        long total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            total = checked(total + read);
            if (total > maximumBytes)
            {
                throw new InvalidDataException("The diagnostic upload is oversized.");
            }

            hash.AppendData(buffer, 0, read);
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        await destination.FlushAsync(cancellationToken);
        return (total, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
    }

    private static async Task FailAsync(
        DiagnosticUploadRepository repository,
        DiagnosticUploadStorage storage,
        DiagnosticUploadTicket ticket,
        string reason,
        System.Net.IPAddress? sourceIp)
    {
        storage.Delete(ticket.UploadId);
        try
        {
            await repository.MarkFailedAsync(
                ticket,
                reason,
                sourceIp,
                CancellationToken.None);
        }
        catch
        {
            // Preserve the original upload failure; cleanup will expire the row.
        }
    }

    private static bool IsSupportedContentType(string? contentType) =>
        contentType is not null &&
        (contentType.StartsWith("application/zip", StringComparison.OrdinalIgnoreCase) ||
         contentType.StartsWith(
             "application/octet-stream",
             StringComparison.OrdinalIgnoreCase));
}

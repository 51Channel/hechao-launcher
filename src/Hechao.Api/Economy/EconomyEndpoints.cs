using Microsoft.Extensions.Options;

namespace Hechao.Api.Economy;

public static class EconomyEndpoints
{
    public static IEndpointRouteBuilder MapEconomyEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var economy = endpoints.MapGroup("/v1/internal/economy")
            .RequireRateLimiting("internal-economy");
        economy.MapGet("/accounts/{playerUuid:guid}", GetBalanceAsync);
        economy.MapPost("/transfers", TransferAsync);
        economy.MapPost("/sales/quotes", CreateQuoteAsync);
        economy.MapPost("/sales/commit", CommitSaleAsync);
        economy.MapGet("/products", ListProductsAsync);
        economy.MapPut("/products", UpsertProductAsync);
        economy.MapPost("/products/disable", DisableProductAsync);
        economy.MapPut("/products/{itemId}", UpsertProductAsync);
        economy.MapPost("/products/{itemId}/disable", DisableProductAsync);
        return endpoints;
    }

    private static async Task<IResult> GetBalanceAsync(
        Guid playerUuid,
        HttpContext context,
        EconomyServiceTokenValidator tokenValidator,
        EconomyRepository repository,
        CancellationToken cancellationToken)
    {
        var authentication = Authenticate(context, tokenValidator, out _);
        if (authentication is not null)
        {
            return authentication;
        }

        if (playerUuid == Guid.Empty)
        {
            return Validation("playerUuid", "玩家 UUID 无效。");
        }

        return Results.Ok(await repository.GetBalanceAsync(playerUuid, cancellationToken));
    }

    private static async Task<IResult> TransferAsync(
        EconomyTransferRequest request,
        HttpContext context,
        EconomyServiceTokenValidator tokenValidator,
        EconomyRepository repository,
        IOptions<EconomyServiceOptions> options,
        CancellationToken cancellationToken)
    {
        var authentication = Authenticate(context, tokenValidator, out var serverId);
        if (authentication is not null)
        {
            return authentication;
        }

        if (!EconomyRules.IsValidTransfer(request, options.Value.MaximumTransferAmount))
        {
            return Validation("request", "转账参数无效。");
        }

        try
        {
            var response = await repository.TransferAsync(
                serverId!,
                request with { Note = request.Note?.Trim() },
                cancellationToken);
            return response.Status == "Applied"
                ? Results.Ok(response)
                : Results.Json(response, statusCode: StatusCodes.Status409Conflict);
        }
        catch (EconomyIdempotencyConflictException)
        {
            return IdempotencyConflict();
        }
    }

    private static async Task<IResult> CreateQuoteAsync(
        EconomySaleQuoteRequest request,
        HttpContext context,
        EconomyServiceTokenValidator tokenValidator,
        EconomyRepository repository,
        IOptions<EconomyServiceOptions> options,
        CancellationToken cancellationToken)
    {
        var authentication = Authenticate(context, tokenValidator, out var serverId);
        if (authentication is not null)
        {
            return authentication;
        }

        if (!EconomyRules.IsValidQuote(request))
        {
            return Validation("request", "报价参数无效，只允许回收目录中的安全物品。");
        }

        var result = await repository.CreateSaleQuoteAsync(
            serverId!,
            request with { ItemId = request.ItemId.Trim() },
            TimeSpan.FromSeconds(options.Value.QuoteLifetimeSeconds),
            cancellationToken);
        return result.Status switch
        {
            EconomyQuoteStatus.Created => Results.Ok(result.Quote),
            EconomyQuoteStatus.ProductNotFound => Results.NotFound(new
            {
                code = "PRODUCT_NOT_FOUND",
                message = "该物品未加入回收目录。"
            }),
            EconomyQuoteStatus.ProductDisabled => Results.Conflict(new
            {
                code = "PRODUCT_DISABLED",
                message = "该物品的回收已暂停。"
            }),
            EconomyQuoteStatus.PersonalLimitExceeded => Results.Conflict(new
            {
                code = "PERSONAL_LIMIT_EXCEEDED",
                message = "已超过该物品的个人每日回收额度。"
            }),
            _ => Results.Conflict(new
            {
                code = "SERVER_LIMIT_EXCEEDED",
                message = "已超过该物品的全服每日回收额度。"
            })
        };
    }

    private static async Task<IResult> CommitSaleAsync(
        EconomySaleCommitRequest request,
        HttpContext context,
        EconomyServiceTokenValidator tokenValidator,
        EconomyRepository repository,
        CancellationToken cancellationToken)
    {
        var authentication = Authenticate(context, tokenValidator, out var serverId);
        if (authentication is not null)
        {
            return authentication;
        }

        if (!EconomyRules.IsValidCommit(request))
        {
            return Validation("request", "出售确认参数无效。");
        }

        try
        {
            var response = await repository.CommitSaleAsync(
                serverId!,
                request,
                cancellationToken);
            return response.Status == "Applied"
                ? Results.Ok(response)
                : Results.Json(response, statusCode: StatusCodes.Status409Conflict);
        }
        catch (EconomyIdempotencyConflictException)
        {
            return IdempotencyConflict();
        }
    }

    private static async Task<IResult> ListProductsAsync(
        bool includeDisabled,
        HttpContext context,
        EconomyServiceTokenValidator tokenValidator,
        EconomyRepository repository,
        CancellationToken cancellationToken)
    {
        var authentication = Authenticate(context, tokenValidator, out _);
        return authentication ?? Results.Ok(
            await repository.ListProductsAsync(includeDisabled, cancellationToken));
    }

    private static async Task<IResult> UpsertProductAsync(
        string itemId,
        EconomyProductUpsertRequest request,
        HttpContext context,
        EconomyServiceTokenValidator tokenValidator,
        EconomyRepository repository,
        CancellationToken cancellationToken)
    {
        var authentication = Authenticate(context, tokenValidator, out _);
        if (authentication is not null)
        {
            return authentication;
        }

        if (!EconomyRules.IsValidMinecraftItemId(itemId) ||
            !EconomyRules.IsValidProductMutation(request))
        {
            return Validation("request", "商品配置无效，请检查物品 ID、价格和额度。");
        }

        return Results.Ok(await repository.UpsertProductAsync(
            itemId,
            request with { ActorName = request.ActorName.Trim() },
            cancellationToken));
    }

    private static async Task<IResult> DisableProductAsync(
        string itemId,
        EconomyProductDisableRequest request,
        HttpContext context,
        EconomyServiceTokenValidator tokenValidator,
        EconomyRepository repository,
        CancellationToken cancellationToken)
    {
        var authentication = Authenticate(context, tokenValidator, out _);
        if (authentication is not null)
        {
            return authentication;
        }

        if (!EconomyRules.IsValidMinecraftItemId(itemId) ||
            !EconomyRules.IsValidProductDisable(request))
        {
            return Validation("request", "停用商品参数无效。");
        }

        var status = await repository.DisableProductAsync(
            itemId,
            request with { ActorName = request.ActorName.Trim() },
            cancellationToken);
        return status == EconomyProductMutationStatus.Applied
            ? Results.NoContent()
            : Results.NotFound();
    }

    private static IResult? Authenticate(
        HttpContext context,
        EconomyServiceTokenValidator tokenValidator,
        out string? serverId)
    {
        serverId = context.Request.Headers["X-Hechao-Server-Id"].ToString().Trim();
        return tokenValidator.Validate(
            context.Request.Headers.Authorization.ToString(),
            serverId) switch
        {
            EconomyAuthenticationStatus.Allowed => null,
            EconomyAuthenticationStatus.NotConfigured => Results.Problem(
                title: "经济服务尚未配置",
                statusCode: StatusCodes.Status503ServiceUnavailable),
            EconomyAuthenticationStatus.ServerNotAllowed => Results.Forbid(),
            _ => Results.Unauthorized()
        };
    }

    private static IResult Validation(string key, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]>
        {
            [key] = [message]
        });

    private static IResult IdempotencyConflict() => Results.Conflict(new
    {
        code = "IDEMPOTENCY_CONFLICT",
        message = "幂等键已被另一笔请求使用。"
    });
}

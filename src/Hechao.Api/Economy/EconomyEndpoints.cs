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
        economy.MapGet("/shop/products", ListShopProductsAsync);
        economy.MapPost("/shop/purchases", PurchaseShopProductAsync);
        economy.MapGet("/shop/deliveries/{playerUuid:guid}", ListShopDeliveriesAsync);
        economy.MapPost("/shop/deliveries/claim", ClaimShopDeliveryAsync);
        economy.MapPut("/products", UpsertProductAsync);
        economy.MapPost("/products/disable", DisableProductAsync);
        // Keep item IDs in the query string for clients that may contain a slash in the path.
        economy.MapPut("/products/shop", UpsertShopProductAsync);
        economy.MapPost("/products/shop/disable", DisableShopProductAsync);
        economy.MapPut("/products/{itemId}", UpsertProductAsync);
        economy.MapPost("/products/{itemId}/disable", DisableProductAsync);
        economy.MapPut("/products/{itemId}/shop", UpsertShopProductAsync);
        economy.MapPost("/products/{itemId}/shop/disable", DisableShopProductAsync);
        economy.MapGet("/market/listings", ListMarketListingsAsync);
        economy.MapGet("/market/listings/mine/{playerUuid:guid}", ListOwnMarketListingsAsync);
        economy.MapPost("/market/listings", CreateMarketListingAsync);
        economy.MapPost("/market/purchases", PurchaseMarketListingAsync);
        economy.MapPost("/market/cancellations", CancelMarketListingAsync);
        economy.MapGet("/market/deliveries/{playerUuid:guid}", ListMarketDeliveriesAsync);
        economy.MapPost("/market/deliveries/claim", ClaimMarketDeliveryAsync);
        return endpoints;
    }

    private static async Task<IResult> ListMarketListingsAsync(
        string? query,
        int? limit,
        string? sort,
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

        if (!EconomyRules.IsValidMarketQuery(query) || limit is < 1 or > 500)
        {
            return Validation("query", "市场搜索参数无效。");
        }

        if (!EconomyRules.TryParseMarketSort(sort, out var marketSort))
        {
            return Validation("sort", "市场排序参数无效。");
        }

        return Results.Ok(await repository.ListMarketListingsAsync(
            serverId!,
            query?.Trim(),
            limit ?? 500,
            marketSort,
            cancellationToken));
    }

    private static async Task<IResult> ListOwnMarketListingsAsync(
        Guid playerUuid,
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

        return playerUuid == Guid.Empty
            ? Validation("playerUuid", "玩家 UUID 无效。")
            : Results.Ok(await repository.ListOwnMarketListingsAsync(
                serverId!, playerUuid, cancellationToken));
    }

    private static async Task<IResult> CreateMarketListingAsync(
        EconomyMarketCreateListingRequest request,
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

        var settings = options.Value;
        if (!EconomyRules.IsValidMarketListing(request, settings.MaximumTransferAmount))
        {
            return Validation("request", "市场上架参数无效。");
        }

        try
        {
            var response = await repository.CreateMarketListingAsync(
                serverId!,
                request with
                {
                    SellerName = request.SellerName.Trim(),
                    ItemId = request.ItemId.Trim()
                },
                settings.MarketListingFeeRate,
                settings.MarketMinimumListingFee,
                settings.MarketMaximumActiveListings,
                TimeSpan.FromHours(settings.MarketListingLifetimeHours),
                cancellationToken);
            return MarketWriteResult(response.Status, response);
        }
        catch (EconomyIdempotencyConflictException)
        {
            return IdempotencyConflict();
        }
    }

    private static async Task<IResult> PurchaseMarketListingAsync(
        EconomyMarketPurchaseRequest request,
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

        if (!EconomyRules.IsValidMarketPurchase(request))
        {
            return Validation("request", "市场购买参数无效。");
        }

        try
        {
            var response = await repository.PurchaseMarketListingAsync(
                serverId!,
                request with { BuyerName = request.BuyerName.Trim() },
                options.Value.MarketTransactionTaxRate,
                cancellationToken);
            return MarketWriteResult(response.Status, response);
        }
        catch (EconomyIdempotencyConflictException)
        {
            return IdempotencyConflict();
        }
    }

    private static async Task<IResult> CancelMarketListingAsync(
        EconomyMarketCancelRequest request,
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

        if (!EconomyRules.IsValidMarketCancel(request))
        {
            return Validation("request", "市场下架参数无效。");
        }

        try
        {
            var response = await repository.CancelMarketListingAsync(
                serverId!, request, cancellationToken);
            return MarketWriteResult(response.Status, response);
        }
        catch (EconomyIdempotencyConflictException)
        {
            return IdempotencyConflict();
        }
    }

    private static async Task<IResult> ListMarketDeliveriesAsync(
        Guid playerUuid,
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

        return playerUuid == Guid.Empty
            ? Validation("playerUuid", "玩家 UUID 无效。")
            : Results.Ok(await repository.ListMarketDeliveriesAsync(
                serverId!, playerUuid, cancellationToken));
    }

    private static async Task<IResult> ClaimMarketDeliveryAsync(
        EconomyMarketClaimRequest request,
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

        if (!EconomyRules.IsValidMarketClaim(request))
        {
            return Validation("request", "待领取物品参数无效。");
        }

        try
        {
            var response = await repository.ClaimMarketDeliveryAsync(
                serverId!, request, cancellationToken);
            return MarketWriteResult(response.Status, response);
        }
        catch (EconomyIdempotencyConflictException)
        {
            return IdempotencyConflict();
        }
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

    private static async Task<IResult> ListShopProductsAsync(
        HttpContext context,
        EconomyServiceTokenValidator tokenValidator,
        EconomyRepository repository,
        CancellationToken cancellationToken)
    {
        var authentication = Authenticate(context, tokenValidator, out _);
        return authentication ?? Results.Ok(
            await repository.ListShopProductsAsync(cancellationToken));
    }

    private static async Task<IResult> PurchaseShopProductAsync(
        EconomyShopPurchaseRequest request,
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

        if (!EconomyRules.IsValidShopPurchase(request))
        {
            return Validation("request", "商城购买参数无效。");
        }

        try
        {
            var response = await repository.PurchaseShopProductAsync(
                serverId!,
                request with { ItemId = request.ItemId.Trim() },
                cancellationToken);
            return MarketWriteResult(response.Status, response);
        }
        catch (EconomyIdempotencyConflictException)
        {
            return IdempotencyConflict();
        }
    }

    private static async Task<IResult> ListShopDeliveriesAsync(
        Guid playerUuid,
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

        return playerUuid == Guid.Empty
            ? Validation("playerUuid", "玩家 UUID 无效。")
            : Results.Ok(await repository.ListShopDeliveriesAsync(
                serverId!, playerUuid, cancellationToken));
    }

    private static async Task<IResult> ClaimShopDeliveryAsync(
        EconomyShopClaimRequest request,
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

        if (!EconomyRules.IsValidShopClaim(request))
        {
            return Validation("request", "商城待领取参数无效。");
        }

        try
        {
            var response = await repository.ClaimShopDeliveryAsync(
                serverId!, request, cancellationToken);
            return MarketWriteResult(response.Status, response);
        }
        catch (EconomyIdempotencyConflictException)
        {
            return IdempotencyConflict();
        }
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

        try
        {
            return Results.Ok(await repository.UpsertProductAsync(
                itemId,
                request with { ActorName = request.ActorName.Trim() },
                cancellationToken));
        }
        catch (EconomyBuybackPriceConflictException)
        {
            return Results.Conflict(new
            {
                code = "BUYBACK_PRICE_NOT_BELOW_SHOP",
                message = "新的回收价必须低于当前商城购买价，请先调整或暂停商城售价。"
            });
        }
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

    private static async Task<IResult> UpsertShopProductAsync(
        string itemId,
        EconomyShopProductUpsertRequest request,
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
            !EconomyRules.IsValidShopProductMutation(request))
        {
            return Validation("request", "商城商品配置无效。");
        }

        try
        {
            var product = await repository.UpsertShopProductAsync(
                itemId,
                request with { ActorName = request.ActorName.Trim() },
                cancellationToken);
            return product is null ? Results.NotFound() : Results.Ok(product);
        }
        catch (EconomyShopPriceConflictException)
        {
            return Results.Conflict(new
            {
                code = "SHOP_PRICE_NOT_ABOVE_BUYBACK",
                message = "商城购买价必须高于当前回收价。"
            });
        }
    }

    private static async Task<IResult> DisableShopProductAsync(
        string itemId,
        EconomyShopProductDisableRequest request,
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
            !EconomyRules.IsValidShopProductDisable(request))
        {
            return Validation("request", "商城商品停用参数无效。");
        }

        var status = await repository.DisableShopProductAsync(
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

    private static IResult MarketWriteResult<T>(string status, T response) =>
        status == "Applied"
            ? Results.Ok(response)
            : Results.Json(response, statusCode: StatusCodes.Status409Conflict);
}

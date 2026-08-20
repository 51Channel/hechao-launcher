using System.Data;
using Npgsql;

namespace Hechao.Api.Economy;

public sealed partial class EconomyRepository
{
    public async Task<IReadOnlyList<EconomyMarketListingResponse>> ListMarketListingsAsync(
        string serverId,
        string? query,
        int limit,
        CancellationToken cancellationToken)
        => await ListMarketListingsAsync(
            serverId,
            query,
            limit,
            EconomyMarketSort.RecentlyListed,
            cancellationToken);

    public async Task<IReadOnlyList<EconomyMarketListingResponse>> ListMarketListingsAsync(
        string serverId,
        string? query,
        int limit,
        EconomyMarketSort sort,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await ExpireMarketListingsAsync(
            connection, transaction, serverId, now, cancellationToken);

        var listings = new List<EconomyMarketListingResponse>();
        await using (var command = new NpgsqlCommand(
            BuildMarketListingSql(sort), connection, transaction))
        {
            command.Parameters.AddWithValue(serverId);
            command.Parameters.AddWithValue(now);
            command.Parameters.AddWithValue(query ?? string.Empty);
            command.Parameters.AddWithValue(limit);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                listings.Add(ReadMarketListing(reader));
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return listings;
    }

    public async Task<IReadOnlyList<EconomyMarketListingResponse>> ListOwnMarketListingsAsync(
        string serverId,
        Guid playerUuid,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await ExpireMarketListingsAsync(
            connection, transaction, serverId, now, cancellationToken);
        const string sql = """
            SELECT listing_id, server_id, seller_uuid, seller_name, item_id,
                   quantity, total_price, listing_fee, status, created_at, expires_at
            FROM launcher.economy_market_listings
            WHERE server_id = $1 AND seller_uuid = $2 AND status = 'Active'
            ORDER BY created_at DESC, listing_id;
            """;
        var listings = new List<EconomyMarketListingResponse>();
        await using (var command = new NpgsqlCommand(sql, connection, transaction))
        {
            command.Parameters.AddWithValue(serverId);
            command.Parameters.AddWithValue(playerUuid);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                listings.Add(ReadMarketListing(reader));
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return listings;
    }

    public async Task<EconomyMarketCreateListingResponse> CreateMarketListingAsync(
        string serverId,
        EconomyMarketCreateListingRequest request,
        decimal listingFeeRate,
        decimal minimumListingFee,
        int maximumActiveListings,
        TimeSpan lifetime,
        CancellationToken cancellationToken)
    {
        var fingerprint = EconomyRules.Fingerprint(
            "MarketList",
            request.SellerUuid,
            request.SellerName,
            request.ItemId,
            request.Quantity,
            request.TotalPrice);
        var now = timeProvider.GetUtcNow();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var reservation = await ReserveOperationAsync(
            connection,
            transaction,
            serverId,
            request.IdempotencyKey,
            fingerprint,
            "MarketList",
            cancellationToken);
        if (!reservation.Created)
        {
            return Deserialize<EconomyMarketCreateListingResponse>(reservation.ResponseJson!);
        }

        await ExpireMarketListingsAsync(
            connection, transaction, serverId, now, cancellationToken);
        await LockMarketActorAsync(
            connection, transaction, serverId, request.SellerUuid, cancellationToken);
        var activeCount = await CountActiveMarketListingsAsync(
            connection, transaction, serverId, request.SellerUuid, cancellationToken);
        var fee = decimal.Max(
            minimumListingFee,
            decimal.Round(
                request.TotalPrice * listingFeeRate,
                2,
                MidpointRounding.AwayFromZero));
        if (activeCount >= maximumActiveListings)
        {
            return await RejectMarketListingAsync(
                connection,
                transaction,
                reservation.OperationId,
                fee,
                "ACTIVE_LISTING_LIMIT",
                cancellationToken);
        }

        await EnsureAccountAsync(
            connection, transaction, request.SellerUuid, now, cancellationToken);
        var balance = await LockBalanceAsync(
            connection, transaction, request.SellerUuid, cancellationToken);
        if (balance < fee)
        {
            return await RejectMarketListingAsync(
                connection,
                transaction,
                reservation.OperationId,
                fee,
                "INSUFFICIENT_LISTING_FEE",
                cancellationToken,
                balance);
        }

        balance -= fee;
        await SetBalanceAsync(
            connection, transaction, request.SellerUuid, balance, now, cancellationToken);
        await InsertEntryAsync(
            connection,
            transaction,
            reservation.OperationId,
            PlayerAccount(request.SellerUuid),
            -fee,
            request.SellerUuid,
            cancellationToken);
        await InsertEntryAsync(
            connection,
            transaction,
            reservation.OperationId,
            "system:market-fees",
            fee,
            null,
            cancellationToken);

        var listing = new EconomyMarketListingResponse(
            Guid.NewGuid(),
            serverId,
            request.SellerUuid,
            request.SellerName,
            request.ItemId,
            request.Quantity,
            request.TotalPrice,
            fee,
            "Active",
            now,
            now.Add(lifetime));
        await InsertMarketListingAsync(
            connection,
            transaction,
            listing,
            reservation.OperationId,
            cancellationToken);
        var response = new EconomyMarketCreateListingResponse(
            reservation.OperationId,
            "Applied",
            listing,
            fee,
            balance);
        await CompleteOperationAsync(
            connection,
            transaction,
            reservation.OperationId,
            "Applied",
            response,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return response;
    }

    public async Task<EconomyMarketPurchaseResponse> PurchaseMarketListingAsync(
        string serverId,
        EconomyMarketPurchaseRequest request,
        decimal transactionTaxRate,
        CancellationToken cancellationToken)
    {
        var fingerprint = EconomyRules.Fingerprint(
            "MarketBuy",
            request.ListingId,
            request.BuyerUuid,
            request.BuyerName);
        var now = timeProvider.GetUtcNow();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var reservation = await ReserveOperationAsync(
            connection,
            transaction,
            serverId,
            request.IdempotencyKey,
            fingerprint,
            "MarketBuy",
            cancellationToken);
        if (!reservation.Created)
        {
            return Deserialize<EconomyMarketPurchaseResponse>(reservation.ResponseJson!);
        }

        var listing = await ReadMarketListingForUpdateAsync(
            connection, transaction, request.ListingId, cancellationToken);
        if (listing is null ||
            !string.Equals(listing.ServerId, serverId, StringComparison.Ordinal))
        {
            return await RejectMarketPurchaseAsync(
                connection,
                transaction,
                reservation.OperationId,
                request.ListingId,
                "LISTING_NOT_FOUND",
                cancellationToken);
        }

        if (listing.Status == "Active" && listing.ExpiresAt <= now)
        {
            await ExpireMarketListingAsync(
                connection, transaction, listing, now, cancellationToken);
            return await RejectMarketPurchaseAsync(
                connection,
                transaction,
                reservation.OperationId,
                request.ListingId,
                "LISTING_EXPIRED",
                cancellationToken,
                listing);
        }

        if (listing.Status != "Active")
        {
            return await RejectMarketPurchaseAsync(
                connection,
                transaction,
                reservation.OperationId,
                request.ListingId,
                "LISTING_NOT_ACTIVE",
                cancellationToken,
                listing);
        }

        if (listing.SellerUuid == request.BuyerUuid)
        {
            return await RejectMarketPurchaseAsync(
                connection,
                transaction,
                reservation.OperationId,
                request.ListingId,
                "CANNOT_BUY_OWN_LISTING",
                cancellationToken,
                listing);
        }

        await EnsureAccountAsync(
            connection, transaction, request.BuyerUuid, now, cancellationToken);
        await EnsureAccountAsync(
            connection, transaction, listing.SellerUuid, now, cancellationToken);
        var balances = await LockBalancesAsync(
            connection,
            transaction,
            request.BuyerUuid,
            listing.SellerUuid,
            cancellationToken);
        var buyerBalance = balances[request.BuyerUuid];
        if (buyerBalance < listing.TotalPrice)
        {
            return await RejectMarketPurchaseAsync(
                connection,
                transaction,
                reservation.OperationId,
                request.ListingId,
                "INSUFFICIENT_FUNDS",
                cancellationToken,
                listing,
                buyerBalance);
        }

        var tax = decimal.Round(
            listing.TotalPrice * transactionTaxRate,
            2,
            MidpointRounding.AwayFromZero);
        var sellerProceeds = listing.TotalPrice - tax;
        buyerBalance -= listing.TotalPrice;
        var sellerBalance = balances[listing.SellerUuid] + sellerProceeds;
        await SetBalanceAsync(
            connection, transaction, request.BuyerUuid, buyerBalance, now, cancellationToken);
        await SetBalanceAsync(
            connection, transaction, listing.SellerUuid, sellerBalance, now, cancellationToken);
        await InsertEntryAsync(
            connection,
            transaction,
            reservation.OperationId,
            PlayerAccount(request.BuyerUuid),
            -listing.TotalPrice,
            request.BuyerUuid,
            cancellationToken);
        await InsertEntryAsync(
            connection,
            transaction,
            reservation.OperationId,
            PlayerAccount(listing.SellerUuid),
            sellerProceeds,
            listing.SellerUuid,
            cancellationToken);
        if (tax > 0)
        {
            await InsertEntryAsync(
                connection,
                transaction,
                reservation.OperationId,
                "system:market-fees",
                tax,
                null,
                cancellationToken);
        }

        var delivery = await InsertMarketDeliveryAsync(
            connection,
            transaction,
            request.BuyerUuid,
            listing,
            "Purchase",
            now,
            cancellationToken);
        await MarkMarketListingSoldAsync(
            connection,
            transaction,
            listing.ListingId,
            request.BuyerUuid,
            reservation.OperationId,
            now,
            cancellationToken);
        var response = new EconomyMarketPurchaseResponse(
            reservation.OperationId,
            "Applied",
            listing.ListingId,
            delivery.DeliveryId,
            listing.ItemId,
            listing.Quantity,
            listing.TotalPrice,
            sellerProceeds,
            tax,
            buyerBalance);
        await CompleteOperationAsync(
            connection,
            transaction,
            reservation.OperationId,
            "Applied",
            response,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return response;
    }

    public async Task<EconomyMarketCancelResponse> CancelMarketListingAsync(
        string serverId,
        EconomyMarketCancelRequest request,
        CancellationToken cancellationToken)
    {
        var fingerprint = EconomyRules.Fingerprint(
            "MarketCancel", request.ListingId, request.SellerUuid);
        var now = timeProvider.GetUtcNow();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var reservation = await ReserveOperationAsync(
            connection,
            transaction,
            serverId,
            request.IdempotencyKey,
            fingerprint,
            "MarketCancel",
            cancellationToken);
        if (!reservation.Created)
        {
            return Deserialize<EconomyMarketCancelResponse>(reservation.ResponseJson!);
        }

        var listing = await ReadMarketListingForUpdateAsync(
            connection, transaction, request.ListingId, cancellationToken);
        if (listing is null ||
            !string.Equals(listing.ServerId, serverId, StringComparison.Ordinal) ||
            listing.SellerUuid != request.SellerUuid)
        {
            return await RejectMarketCancelAsync(
                connection,
                transaction,
                reservation.OperationId,
                request.ListingId,
                "LISTING_NOT_FOUND",
                cancellationToken);
        }

        if (listing.Status == "Active" && listing.ExpiresAt <= now)
        {
            await ExpireMarketListingAsync(
                connection, transaction, listing, now, cancellationToken);
            return await RejectMarketCancelAsync(
                connection,
                transaction,
                reservation.OperationId,
                request.ListingId,
                "LISTING_EXPIRED",
                cancellationToken,
                listing);
        }

        if (listing.Status != "Active")
        {
            return await RejectMarketCancelAsync(
                connection,
                transaction,
                reservation.OperationId,
                request.ListingId,
                "LISTING_NOT_ACTIVE",
                cancellationToken,
                listing);
        }

        const string updateSql = """
            UPDATE launcher.economy_market_listings
            SET status = 'Cancelled', completion_operation_id = $2
            WHERE listing_id = $1 AND status = 'Active';
            """;
        await using (var update = new NpgsqlCommand(updateSql, connection, transaction))
        {
            update.Parameters.AddWithValue(listing.ListingId);
            update.Parameters.AddWithValue(reservation.OperationId);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        var delivery = await InsertMarketDeliveryAsync(
            connection,
            transaction,
            request.SellerUuid,
            listing,
            "Cancelled",
            now,
            cancellationToken);
        var response = new EconomyMarketCancelResponse(
            reservation.OperationId,
            "Applied",
            listing.ListingId,
            delivery.DeliveryId,
            listing.ItemId,
            listing.Quantity);
        await CompleteOperationAsync(
            connection,
            transaction,
            reservation.OperationId,
            "Applied",
            response,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return response;
    }

    public async Task<IReadOnlyList<EconomyMarketDeliveryResponse>> ListMarketDeliveriesAsync(
        string serverId,
        Guid playerUuid,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await ExpireMarketListingsAsync(
            connection, transaction, serverId, now, cancellationToken);
        const string sql = """
            SELECT delivery_id, player_uuid, source_listing_id, server_id,
                   item_id, quantity, reason, status, created_at
            FROM launcher.economy_market_deliveries
            WHERE server_id = $1 AND player_uuid = $2 AND status = 'Pending'
            ORDER BY created_at, delivery_id;
            """;
        var deliveries = new List<EconomyMarketDeliveryResponse>();
        await using (var command = new NpgsqlCommand(sql, connection, transaction))
        {
            command.Parameters.AddWithValue(serverId);
            command.Parameters.AddWithValue(playerUuid);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                deliveries.Add(ReadMarketDelivery(reader));
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return deliveries;
    }

    public async Task<EconomyMarketClaimResponse> ClaimMarketDeliveryAsync(
        string serverId,
        EconomyMarketClaimRequest request,
        CancellationToken cancellationToken)
    {
        var fingerprint = EconomyRules.Fingerprint(
            "MarketClaim", request.DeliveryId, request.PlayerUuid);
        var now = timeProvider.GetUtcNow();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var reservation = await ReserveOperationAsync(
            connection,
            transaction,
            serverId,
            request.IdempotencyKey,
            fingerprint,
            "MarketClaim",
            cancellationToken);
        if (!reservation.Created)
        {
            return Deserialize<EconomyMarketClaimResponse>(reservation.ResponseJson!);
        }

        var delivery = await ReadMarketDeliveryForUpdateAsync(
            connection, transaction, request.DeliveryId, cancellationToken);
        if (delivery is null ||
            delivery.PlayerUuid != request.PlayerUuid ||
            !string.Equals(delivery.ServerId, serverId, StringComparison.Ordinal))
        {
            return await RejectMarketClaimAsync(
                connection,
                transaction,
                reservation.OperationId,
                request.DeliveryId,
                "DELIVERY_NOT_FOUND",
                cancellationToken);
        }

        if (delivery.Status != "Pending")
        {
            return await RejectMarketClaimAsync(
                connection,
                transaction,
                reservation.OperationId,
                request.DeliveryId,
                "DELIVERY_ALREADY_CLAIMED",
                cancellationToken,
                delivery);
        }

        const string updateSql = """
            UPDATE launcher.economy_market_deliveries
            SET status = 'Claimed', claimed_at = $2, claim_operation_id = $3
            WHERE delivery_id = $1 AND status = 'Pending';
            """;
        await using (var update = new NpgsqlCommand(updateSql, connection, transaction))
        {
            update.Parameters.AddWithValue(delivery.DeliveryId);
            update.Parameters.AddWithValue(now);
            update.Parameters.AddWithValue(reservation.OperationId);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        var response = new EconomyMarketClaimResponse(
            reservation.OperationId,
            "Applied",
            delivery.DeliveryId,
            delivery.ItemId,
            delivery.Quantity);
        await CompleteOperationAsync(
            connection,
            transaction,
            reservation.OperationId,
            "Applied",
            response,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return response;
    }

    internal static string BuildMarketListingSql() =>
        BuildMarketListingSql(EconomyMarketSort.RecentlyListed);

    internal static string BuildMarketListingSql(EconomyMarketSort sort)
    {
        var orderBy = sort switch
        {
            EconomyMarketSort.LowestUnitPrice =>
                "total_price / quantity ASC, created_at DESC, listing_id",
            EconomyMarketSort.HighestUnitPrice =>
                "total_price / quantity DESC, created_at DESC, listing_id",
            EconomyMarketSort.ExpiringSoon =>
                "expires_at ASC, created_at DESC, listing_id",
            _ => "created_at DESC, listing_id"
        };

        return $"""
            SELECT listing_id, server_id, seller_uuid, seller_name, item_id,
                   quantity, total_price, listing_fee, status, created_at, expires_at
            FROM launcher.economy_market_listings
            WHERE server_id = $1 AND status = 'Active' AND expires_at > $2
              AND ($3 = '' OR position(lower($3) in lower(item_id || ' ' || seller_name)) > 0)
            ORDER BY {orderBy}
            LIMIT $4;
            """;
    }

    private static async Task LockMarketActorAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string serverId,
        Guid playerUuid,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtextextended($1, 0));",
            connection,
            transaction);
        command.Parameters.AddWithValue($"market:{serverId}:{playerUuid:D}");
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> CountActiveMarketListingsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string serverId,
        Guid sellerUuid,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT count(*)::integer
            FROM launcher.economy_market_listings
            WHERE server_id = $1 AND seller_uuid = $2 AND status = 'Active';
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(serverId);
        command.Parameters.AddWithValue(sellerUuid);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken) ?? 0);
    }

    private static async Task InsertMarketListingAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        EconomyMarketListingResponse listing,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO launcher.economy_market_listings
                (listing_id, server_id, seller_uuid, seller_name, item_id,
                 quantity, total_price, listing_fee, status, created_at,
                 expires_at, creation_operation_id)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, 'Active', $9, $10, $11);
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(listing.ListingId);
        command.Parameters.AddWithValue(listing.ServerId);
        command.Parameters.AddWithValue(listing.SellerUuid);
        command.Parameters.AddWithValue(listing.SellerName);
        command.Parameters.AddWithValue(listing.ItemId);
        command.Parameters.AddWithValue(listing.Quantity);
        command.Parameters.AddWithValue(listing.TotalPrice);
        command.Parameters.AddWithValue(listing.ListingFee);
        command.Parameters.AddWithValue(listing.CreatedAt);
        command.Parameters.AddWithValue(listing.ExpiresAt);
        command.Parameters.AddWithValue(operationId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<EconomyMarketListingResponse?> ReadMarketListingForUpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid listingId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT listing_id, server_id, seller_uuid, seller_name, item_id,
                   quantity, total_price, listing_fee, status, created_at, expires_at
            FROM launcher.economy_market_listings
            WHERE listing_id = $1
            FOR UPDATE;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(listingId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadMarketListing(reader) : null;
    }

    private static EconomyMarketListingResponse ReadMarketListing(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetString(1),
        reader.GetGuid(2),
        reader.GetString(3),
        reader.GetString(4),
        reader.GetInt32(5),
        reader.GetDecimal(6),
        reader.GetDecimal(7),
        reader.GetString(8),
        reader.GetFieldValue<DateTimeOffset>(9),
        reader.GetFieldValue<DateTimeOffset>(10));

    private static async Task MarkMarketListingSoldAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid listingId,
        Guid buyerUuid,
        Guid operationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE launcher.economy_market_listings
            SET status = 'Sold', sold_at = $2, buyer_uuid = $3,
                completion_operation_id = $4
            WHERE listing_id = $1 AND status = 'Active';
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(listingId);
        command.Parameters.AddWithValue(now);
        command.Parameters.AddWithValue(buyerUuid);
        command.Parameters.AddWithValue(operationId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ExpireMarketListingsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string serverId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT listing_id, server_id, seller_uuid, seller_name, item_id,
                   quantity, total_price, listing_fee, status, created_at, expires_at
            FROM launcher.economy_market_listings
            WHERE server_id = $1 AND status = 'Active' AND expires_at <= $2
            ORDER BY expires_at
            FOR UPDATE;
            """;
        var expired = new List<EconomyMarketListingResponse>();
        await using (var command = new NpgsqlCommand(sql, connection, transaction))
        {
            command.Parameters.AddWithValue(serverId);
            command.Parameters.AddWithValue(now);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                expired.Add(ReadMarketListing(reader));
            }
        }

        foreach (var listing in expired)
        {
            await ExpireMarketListingAsync(
                connection, transaction, listing, now, cancellationToken);
        }
    }

    private static async Task ExpireMarketListingAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        EconomyMarketListingResponse listing,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using (var update = new NpgsqlCommand(
            "UPDATE launcher.economy_market_listings SET status = 'Expired' " +
            "WHERE listing_id = $1 AND status = 'Active';",
            connection,
            transaction))
        {
            update.Parameters.AddWithValue(listing.ListingId);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        await InsertMarketDeliveryAsync(
            connection,
            transaction,
            listing.SellerUuid,
            listing,
            "Expired",
            now,
            cancellationToken);
    }

    private static async Task<EconomyMarketDeliveryResponse> InsertMarketDeliveryAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid playerUuid,
        EconomyMarketListingResponse listing,
        string reason,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var delivery = new EconomyMarketDeliveryResponse(
            Guid.NewGuid(),
            playerUuid,
            listing.ListingId,
            listing.ServerId,
            listing.ItemId,
            listing.Quantity,
            reason,
            "Pending",
            now);
        const string sql = """
            INSERT INTO launcher.economy_market_deliveries
                (delivery_id, player_uuid, source_listing_id, server_id,
                 item_id, quantity, reason, status, created_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7, 'Pending', $8)
            ON CONFLICT (source_listing_id, player_uuid, reason) DO NOTHING
            RETURNING delivery_id;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(delivery.DeliveryId);
        command.Parameters.AddWithValue(delivery.PlayerUuid);
        command.Parameters.AddWithValue(delivery.ListingId);
        command.Parameters.AddWithValue(delivery.ServerId);
        command.Parameters.AddWithValue(delivery.ItemId);
        command.Parameters.AddWithValue(delivery.Quantity);
        command.Parameters.AddWithValue(delivery.Reason);
        command.Parameters.AddWithValue(delivery.CreatedAt);
        var inserted = await command.ExecuteScalarAsync(cancellationToken);
        if (inserted is Guid insertedId)
        {
            return delivery with { DeliveryId = insertedId };
        }

        const string existingSql = """
            SELECT delivery_id, player_uuid, source_listing_id, server_id,
                   item_id, quantity, reason, status, created_at
            FROM launcher.economy_market_deliveries
            WHERE source_listing_id = $1 AND player_uuid = $2 AND reason = $3;
            """;
        await using var existing = new NpgsqlCommand(existingSql, connection, transaction);
        existing.Parameters.AddWithValue(listing.ListingId);
        existing.Parameters.AddWithValue(playerUuid);
        existing.Parameters.AddWithValue(reason);
        await using var reader = await existing.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Market delivery could not be created.");
        }
        return ReadMarketDelivery(reader);
    }

    private static async Task<EconomyMarketDeliveryResponse?> ReadMarketDeliveryForUpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid deliveryId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT delivery_id, player_uuid, source_listing_id, server_id,
                   item_id, quantity, reason, status, created_at
            FROM launcher.economy_market_deliveries
            WHERE delivery_id = $1
            FOR UPDATE;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(deliveryId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadMarketDelivery(reader) : null;
    }

    private static EconomyMarketDeliveryResponse ReadMarketDelivery(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetGuid(1),
        reader.GetGuid(2),
        reader.GetString(3),
        reader.GetString(4),
        reader.GetInt32(5),
        reader.GetString(6),
        reader.GetString(7),
        reader.GetFieldValue<DateTimeOffset>(8));

    private static async Task<EconomyMarketCreateListingResponse> RejectMarketListingAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid operationId,
        decimal fee,
        string failureCode,
        CancellationToken cancellationToken,
        decimal balance = 0m)
    {
        var response = new EconomyMarketCreateListingResponse(
            operationId, "Rejected", null, fee, balance, failureCode);
        await CompleteOperationAsync(
            connection, transaction, operationId, "Rejected", response, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return response;
    }

    private static async Task<EconomyMarketPurchaseResponse> RejectMarketPurchaseAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid operationId,
        Guid listingId,
        string failureCode,
        CancellationToken cancellationToken,
        EconomyMarketListingResponse? listing = null,
        decimal buyerBalance = 0m)
    {
        var response = new EconomyMarketPurchaseResponse(
            operationId,
            "Rejected",
            listingId,
            null,
            listing?.ItemId ?? string.Empty,
            listing?.Quantity ?? 0,
            listing?.TotalPrice ?? 0m,
            0m,
            0m,
            buyerBalance,
            failureCode);
        await CompleteOperationAsync(
            connection, transaction, operationId, "Rejected", response, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return response;
    }

    private static async Task<EconomyMarketCancelResponse> RejectMarketCancelAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid operationId,
        Guid listingId,
        string failureCode,
        CancellationToken cancellationToken,
        EconomyMarketListingResponse? listing = null)
    {
        var response = new EconomyMarketCancelResponse(
            operationId,
            "Rejected",
            listingId,
            null,
            listing?.ItemId ?? string.Empty,
            listing?.Quantity ?? 0,
            failureCode);
        await CompleteOperationAsync(
            connection, transaction, operationId, "Rejected", response, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return response;
    }

    private static async Task<EconomyMarketClaimResponse> RejectMarketClaimAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid operationId,
        Guid deliveryId,
        string failureCode,
        CancellationToken cancellationToken,
        EconomyMarketDeliveryResponse? delivery = null)
    {
        var response = new EconomyMarketClaimResponse(
            operationId,
            "Rejected",
            deliveryId,
            delivery?.ItemId ?? string.Empty,
            delivery?.Quantity ?? 0,
            failureCode);
        await CompleteOperationAsync(
            connection, transaction, operationId, "Rejected", response, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return response;
    }
}

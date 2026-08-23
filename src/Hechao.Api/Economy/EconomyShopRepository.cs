using System.Data;
using Npgsql;

namespace Hechao.Api.Economy;

public sealed class EconomyShopPriceConflictException()
    : InvalidOperationException("The shop price must be higher than the buyback price.");

public sealed class EconomyBuybackPriceConflictException()
    : InvalidOperationException("The buyback price must remain below the shop price.");

public sealed partial class EconomyRepository
{
    public async Task<EconomyProductResponse?> UpsertShopProductAsync(
        string itemId,
        EconomyShopProductUpsertRequest request,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var before = await ReadProductForUpdateAsync(
            connection, transaction, itemId, cancellationToken);
        if (before is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        if (request.ShopUnitPrice <= before.UnitPrice)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new EconomyShopPriceConflictException();
        }

        const string sql = """
            UPDATE launcher.economy_products
            SET shop_unit_price = $2,
                updated_by_uuid = $3,
                updated_by_name = $4,
                updated_at = $5
            WHERE item_id = $1
            RETURNING item_id, unit_price, personal_daily_limit,
                      server_daily_limit, enabled, updated_by_uuid,
                      updated_by_name, updated_at, shop_unit_price;
            """;
        EconomyProductResponse after;
        await using (var command = new NpgsqlCommand(sql, connection, transaction))
        {
            command.Parameters.AddWithValue(itemId);
            command.Parameters.AddWithValue(request.ShopUnitPrice);
            command.Parameters.AddWithValue(request.ActorUuid);
            command.Parameters.AddWithValue(request.ActorName.Trim());
            command.Parameters.AddWithValue(now);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            after = ReadProduct(reader);
        }

        await WriteProductAuditAsync(
            connection,
            transaction,
            itemId,
            "ShopUpsert",
            request.ActorUuid,
            request.ActorName,
            before,
            after,
            now,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return after;
    }

    public async Task<EconomyProductMutationStatus> DisableShopProductAsync(
        string itemId,
        EconomyShopProductDisableRequest request,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var before = await ReadProductForUpdateAsync(
            connection, transaction, itemId, cancellationToken);
        if (before is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return EconomyProductMutationStatus.NotFound;
        }

        const string sql = """
            UPDATE launcher.economy_products
            SET shop_unit_price = NULL,
                updated_by_uuid = $2,
                updated_by_name = $3,
                updated_at = $4
            WHERE item_id = $1;
            """;
        await using (var command = new NpgsqlCommand(sql, connection, transaction))
        {
            command.Parameters.AddWithValue(itemId);
            command.Parameters.AddWithValue(request.ActorUuid);
            command.Parameters.AddWithValue(request.ActorName.Trim());
            command.Parameters.AddWithValue(now);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var after = before with
        {
            ShopUnitPrice = null,
            UpdatedByUuid = request.ActorUuid,
            UpdatedByName = request.ActorName.Trim(),
            UpdatedAt = now
        };
        await WriteProductAuditAsync(
            connection,
            transaction,
            itemId,
            "ShopDisable",
            request.ActorUuid,
            request.ActorName,
            before,
            after,
            now,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return EconomyProductMutationStatus.Applied;
    }

    public async Task<EconomyShopPurchaseResponse> PurchaseShopProductAsync(
        string serverId,
        EconomyShopPurchaseRequest request,
        CancellationToken cancellationToken)
    {
        var fingerprint = EconomyRules.Fingerprint(
            "ShopBuy",
            request.PlayerUuid,
            request.ItemId,
            request.Quantity);
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
            "ShopBuy",
            cancellationToken);
        if (!reservation.Created)
        {
            return Deserialize<EconomyShopPurchaseResponse>(reservation.ResponseJson!);
        }

        var product = await ReadProductForUpdateAsync(
            connection, transaction, request.ItemId, cancellationToken);
        if (product is null || !product.Enabled || product.ShopUnitPrice is null)
        {
            return await RejectShopPurchaseAsync(
                connection,
                transaction,
                reservation.OperationId,
                request,
                product,
                "PRODUCT_NOT_AVAILABLE",
                cancellationToken);
        }

        var unitPrice = product.ShopUnitPrice.Value;
        var totalAmount = decimal.Round(
            unitPrice * request.Quantity,
            2,
            MidpointRounding.AwayFromZero);
        await EnsureAccountAsync(
            connection, transaction, request.PlayerUuid, now, cancellationToken);
        var balance = await LockBalanceAsync(
            connection, transaction, request.PlayerUuid, cancellationToken);
        if (balance < totalAmount)
        {
            return await RejectShopPurchaseAsync(
                connection,
                transaction,
                reservation.OperationId,
                request,
                product,
                "INSUFFICIENT_FUNDS",
                cancellationToken,
                balance);
        }

        balance -= totalAmount;
        await SetBalanceAsync(
            connection,
            transaction,
            request.PlayerUuid,
            balance,
            now,
            cancellationToken);
        await InsertEntryAsync(
            connection,
            transaction,
            reservation.OperationId,
            PlayerAccount(request.PlayerUuid),
            -totalAmount,
            request.PlayerUuid,
            cancellationToken);
        await InsertEntryAsync(
            connection,
            transaction,
            reservation.OperationId,
            "system:shop-sink",
            totalAmount,
            null,
            cancellationToken);

        var delivery = new EconomyShopDeliveryResponse(
            Guid.NewGuid(),
            request.PlayerUuid,
            serverId,
            request.ItemId,
            request.Quantity,
            unitPrice,
            totalAmount,
            "Pending",
            now);
        await InsertShopDeliveryAsync(
            connection,
            transaction,
            delivery,
            reservation.OperationId,
            cancellationToken);

        var response = new EconomyShopPurchaseResponse(
            reservation.OperationId,
            "Applied",
            request.PlayerUuid,
            delivery.DeliveryId,
            request.ItemId,
            request.Quantity,
            unitPrice,
            totalAmount,
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

    public async Task<IReadOnlyList<EconomyShopDeliveryResponse>> ListShopDeliveriesAsync(
        string serverId,
        Guid playerUuid,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT delivery_id, player_uuid, server_id, item_id, quantity,
                   unit_price, total_amount, status, created_at
            FROM launcher.economy_shop_deliveries
            WHERE server_id = $1 AND player_uuid = $2 AND status = 'Pending'
            ORDER BY created_at, delivery_id;
            """;
        var deliveries = new List<EconomyShopDeliveryResponse>();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(serverId);
        command.Parameters.AddWithValue(playerUuid);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            deliveries.Add(ReadShopDelivery(reader));
        }

        return deliveries;
    }

    public async Task<EconomyShopClaimResponse> ClaimShopDeliveryAsync(
        string serverId,
        EconomyShopClaimRequest request,
        CancellationToken cancellationToken)
    {
        var fingerprint = EconomyRules.Fingerprint(
            "ShopClaim",
            request.DeliveryId,
            request.PlayerUuid);
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
            "ShopClaim",
            cancellationToken);
        if (!reservation.Created)
        {
            return Deserialize<EconomyShopClaimResponse>(reservation.ResponseJson!);
        }

        var delivery = await ReadShopDeliveryForUpdateAsync(
            connection, transaction, request.DeliveryId, cancellationToken);
        if (delivery is null ||
            delivery.PlayerUuid != request.PlayerUuid ||
            !string.Equals(delivery.ServerId, serverId, StringComparison.Ordinal))
        {
            return await RejectShopClaimAsync(
                connection,
                transaction,
                reservation.OperationId,
                request.DeliveryId,
                "DELIVERY_NOT_FOUND",
                cancellationToken);
        }

        if (delivery.Status != "Pending")
        {
            return await RejectShopClaimAsync(
                connection,
                transaction,
                reservation.OperationId,
                request.DeliveryId,
                "DELIVERY_ALREADY_CLAIMED",
                cancellationToken,
                delivery);
        }

        var now = timeProvider.GetUtcNow();
        const string updateSql = """
            UPDATE launcher.economy_shop_deliveries
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

        var response = new EconomyShopClaimResponse(
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

    private static async Task InsertShopDeliveryAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        EconomyShopDeliveryResponse delivery,
        Guid purchaseOperationId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO launcher.economy_shop_deliveries
                (delivery_id, purchase_operation_id, player_uuid, server_id,
                 item_id, quantity, unit_price, total_amount, created_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9);
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(delivery.DeliveryId);
        command.Parameters.AddWithValue(purchaseOperationId);
        command.Parameters.AddWithValue(delivery.PlayerUuid);
        command.Parameters.AddWithValue(delivery.ServerId);
        command.Parameters.AddWithValue(delivery.ItemId);
        command.Parameters.AddWithValue(delivery.Quantity);
        command.Parameters.AddWithValue(delivery.UnitPrice);
        command.Parameters.AddWithValue(delivery.TotalAmount);
        command.Parameters.AddWithValue(delivery.CreatedAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<EconomyShopDeliveryResponse?> ReadShopDeliveryForUpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid deliveryId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT delivery_id, player_uuid, server_id, item_id, quantity,
                   unit_price, total_amount, status, created_at
            FROM launcher.economy_shop_deliveries
            WHERE delivery_id = $1
            FOR UPDATE;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(deliveryId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadShopDelivery(reader)
            : null;
    }

    private static EconomyShopDeliveryResponse ReadShopDelivery(
        NpgsqlDataReader reader) => new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetInt32(4),
            reader.GetDecimal(5),
            reader.GetDecimal(6),
            reader.GetString(7),
            reader.GetFieldValue<DateTimeOffset>(8));

    private static async Task<EconomyShopPurchaseResponse> RejectShopPurchaseAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid operationId,
        EconomyShopPurchaseRequest request,
        EconomyProductResponse? product,
        string failureCode,
        CancellationToken cancellationToken,
        decimal balance = 0m)
    {
        var response = new EconomyShopPurchaseResponse(
            operationId,
            "Rejected",
            request.PlayerUuid,
            null,
            request.ItemId,
            request.Quantity,
            product?.ShopUnitPrice ?? 0m,
            product?.ShopUnitPrice is null
                ? 0m
                : decimal.Round(
                    product.ShopUnitPrice.Value * request.Quantity,
                    2,
                    MidpointRounding.AwayFromZero),
            balance,
            failureCode);
        await CompleteOperationAsync(
            connection,
            transaction,
            operationId,
            "Rejected",
            response,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return response;
    }

    private static async Task<EconomyShopClaimResponse> RejectShopClaimAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid operationId,
        Guid deliveryId,
        string failureCode,
        CancellationToken cancellationToken,
        EconomyShopDeliveryResponse? delivery = null)
    {
        var response = new EconomyShopClaimResponse(
            operationId,
            "Rejected",
            deliveryId,
            delivery?.ItemId ?? string.Empty,
            delivery?.Quantity ?? 0,
            failureCode);
        await CompleteOperationAsync(
            connection,
            transaction,
            operationId,
            "Rejected",
            response,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return response;
    }
}

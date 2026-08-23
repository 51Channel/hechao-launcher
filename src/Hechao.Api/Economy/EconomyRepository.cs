using System.Data;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace Hechao.Api.Economy;

public sealed class EconomyIdempotencyConflictException()
    : InvalidOperationException("The idempotency key was already used for another request.");

public sealed partial class EconomyRepository(
    NpgsqlDataSource dataSource,
    TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<EconomyBalanceResponse> GetBalanceAsync(
        Guid playerUuid,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT available_balance, frozen_balance, updated_at
            FROM launcher.economy_accounts
            WHERE player_uuid = $1;
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(playerUuid);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new EconomyBalanceResponse(
                playerUuid,
                reader.GetDecimal(0),
                reader.GetDecimal(1),
                reader.GetFieldValue<DateTimeOffset>(2))
            : new EconomyBalanceResponse(playerUuid, 0m, 0m, null);
    }

    public async Task<EconomyTransferResponse> TransferAsync(
        string serverId,
        EconomyTransferRequest request,
        CancellationToken cancellationToken)
    {
        var fingerprint = EconomyRules.Fingerprint(
            "Transfer",
            request.SenderUuid,
            request.RecipientUuid,
            request.Amount,
            request.Note);
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
            "Transfer",
            cancellationToken);
        if (!reservation.Created)
        {
            return Deserialize<EconomyTransferResponse>(reservation.ResponseJson!);
        }

        await EnsureAccountAsync(
            connection,
            transaction,
            request.SenderUuid,
            now,
            cancellationToken);
        await EnsureAccountAsync(
            connection,
            transaction,
            request.RecipientUuid,
            now,
            cancellationToken);
        var balances = await LockBalancesAsync(
            connection,
            transaction,
            request.SenderUuid,
            request.RecipientUuid,
            cancellationToken);
        var senderBalance = balances[request.SenderUuid];
        var recipientBalance = balances[request.RecipientUuid];

        EconomyTransferResponse response;
        if (senderBalance < request.Amount)
        {
            response = new EconomyTransferResponse(
                reservation.OperationId,
                "Rejected",
                request.SenderUuid,
                request.RecipientUuid,
                request.Amount,
                senderBalance,
                recipientBalance,
                "INSUFFICIENT_FUNDS");
            await CompleteOperationAsync(
                connection,
                transaction,
                reservation.OperationId,
                "Rejected",
                response,
                cancellationToken);
        }
        else
        {
            senderBalance -= request.Amount;
            recipientBalance += request.Amount;
            await SetBalanceAsync(
                connection,
                transaction,
                request.SenderUuid,
                senderBalance,
                now,
                cancellationToken);
            await SetBalanceAsync(
                connection,
                transaction,
                request.RecipientUuid,
                recipientBalance,
                now,
                cancellationToken);
            await InsertEntryAsync(
                connection,
                transaction,
                reservation.OperationId,
                PlayerAccount(request.SenderUuid),
                -request.Amount,
                request.SenderUuid,
                cancellationToken);
            await InsertEntryAsync(
                connection,
                transaction,
                reservation.OperationId,
                PlayerAccount(request.RecipientUuid),
                request.Amount,
                request.RecipientUuid,
                cancellationToken);
            response = new EconomyTransferResponse(
                reservation.OperationId,
                "Applied",
                request.SenderUuid,
                request.RecipientUuid,
                request.Amount,
                senderBalance,
                recipientBalance);
            await CompleteOperationAsync(
                connection,
                transaction,
                reservation.OperationId,
                "Applied",
                response,
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return response;
    }

    public async Task<EconomyQuoteResult> CreateSaleQuoteAsync(
        string serverId,
        EconomySaleQuoteRequest request,
        TimeSpan lifetime,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        const string productSql = """
            SELECT unit_price, personal_daily_limit, server_daily_limit, enabled
            FROM launcher.economy_products
            WHERE item_id = $1;
            """;
        decimal unitPrice;
        int personalLimit;
        int serverLimit;
        bool enabled;
        await using (var product = new NpgsqlCommand(productSql, connection))
        {
            product.Parameters.AddWithValue(request.ItemId);
            await using var reader = await product.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return new EconomyQuoteResult(EconomyQuoteStatus.ProductNotFound);
            }

            unitPrice = reader.GetDecimal(0);
            personalLimit = reader.GetInt32(1);
            serverLimit = reader.GetInt32(2);
            enabled = reader.GetBoolean(3);
        }

        if (!enabled)
        {
            return new EconomyQuoteResult(EconomyQuoteStatus.ProductDisabled);
        }

        var date = DateOnly.FromDateTime(now.UtcDateTime);
        var personalUsed = await ReadPersonalUsageAsync(
            connection,
            null,
            date,
            serverId,
            request.PlayerUuid,
            request.ItemId,
            cancellationToken);
        var serverUsed = await ReadServerUsageAsync(
            connection,
            null,
            date,
            serverId,
            request.ItemId,
            cancellationToken);
        var saleQuantity = EconomyRules.CalculateSaleQuantity(
            request.Quantity,
            personalUsed,
            personalLimit,
            serverUsed,
            serverLimit);
        if (saleQuantity == 0 && personalUsed >= personalLimit)
        {
            return new EconomyQuoteResult(EconomyQuoteStatus.PersonalLimitExceeded);
        }

        if (saleQuantity == 0)
        {
            return new EconomyQuoteResult(EconomyQuoteStatus.ServerLimitExceeded);
        }

        var quote = new EconomySaleQuoteResponse(
            Guid.NewGuid(),
            request.PlayerUuid,
            request.ItemId,
            saleQuantity,
            unitPrice,
            decimal.Round(unitPrice * saleQuantity, 2),
            Math.Max(0, personalLimit - personalUsed - saleQuantity),
            Math.Max(0, serverLimit - serverUsed - saleQuantity),
            now.Add(lifetime));
        const string insertSql = """
            INSERT INTO launcher.economy_sale_quotes
                (quote_id, server_id, player_uuid, item_id, quantity,
                 unit_price, total_amount, created_at, expires_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9);
            """;
        await using var insert = new NpgsqlCommand(insertSql, connection);
        insert.Parameters.AddWithValue(quote.QuoteId);
        insert.Parameters.AddWithValue(serverId);
        insert.Parameters.AddWithValue(quote.PlayerUuid);
        insert.Parameters.AddWithValue(quote.ItemId);
        insert.Parameters.AddWithValue(quote.Quantity);
        insert.Parameters.AddWithValue(quote.UnitPrice);
        insert.Parameters.AddWithValue(quote.TotalAmount);
        insert.Parameters.AddWithValue(now);
        insert.Parameters.AddWithValue(quote.ExpiresAt);
        await insert.ExecuteNonQueryAsync(cancellationToken);
        return new EconomyQuoteResult(EconomyQuoteStatus.Created, quote);
    }

    public async Task<EconomySaleCommitResponse> CommitSaleAsync(
        string serverId,
        EconomySaleCommitRequest request,
        CancellationToken cancellationToken)
    {
        var fingerprint = EconomyRules.Fingerprint(
            "Sale",
            request.QuoteId,
            request.PlayerUuid);
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
            "Sale",
            cancellationToken);
        if (!reservation.Created)
        {
            return Deserialize<EconomySaleCommitResponse>(reservation.ResponseJson!);
        }

        var quote = await ReadQuoteForUpdateAsync(
            connection,
            transaction,
            request.QuoteId,
            cancellationToken);
        if (quote is null ||
            quote.PlayerUuid != request.PlayerUuid ||
            !string.Equals(quote.ServerId, serverId, StringComparison.Ordinal))
        {
            return await RejectSaleAsync(
                connection,
                transaction,
                reservation.OperationId,
                request,
                quote,
                "QUOTE_NOT_FOUND",
                cancellationToken);
        }

        if (quote.Status != "Open")
        {
            return await RejectSaleAsync(
                connection,
                transaction,
                reservation.OperationId,
                request,
                quote,
                quote.Status == "Committed" ? "QUOTE_ALREADY_COMMITTED" : "QUOTE_EXPIRED",
                cancellationToken);
        }

        if (quote.ExpiresAt <= now)
        {
            await SetQuoteExpiredAsync(
                connection,
                transaction,
                quote.QuoteId,
                cancellationToken);
            return await RejectSaleAsync(
                connection,
                transaction,
                reservation.OperationId,
                request,
                quote,
                "QUOTE_EXPIRED",
                cancellationToken);
        }

        var product = await ReadProductForUpdateAsync(
            connection,
            transaction,
            quote.ItemId,
            cancellationToken);
        if (product is null || !product.Enabled)
        {
            return await RejectSaleAsync(
                connection,
                transaction,
                reservation.OperationId,
                request,
                quote,
                "PRODUCT_DISABLED",
                cancellationToken);
        }

        var date = DateOnly.FromDateTime(now.UtcDateTime);
        var personalUsed = await ReadPersonalUsageAsync(
            connection,
            transaction,
            date,
            serverId,
            request.PlayerUuid,
            quote.ItemId,
            cancellationToken);
        var serverUsed = await ReadServerUsageAsync(
            connection,
            transaction,
            date,
            serverId,
            quote.ItemId,
            cancellationToken);
        if (personalUsed + quote.Quantity > product.PersonalDailyLimit ||
            serverUsed + quote.Quantity > product.ServerDailyLimit)
        {
            return await RejectSaleAsync(
                connection,
                transaction,
                reservation.OperationId,
                request,
                quote,
                "DAILY_LIMIT_EXCEEDED",
                cancellationToken);
        }

        await EnsureAccountAsync(
            connection,
            transaction,
            request.PlayerUuid,
            now,
            cancellationToken);
        var balance = await LockBalanceAsync(
            connection,
            transaction,
            request.PlayerUuid,
            cancellationToken);
        balance += quote.TotalAmount;
        await SetBalanceAsync(
            connection,
            transaction,
            request.PlayerUuid,
            balance,
            now,
            cancellationToken);
        await AddUsageAsync(
            connection,
            transaction,
            date,
            serverId,
            request.PlayerUuid,
            quote.ItemId,
            quote.Quantity,
            cancellationToken);
        await InsertEntryAsync(
            connection,
            transaction,
            reservation.OperationId,
            "system:sale-budget",
            -quote.TotalAmount,
            null,
            cancellationToken);
        await InsertEntryAsync(
            connection,
            transaction,
            reservation.OperationId,
            PlayerAccount(request.PlayerUuid),
            quote.TotalAmount,
            request.PlayerUuid,
            cancellationToken);
        var response = new EconomySaleCommitResponse(
            reservation.OperationId,
            "Applied",
            quote.QuoteId,
            request.PlayerUuid,
            quote.ItemId,
            quote.Quantity,
            quote.TotalAmount,
            balance);
        await CompleteOperationAsync(
            connection,
            transaction,
            reservation.OperationId,
            "Applied",
            response,
            cancellationToken);
        await CommitQuoteAsync(
            connection,
            transaction,
            quote.QuoteId,
            reservation.OperationId,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return response;
    }

    public async Task<IReadOnlyList<EconomyProductResponse>> ListProductsAsync(
        bool includeDisabled,
        CancellationToken cancellationToken)
    {
        var sql = BuildProductListSql(includeDisabled);
        var products = new List<EconomyProductResponse>();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            products.Add(ReadProduct(reader));
        }

        return products;
    }

    internal static string BuildProductListSql(bool includeDisabled)
    {
        var enabledFilter = includeDisabled ? string.Empty : "WHERE enabled";
        return $"""
            SELECT item_id, unit_price, personal_daily_limit, server_daily_limit,
                   enabled, updated_by_uuid, updated_by_name, updated_at,
                   shop_unit_price
            FROM launcher.economy_products
            {enabledFilter}
            ORDER BY item_id;
            """;
    }

    public async Task<IReadOnlyList<EconomyProductResponse>> ListShopProductsAsync(
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT item_id, unit_price, personal_daily_limit, server_daily_limit,
                   enabled, updated_by_uuid, updated_by_name, updated_at,
                   shop_unit_price
            FROM launcher.economy_products
            WHERE enabled AND shop_unit_price IS NOT NULL
            ORDER BY item_id;
            """;
        var products = new List<EconomyProductResponse>();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            products.Add(ReadProduct(reader));
        }

        return products;
    }

    public async Task<EconomyProductResponse> UpsertProductAsync(
        string itemId,
        EconomyProductUpsertRequest request,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var before = await ReadProductForUpdateAsync(
            connection,
            transaction,
            itemId,
            cancellationToken);
        if (before?.ShopUnitPrice is decimal shopUnitPrice &&
            request.UnitPrice >= shopUnitPrice)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new EconomyBuybackPriceConflictException();
        }

        const string sql = """
            INSERT INTO launcher.economy_products
                (item_id, unit_price, personal_daily_limit, server_daily_limit,
                 enabled, updated_by_uuid, updated_by_name, updated_at,
                 shop_unit_price)
            VALUES ($1, $2, $3, $4, true, $5, $6, $7, NULL)
            ON CONFLICT (item_id) DO UPDATE
            SET unit_price = EXCLUDED.unit_price,
                personal_daily_limit = EXCLUDED.personal_daily_limit,
                server_daily_limit = EXCLUDED.server_daily_limit,
                enabled = true,
                updated_by_uuid = EXCLUDED.updated_by_uuid,
                updated_by_name = EXCLUDED.updated_by_name,
                updated_at = EXCLUDED.updated_at
            RETURNING item_id, unit_price, personal_daily_limit,
                      server_daily_limit, enabled, updated_by_uuid,
                      updated_by_name, updated_at, shop_unit_price;
            """;
        EconomyProductResponse result;
        await using (var command = new NpgsqlCommand(sql, connection, transaction))
        {
            command.Parameters.AddWithValue(itemId);
            command.Parameters.AddWithValue(request.UnitPrice);
            command.Parameters.AddWithValue(request.PersonalDailyLimit);
            command.Parameters.AddWithValue(request.ServerDailyLimit);
            command.Parameters.AddWithValue(request.ActorUuid);
            command.Parameters.AddWithValue(request.ActorName.Trim());
            command.Parameters.AddWithValue(now);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            result = ReadProduct(reader);
        }

        await WriteProductAuditAsync(
            connection,
            transaction,
            itemId,
            "Upsert",
            request.ActorUuid,
            request.ActorName,
            before,
            result,
            now,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<EconomyProductMutationStatus> DisableProductAsync(
        string itemId,
        EconomyProductDisableRequest request,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var before = await ReadProductForUpdateAsync(
            connection,
            transaction,
            itemId,
            cancellationToken);
        if (before is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return EconomyProductMutationStatus.NotFound;
        }

        const string sql = """
            UPDATE launcher.economy_products
            SET enabled = false,
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
            Enabled = false,
            UpdatedByUuid = request.ActorUuid,
            UpdatedByName = request.ActorName.Trim(),
            UpdatedAt = now
        };
        await WriteProductAuditAsync(
            connection,
            transaction,
            itemId,
            "Disable",
            request.ActorUuid,
            request.ActorName,
            before,
            after,
            now,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return EconomyProductMutationStatus.Applied;
    }

    private static async Task<OperationReservation> ReserveOperationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string serverId,
        string idempotencyKey,
        string fingerprint,
        string kind,
        CancellationToken cancellationToken)
    {
        var operationId = Guid.NewGuid();
        const string insertSql = """
            INSERT INTO launcher.economy_operations
                (operation_id, server_id, idempotency_key, request_fingerprint,
                 operation_kind, status, response_json, created_at)
            VALUES ($1, $2, $3, $4, $5, 'Pending', '{}'::jsonb, $6)
            ON CONFLICT (server_id, idempotency_key) DO NOTHING
            RETURNING operation_id;
            """;
        await using (var insert = new NpgsqlCommand(insertSql, connection, transaction))
        {
            insert.Parameters.AddWithValue(operationId);
            insert.Parameters.AddWithValue(serverId);
            insert.Parameters.AddWithValue(idempotencyKey);
            insert.Parameters.AddWithValue(fingerprint);
            insert.Parameters.AddWithValue(kind);
            insert.Parameters.AddWithValue(DateTimeOffset.UtcNow);
            var inserted = await insert.ExecuteScalarAsync(cancellationToken);
            if (inserted is Guid)
            {
                return new OperationReservation(operationId, true, null);
            }
        }

        const string selectSql = """
            SELECT operation_id, request_fingerprint, operation_kind,
                   status, response_json::text
            FROM launcher.economy_operations
            WHERE server_id = $1 AND idempotency_key = $2;
            """;
        await using var select = new NpgsqlCommand(selectSql, connection, transaction);
        select.Parameters.AddWithValue(serverId);
        select.Parameters.AddWithValue(idempotencyKey);
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) ||
            !string.Equals(reader.GetString(1), fingerprint, StringComparison.Ordinal) ||
            !string.Equals(reader.GetString(2), kind, StringComparison.Ordinal) ||
            string.Equals(reader.GetString(3), "Pending", StringComparison.Ordinal))
        {
            throw new EconomyIdempotencyConflictException();
        }

        return new OperationReservation(reader.GetGuid(0), false, reader.GetString(4));
    }

    private static async Task CompleteOperationAsync<T>(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid operationId,
        string status,
        T response,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE launcher.economy_operations
            SET status = $2, response_json = $3
            WHERE operation_id = $1 AND status = 'Pending';
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(operationId);
        command.Parameters.AddWithValue(status);
        command.Parameters.AddWithValue(
            NpgsqlDbType.Jsonb,
            JsonSerializer.Serialize(response, JsonOptions));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsureAccountAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid playerUuid,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO launcher.economy_accounts (player_uuid, updated_at)
            VALUES ($1, $2)
            ON CONFLICT (player_uuid) DO NOTHING;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(playerUuid);
        command.Parameters.AddWithValue(now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<Dictionary<Guid, decimal>> LockBalancesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid first,
        Guid second,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, decimal>();
        foreach (var playerUuid in new[] { first, second }.Order())
        {
            result[playerUuid] = await LockBalanceAsync(
                connection,
                transaction,
                playerUuid,
                cancellationToken);
        }

        return result;
    }

    private static async Task<decimal> LockBalanceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid playerUuid,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT available_balance
            FROM launcher.economy_accounts
            WHERE player_uuid = $1
            FOR UPDATE;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(playerUuid);
        return (decimal)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("Economy account was not created."));
    }

    private static async Task SetBalanceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid playerUuid,
        decimal balance,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE launcher.economy_accounts
            SET available_balance = $2, updated_at = $3
            WHERE player_uuid = $1;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(playerUuid);
        command.Parameters.AddWithValue(balance);
        command.Parameters.AddWithValue(now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertEntryAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid operationId,
        string accountKey,
        decimal amount,
        Guid? playerUuid,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO launcher.economy_ledger_entries
                (operation_id, account_key, amount, player_uuid)
            VALUES ($1, $2, $3, $4);
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(operationId);
        command.Parameters.AddWithValue(accountKey);
        command.Parameters.AddWithValue(amount);
        command.Parameters.AddWithValue(
            NpgsqlDbType.Uuid,
            playerUuid is null ? DBNull.Value : playerUuid.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> ReadPersonalUsageAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        DateOnly date,
        string serverId,
        Guid playerUuid,
        string itemId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT COALESCE(quantity, 0)
            FROM launcher.economy_sale_usage
            WHERE usage_date = $1 AND server_id = $2
              AND player_uuid = $3 AND item_id = $4;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(date);
        command.Parameters.AddWithValue(serverId);
        command.Parameters.AddWithValue(playerUuid);
        command.Parameters.AddWithValue(itemId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken) ?? 0);
    }

    private static async Task<int> ReadServerUsageAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        DateOnly date,
        string serverId,
        string itemId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT COALESCE(sum(quantity), 0)::integer
            FROM launcher.economy_sale_usage
            WHERE usage_date = $1 AND server_id = $2 AND item_id = $3;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(date);
        command.Parameters.AddWithValue(serverId);
        command.Parameters.AddWithValue(itemId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken) ?? 0);
    }

    private static async Task AddUsageAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        DateOnly date,
        string serverId,
        Guid playerUuid,
        string itemId,
        int quantity,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO launcher.economy_sale_usage
                (usage_date, server_id, player_uuid, item_id, quantity)
            VALUES ($1, $2, $3, $4, $5)
            ON CONFLICT (usage_date, server_id, player_uuid, item_id)
            DO UPDATE SET quantity = launcher.economy_sale_usage.quantity
                                   + EXCLUDED.quantity;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(date);
        command.Parameters.AddWithValue(serverId);
        command.Parameters.AddWithValue(playerUuid);
        command.Parameters.AddWithValue(itemId);
        command.Parameters.AddWithValue(quantity);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<SaleQuote?> ReadQuoteForUpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid quoteId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT quote_id, server_id, player_uuid, item_id, quantity,
                   total_amount, status, expires_at
            FROM launcher.economy_sale_quotes
            WHERE quote_id = $1
            FOR UPDATE;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(quoteId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new SaleQuote(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetGuid(2),
                reader.GetString(3),
                reader.GetInt32(4),
                reader.GetDecimal(5),
                reader.GetString(6),
                reader.GetFieldValue<DateTimeOffset>(7))
            : null;
    }

    private static async Task<EconomyProductResponse?> ReadProductForUpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string itemId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT item_id, unit_price, personal_daily_limit, server_daily_limit,
                   enabled, updated_by_uuid, updated_by_name, updated_at,
                   shop_unit_price
            FROM launcher.economy_products
            WHERE item_id = $1
            FOR UPDATE;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(itemId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadProduct(reader) : null;
    }

    private static EconomyProductResponse ReadProduct(NpgsqlDataReader reader) => new(
        reader.GetString(0),
        reader.GetDecimal(1),
        reader.GetInt32(2),
        reader.GetInt32(3),
        reader.GetBoolean(4),
        reader.GetGuid(5),
        reader.GetString(6),
        reader.GetFieldValue<DateTimeOffset>(7),
        reader.IsDBNull(8) ? null : reader.GetDecimal(8));

    private static async Task<EconomySaleCommitResponse> RejectSaleAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid operationId,
        EconomySaleCommitRequest request,
        SaleQuote? quote,
        string failureCode,
        CancellationToken cancellationToken)
    {
        var response = new EconomySaleCommitResponse(
            operationId,
            "Rejected",
            request.QuoteId,
            request.PlayerUuid,
            quote?.ItemId ?? string.Empty,
            quote?.Quantity ?? 0,
            quote?.TotalAmount ?? 0m,
            0m,
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

    private static async Task SetQuoteExpiredAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid quoteId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "UPDATE launcher.economy_sale_quotes SET status = 'Expired' WHERE quote_id = $1;",
            connection,
            transaction);
        command.Parameters.AddWithValue(quoteId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task CommitQuoteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid quoteId,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE launcher.economy_sale_quotes
            SET status = 'Committed', committed_operation_id = $2
            WHERE quote_id = $1 AND status = 'Open';
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(quoteId);
        command.Parameters.AddWithValue(operationId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task WriteProductAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string itemId,
        string action,
        Guid actorUuid,
        string actorName,
        EconomyProductResponse? before,
        EconomyProductResponse? after,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO launcher.economy_product_audit
                (item_id, action, actor_uuid, actor_name,
                 before_json, after_json, created_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7);
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(itemId);
        command.Parameters.AddWithValue(action);
        command.Parameters.AddWithValue(actorUuid);
        command.Parameters.AddWithValue(actorName.Trim());
        command.Parameters.AddWithValue(
            NpgsqlDbType.Jsonb,
            before is null ? DBNull.Value : JsonSerializer.Serialize(before, JsonOptions));
        command.Parameters.AddWithValue(
            NpgsqlDbType.Jsonb,
            after is null ? DBNull.Value : JsonSerializer.Serialize(after, JsonOptions));
        command.Parameters.AddWithValue(now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, JsonOptions)
        ?? throw new InvalidOperationException("Stored economy response is invalid.");

    private static string PlayerAccount(Guid playerUuid) =>
        $"player:{playerUuid:D}";

    private sealed record OperationReservation(
        Guid OperationId,
        bool Created,
        string? ResponseJson);

    private sealed record SaleQuote(
        Guid QuoteId,
        string ServerId,
        Guid PlayerUuid,
        string ItemId,
        int Quantity,
        decimal TotalAmount,
        string Status,
        DateTimeOffset ExpiresAt);
}

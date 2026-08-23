using Hechao.Api.Database;
using Hechao.Api.Economy;
using Npgsql;
using Xunit;

namespace Hechao.Api.Tests;

public sealed class EconomyPostgresIntegrationTests
{
    [PostgresFact]
    public async Task MigrationAndRepository_SupportModdedProductsAndBalancedTransactions()
    {
        var connectionString =
            Environment.GetEnvironmentVariable(PostgresFactAttribute.ConnectionVariable)!;
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        Assert.StartsWith(
            "hechao_economy_test_",
            builder.Database,
            StringComparison.Ordinal);

        await using var dataSource = NpgsqlDataSource.Create(builder.ConnectionString);
        await using (var connection = await dataSource.OpenConnectionAsync())
        {
            await using var reset = new NpgsqlCommand(
                "DROP SCHEMA IF EXISTS launcher CASCADE; CREATE SCHEMA launcher;",
                connection);
            await reset.ExecuteNonQueryAsync();

            foreach (var resourceName in new[]
            {
                "Hechao.Api.Database.Migrations.031_economy_ledger.sql",
                "Hechao.Api.Database.Migrations.032_economy_dashboard_indexes.sql",
                "Hechao.Api.Database.Migrations.033_economy_item_history_index.sql",
                "Hechao.Api.Database.Migrations.034_economy_player_market.sql",
                "Hechao.Api.Database.Migrations.035_economy_server_shop.sql"
            })
            {
                await using var stream = typeof(DatabaseMigrator).Assembly
                    .GetManifestResourceStream(resourceName);
                Assert.NotNull(stream);
                using var reader = new StreamReader(stream);
                var sql = await reader.ReadToEndAsync();
                await using var migration = new NpgsqlCommand(sql, connection);
                await migration.ExecuteNonQueryAsync();
            }
            await using var dashboardSupport = new NpgsqlCommand(
                """
                CREATE TABLE launcher.servers (
                    id text PRIMARY KEY,
                    display_name text NOT NULL
                );
                CREATE TABLE launcher.minecraft_identities (
                    minecraft_uuid uuid PRIMARY KEY,
                    minecraft_name text NOT NULL
                );
                INSERT INTO launcher.servers (id, display_name)
                VALUES ('activity-survival', '活动生存服');
                """,
                connection);
            await dashboardSupport.ExecuteNonQueryAsync();
        }

        var repository = new EconomyRepository(dataSource, TimeProvider.System);
        var actorUuid = Guid.NewGuid();
        var playerUuid = Guid.NewGuid();
        var recipientUuid = Guid.NewGuid();
        var vanilla = await repository.UpsertProductAsync(
            "minecraft:iron_ingot",
            new EconomyProductUpsertRequest(2.50m, 64, 640, actorUuid, "integration-admin"),
            CancellationToken.None);
        var modded = await repository.UpsertProductAsync(
            "create:brass_ingot",
            new EconomyProductUpsertRequest(5.00m, 64, 640, actorUuid, "integration-admin"),
            CancellationToken.None);

        Assert.Equal("minecraft:iron_ingot", vanilla.ItemId);
        Assert.Equal("create:brass_ingot", modded.ItemId);

        var enabledProducts = await repository.ListProductsAsync(
            includeDisabled: false,
            CancellationToken.None);
        Assert.Equal(
            ["create:brass_ingot", "minecraft:iron_ingot"],
            enabledProducts.Select(product => product.ItemId));

        var disabled = await repository.DisableProductAsync(
            vanilla.ItemId,
            new EconomyProductDisableRequest(actorUuid, "integration-admin"),
            CancellationToken.None);
        Assert.Equal(EconomyProductMutationStatus.Applied, disabled);

        var activeProducts = await repository.ListProductsAsync(
            includeDisabled: false,
            CancellationToken.None);
        Assert.Collection(
            activeProducts,
            product => Assert.Equal("create:brass_ingot", product.ItemId));

        var allProducts = await repository.ListProductsAsync(
            includeDisabled: true,
            CancellationToken.None);
        Assert.Equal(2, allProducts.Count);
        Assert.False(allProducts.Single(product =>
            product.ItemId == "minecraft:iron_ingot").Enabled);

        var cappedProduct = await repository.UpsertProductAsync(
            "minecraft:apple",
            new EconomyProductUpsertRequest(2.00m, 32, 640, actorUuid, "integration-admin"),
            CancellationToken.None);
        var partialQuote = await repository.CreateSaleQuoteAsync(
            "activity-survival",
            new EconomySaleQuoteRequest(playerUuid, cappedProduct.ItemId, 64),
            TimeSpan.FromSeconds(30),
            CancellationToken.None);
        Assert.Equal(EconomyQuoteStatus.Created, partialQuote.Status);
        Assert.Equal(32, partialQuote.Quote!.Quantity);
        Assert.Equal(64.00m, partialQuote.Quote.TotalAmount);
        Assert.Equal(0, partialQuote.Quote.PersonalRemaining);
        Assert.Equal(608, partialQuote.Quote.ServerRemaining);

        var quote = await repository.CreateSaleQuoteAsync(
            "activity-survival",
            new EconomySaleQuoteRequest(playerUuid, modded.ItemId, 10),
            TimeSpan.FromSeconds(30),
            CancellationToken.None);
        Assert.Equal(EconomyQuoteStatus.Created, quote.Status);

        var saleRequest = new EconomySaleCommitRequest(
            "sale:integration-0001",
            quote.Quote!.QuoteId,
            playerUuid);
        var sale = await repository.CommitSaleAsync(
            "activity-survival",
            saleRequest,
            CancellationToken.None);
        var repeatedSale = await repository.CommitSaleAsync(
            "activity-survival",
            saleRequest,
            CancellationToken.None);
        Assert.Equal("Applied", sale.Status);
        Assert.Equal(sale.OperationId, repeatedSale.OperationId);
        Assert.Equal(50.00m, sale.Balance);

        var transferRequest = new EconomyTransferRequest(
            "pay:integration-0001",
            playerUuid,
            recipientUuid,
            12.50m,
            "integration transfer");
        var transfer = await repository.TransferAsync(
            "activity-survival",
            transferRequest,
            CancellationToken.None);
        var repeatedTransfer = await repository.TransferAsync(
            "activity-survival",
            transferRequest,
            CancellationToken.None);
        Assert.Equal("Applied", transfer.Status);
        Assert.Equal(transfer.OperationId, repeatedTransfer.OperationId);
        Assert.Equal(37.50m, transfer.SenderBalance);
        Assert.Equal(12.50m, transfer.RecipientBalance);

        var repriced = await repository.UpsertProductAsync(
            modded.ItemId,
            new EconomyProductUpsertRequest(7.00m, 64, 640, actorUuid, "integration-admin"),
            CancellationToken.None);
        Assert.Equal(7.00m, repriced.UnitPrice);
        var secondQuote = await repository.CreateSaleQuoteAsync(
            "activity-survival",
            new EconomySaleQuoteRequest(recipientUuid, modded.ItemId, 5),
            TimeSpan.FromSeconds(30),
            CancellationToken.None);
        Assert.Equal(EconomyQuoteStatus.Created, secondQuote.Status);
        var secondSale = await repository.CommitSaleAsync(
            "activity-survival",
            new EconomySaleCommitRequest(
                "sale:integration-0002",
                secondQuote.Quote!.QuoteId,
                recipientUuid),
            CancellationToken.None);
        Assert.Equal("Applied", secondSale.Status);
        Assert.Equal(47.50m, secondSale.Balance);

        await using (var connection = await dataSource.OpenConnectionAsync())
        await using (var identities = new NpgsqlCommand(
            """
            INSERT INTO launcher.minecraft_identities (minecraft_uuid, minecraft_name)
            VALUES ($1, 'IntegrationSeller'), ($2, 'IntegrationBuyer');
            """,
            connection))
        {
            identities.Parameters.AddWithValue(playerUuid);
            identities.Parameters.AddWithValue(recipientUuid);
            await identities.ExecuteNonQueryAsync();
        }

        await using (var connection = await dataSource.OpenConnectionAsync())
        await using (var operationTimes = new NpgsqlCommand(
            """
            UPDATE launcher.economy_operations
            SET created_at = CASE operation_id
                WHEN $1 THEN now() - interval '2 hours'
                WHEN $2 THEN now() - interval '1 hour'
                ELSE created_at
            END
            WHERE operation_id IN ($1, $2);
            """,
            connection))
        {
            operationTimes.Parameters.AddWithValue(sale.OperationId);
            operationTimes.Parameters.AddWithValue(secondSale.OperationId);
            Assert.Equal(2, await operationTimes.ExecuteNonQueryAsync());
        }

        var dashboard = new AdminEconomyRepository(dataSource, TimeProvider.System);
        var overview = await dashboard.GetOverviewAsync(
            24,
            null,
            CancellationToken.None);
        Assert.Equal(85.00m, overview.Summary.TotalSupply);
        Assert.Equal(85.00m, overview.Summary.WindowIssued);
        Assert.Equal(12.50m, overview.Summary.TransferVolume);
        Assert.Equal(2, overview.Summary.ActivePlayers);
        Assert.Equal(3, overview.Summary.OperationCount);
        Assert.Equal(2, overview.Wealth.FundedAccounts);
        Assert.Equal(42.50m, overview.Wealth.AverageBalance);
        Assert.Equal(42.50m, overview.Wealth.MedianBalance);
        Assert.Equal(46.50m, overview.Wealth.P90Balance);
        Assert.Equal(
            decimal.Round(47.50m / 85.00m, 20),
            overview.Wealth.TopTenPercentShare);
        Assert.Equal(85.00m, overview.Series[^1].TotalSupply);
        Assert.Equal(85.00m, overview.Series.Sum(point => point.IssuedAmount));
        Assert.Contains(overview.TopBalances, player =>
            player.PlayerName == "IntegrationSeller" && player.Balance == 37.50m);
        Assert.Collection(
            overview.Products,
            product =>
            {
                Assert.Equal("create:brass_ingot", product.ItemId);
                Assert.Equal(15, product.Quantity);
                Assert.Equal(85.00m, product.Amount);
            });
        Assert.Collection(
            overview.ServerVolumes,
            server =>
            {
                Assert.Equal("activity-survival", server.ServerId);
                Assert.Equal("活动生存服", server.DisplayName);
                Assert.Equal(85.00m, server.SaleVolume);
                Assert.Equal(12.50m, server.TransferVolume);
            });
        Assert.Contains(
            overview.Items,
            item => item.ItemId == modded.ItemId &&
                    item.CurrentUnitPrice == 7.00m &&
                    item.Enabled);

        var itemHistory = await dashboard.GetItemHistoryAsync(
            24,
            modded.ItemId,
            null,
            CancellationToken.None);
        Assert.NotNull(itemHistory);
        Assert.Equal(7.00m, itemHistory.CurrentUnitPrice);
        Assert.Equal(5.00m, itemHistory.Summary.OpenUnitPrice);
        Assert.Equal(7.00m, itemHistory.Summary.CloseUnitPrice);
        Assert.Equal(5.00m, itemHistory.Summary.LowUnitPrice);
        Assert.Equal(7.00m, itemHistory.Summary.HighUnitPrice);
        Assert.Equal(0.40m, itemHistory.Summary.PriceChangeRate);
        Assert.Equal(15, itemHistory.Summary.Quantity);
        Assert.Equal(85.00m, itemHistory.Summary.Amount);
        Assert.Equal(2, itemHistory.Summary.Sellers);
        Assert.Equal(2, itemHistory.Summary.Transactions);
        Assert.Equal(15, itemHistory.Series.Sum(point => point.Quantity));
        Assert.Equal(85.00m, itemHistory.Series.Sum(point => point.Amount));
        Assert.Contains(itemHistory.Series, point =>
            point.OpenUnitPrice == 5.00m &&
            point.CloseUnitPrice == 5.00m &&
            point.LowUnitPrice == 5.00m &&
            point.HighUnitPrice == 5.00m);
        Assert.Contains(itemHistory.Series, point =>
            point.OpenUnitPrice == 7.00m &&
            point.CloseUnitPrice == 7.00m &&
            point.LowUnitPrice == 7.00m &&
            point.HighUnitPrice == 7.00m);
        Assert.Null(await dashboard.GetItemHistoryAsync(
            24,
            "minecraft:missing_item",
            null,
            CancellationToken.None));

        var shopProduct = await repository.UpsertShopProductAsync(
            modded.ItemId,
            new EconomyShopProductUpsertRequest(20.00m, actorUuid, "integration-admin"),
            CancellationToken.None);
        Assert.Equal(20.00m, shopProduct!.ShopUnitPrice);
        await Assert.ThrowsAsync<EconomyBuybackPriceConflictException>(() =>
            repository.UpsertProductAsync(
                modded.ItemId,
                new EconomyProductUpsertRequest(
                    20.00m,
                    100,
                    1000,
                    actorUuid,
                    "integration-admin"),
                CancellationToken.None));
        await Assert.ThrowsAsync<EconomyShopPriceConflictException>(() =>
            repository.UpsertShopProductAsync(
                modded.ItemId,
                new EconomyShopProductUpsertRequest(7.00m, actorUuid, "integration-admin"),
                CancellationToken.None));
        Assert.Single(await repository.ListShopProductsAsync(CancellationToken.None));
        var shopRequest = new EconomyShopPurchaseRequest(
            "shop-buy:integration-0001",
            playerUuid,
            modded.ItemId,
            1);
        var shopPurchase = await repository.PurchaseShopProductAsync(
            "activity-survival",
            shopRequest,
            CancellationToken.None);
        var repeatedShopPurchase = await repository.PurchaseShopProductAsync(
            "activity-survival",
            shopRequest,
            CancellationToken.None);
        Assert.Equal("Applied", shopPurchase.Status);
        Assert.Equal(shopPurchase.OperationId, repeatedShopPurchase.OperationId);
        Assert.Equal(17.50m, shopPurchase.Balance);
        Assert.NotNull(shopPurchase.DeliveryId);
        var shopDeliveries = await repository.ListShopDeliveriesAsync(
            "activity-survival",
            playerUuid,
            CancellationToken.None);
        Assert.Single(shopDeliveries);
        var shopClaimRequest = new EconomyShopClaimRequest(
            "shop-claim:integration-0001",
            shopPurchase.DeliveryId!.Value,
            playerUuid);
        var shopClaim = await repository.ClaimShopDeliveryAsync(
            "activity-survival",
            shopClaimRequest,
            CancellationToken.None);
        var repeatedShopClaim = await repository.ClaimShopDeliveryAsync(
            "activity-survival",
            shopClaimRequest,
            CancellationToken.None);
        Assert.Equal("Applied", shopClaim.Status);
        Assert.Equal(shopClaim.OperationId, repeatedShopClaim.OperationId);
        Assert.Empty(await repository.ListShopDeliveriesAsync(
            "activity-survival",
            playerUuid,
            CancellationToken.None));
        Assert.Empty(await repository.ListShopDeliveriesAsync(
            "another-server",
            playerUuid,
            CancellationToken.None));

        var insufficientShopPurchase = await repository.PurchaseShopProductAsync(
            "activity-survival",
            new EconomyShopPurchaseRequest(
                "shop-buy:integration-insufficient",
                playerUuid,
                modded.ItemId,
                1),
            CancellationToken.None);
        Assert.Equal("Rejected", insufficientShopPurchase.Status);
        Assert.Equal("INSUFFICIENT_FUNDS", insufficientShopPurchase.FailureCode);
        Assert.Equal(17.50m, insufficientShopPurchase.Balance);
        Assert.Null(insufficientShopPurchase.DeliveryId);

        var listingRequest = new EconomyMarketCreateListingRequest(
            "market-list:integration-0001",
            playerUuid,
            "IntegrationSeller",
            modded.ItemId,
            4,
            20.00m);
        var listing = await repository.CreateMarketListingAsync(
            "activity-survival",
            listingRequest,
            0.01m,
            1.00m,
            5,
            TimeSpan.FromHours(24),
            CancellationToken.None);
        var repeatedListing = await repository.CreateMarketListingAsync(
            "activity-survival",
            listingRequest,
            0.01m,
            1.00m,
            5,
            TimeSpan.FromHours(24),
            CancellationToken.None);
        Assert.Equal("Applied", listing.Status);
        Assert.Equal(1.00m, listing.ListingFee);
        Assert.Equal(listing.OperationId, repeatedListing.OperationId);
        Assert.Equal(listing.Listing!.ListingId, repeatedListing.Listing!.ListingId);

        var search = await repository.ListMarketListingsAsync(
            "activity-survival",
            "IntegrationSeller",
            100,
            CancellationToken.None);
        Assert.Collection(search, result =>
        {
            Assert.Equal(modded.ItemId, result.ItemId);
            Assert.Equal(4, result.Quantity);
            Assert.Equal(5.0000m, result.UnitPrice);
        });
        Assert.Empty(await repository.ListMarketListingsAsync(
            "another-server",
            null,
            100,
            CancellationToken.None));

        var purchaseRequest = new EconomyMarketPurchaseRequest(
            "market-buy:integration-0001",
            listing.Listing.ListingId,
            recipientUuid,
            "IntegrationBuyer");
        var purchase = await repository.PurchaseMarketListingAsync(
            "activity-survival",
            purchaseRequest,
            0.03m,
            CancellationToken.None);
        var repeatedPurchase = await repository.PurchaseMarketListingAsync(
            "activity-survival",
            purchaseRequest,
            0.03m,
            CancellationToken.None);
        Assert.Equal("Applied", purchase.Status);
        Assert.Equal(purchase.OperationId, repeatedPurchase.OperationId);
        Assert.Equal(19.40m, purchase.SellerProceeds);
        Assert.Equal(0.60m, purchase.TransactionTax);
        Assert.Equal(27.50m, purchase.BuyerBalance);

        var buyerDeliveries = await repository.ListMarketDeliveriesAsync(
            "activity-survival",
            recipientUuid,
            CancellationToken.None);
        Assert.Collection(buyerDeliveries, delivery =>
        {
            Assert.Equal(purchase.DeliveryId, delivery.DeliveryId);
            Assert.Equal("Purchase", delivery.Reason);
        });
        var claimRequest = new EconomyMarketClaimRequest(
            "market-claim:integration-0001",
            purchase.DeliveryId!.Value,
            recipientUuid);
        var claim = await repository.ClaimMarketDeliveryAsync(
            "activity-survival",
            claimRequest,
            CancellationToken.None);
        var repeatedClaim = await repository.ClaimMarketDeliveryAsync(
            "activity-survival",
            claimRequest,
            CancellationToken.None);
        Assert.Equal("Applied", claim.Status);
        Assert.Equal(claim.OperationId, repeatedClaim.OperationId);
        Assert.Empty(await repository.ListMarketDeliveriesAsync(
            "activity-survival",
            recipientUuid,
            CancellationToken.None));

        var cancellable = await repository.CreateMarketListingAsync(
            "activity-survival",
            listingRequest with
            {
                IdempotencyKey = "market-list:integration-0002",
                Quantity = 2,
                TotalPrice = 10.00m
            },
            0.01m,
            1.00m,
            5,
            TimeSpan.FromHours(24),
            CancellationToken.None);
        var cancellation = await repository.CancelMarketListingAsync(
            "activity-survival",
            new EconomyMarketCancelRequest(
                "market-cancel:integration-0001",
                cancellable.Listing!.ListingId,
                playerUuid),
            CancellationToken.None);
        Assert.Equal("Applied", cancellation.Status);
        Assert.Contains(
            await repository.ListMarketDeliveriesAsync(
                "activity-survival",
                playerUuid,
                CancellationToken.None),
            delivery => delivery.DeliveryId == cancellation.DeliveryId &&
                        delivery.Reason == "Cancelled");

        await using var verification = await dataSource.OpenConnectionAsync();
        await using (var balanced = new NpgsqlCommand(
            """
            SELECT operation_id, sum(amount)
            FROM launcher.economy_ledger_entries
            GROUP BY operation_id
            HAVING sum(amount) <> 0;
            """,
            verification))
        await using (var result = await balanced.ExecuteReaderAsync())
        {
            Assert.False(await result.ReadAsync());
        }

        await using var invalidProduct = new NpgsqlCommand(
            """
            INSERT INTO launcher.economy_products
                (item_id, unit_price, personal_daily_limit, server_daily_limit,
                 updated_by_uuid, updated_by_name, updated_at)
            VALUES ('Invalid Namespace:item', 1, 1, 1, $1, 'integration-admin', now());
            """,
            verification);
        invalidProduct.Parameters.AddWithValue(actorUuid);
        var invalid = await Assert.ThrowsAsync<PostgresException>(
            () => invalidProduct.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, invalid.SqlState);

        await using var unbalancedTransaction =
            await verification.BeginTransactionAsync();
        await using (var deleteEntry = new NpgsqlCommand(
            """
            DELETE FROM launcher.economy_ledger_entries
            WHERE operation_id = $1 AND amount < 0;
            """,
            verification,
            unbalancedTransaction))
        {
            deleteEntry.Parameters.AddWithValue(transfer.OperationId);
            Assert.Equal(1, await deleteEntry.ExecuteNonQueryAsync());
        }
        var unbalanced = await Assert.ThrowsAsync<PostgresException>(
            () => unbalancedTransaction.CommitAsync());
        Assert.Equal(PostgresErrorCodes.RaiseException, unbalanced.SqlState);
    }
}

public sealed class PostgresFactAttribute : FactAttribute
{
    public const string ConnectionVariable = "HECHAO_ECONOMY_TEST_DATABASE";

    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionVariable)))
        {
            Skip = $"Set {ConnectionVariable} to an isolated PostgreSQL database.";
        }
    }
}

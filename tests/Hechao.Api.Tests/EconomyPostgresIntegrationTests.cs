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

            const string resourceName =
                "Hechao.Api.Database.Migrations.031_economy_ledger.sql";
            await using var stream = typeof(DatabaseMigrator).Assembly
                .GetManifestResourceStream(resourceName);
            Assert.NotNull(stream);
            using var reader = new StreamReader(stream);
            var sql = await reader.ReadToEndAsync();
            await using var migration = new NpgsqlCommand(sql, connection);
            await migration.ExecuteNonQueryAsync();
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

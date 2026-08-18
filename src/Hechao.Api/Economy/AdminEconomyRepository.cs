using System.Data;
using Npgsql;
using NpgsqlTypes;

namespace Hechao.Api.Economy;

public sealed class AdminEconomyRepository(
    NpgsqlDataSource dataSource,
    TimeProvider timeProvider)
{
    internal const string WindowMetricsSql = """
        SELECT
            COALESCE(sum(le.amount) FILTER (
                WHERE o.operation_kind = 'Sale'
                  AND le.player_uuid IS NOT NULL
            ), 0)::numeric,
            COALESCE(sum(le.amount) FILTER (
                WHERE o.operation_kind = 'Transfer'
                  AND le.player_uuid IS NOT NULL
                  AND le.amount > 0
            ), 0)::numeric,
            count(DISTINCT le.player_uuid)::bigint,
            count(DISTINCT o.operation_id)::bigint
        FROM launcher.economy_operations o
        JOIN launcher.economy_ledger_entries le
          ON le.operation_id = o.operation_id
        WHERE o.status = 'Applied'
          AND o.created_at >= $1
          AND o.created_at < $2
          AND ($3::text IS NULL OR o.server_id = $3);
        """;

    internal const string ServerVolumesSql = """
        SELECT
            o.server_id,
            COALESCE(s.display_name, o.server_id),
            COALESCE(sum(le.amount) FILTER (
                WHERE o.operation_kind = 'Sale'
                  AND le.player_uuid IS NOT NULL
            ), 0)::numeric,
            COALESCE(sum(le.amount) FILTER (
                WHERE o.operation_kind = 'Transfer'
                  AND le.player_uuid IS NOT NULL
                  AND le.amount > 0
            ), 0)::numeric,
            count(DISTINCT le.player_uuid)::bigint,
            count(DISTINCT o.operation_id)::bigint
        FROM launcher.economy_operations o
        JOIN launcher.economy_ledger_entries le
          ON le.operation_id = o.operation_id
        LEFT JOIN launcher.servers s ON s.id = o.server_id
        WHERE o.status = 'Applied'
          AND o.created_at >= $1
          AND o.created_at < $2
        GROUP BY o.server_id, s.display_name
        ORDER BY
            COALESCE(sum(le.amount) FILTER (
                WHERE le.player_uuid IS NOT NULL AND le.amount > 0
            ), 0) DESC,
            o.server_id;
        """;

    internal const string ItemOptionsSql = """
        WITH item_ids AS (
            SELECT item_id FROM launcher.economy_products
            UNION
            SELECT item_id FROM launcher.economy_sale_quotes
        )
        SELECT
            item.item_id,
            product.unit_price AS current_unit_price,
            COALESCE(product.enabled, false) AS enabled
        FROM item_ids item
        LEFT JOIN launcher.economy_products product
          ON product.item_id = item.item_id
        ORDER BY COALESCE(product.enabled, false) DESC, item.item_id;
        """;

    public async Task<AdminEconomyOverview> GetOverviewAsync(
        int hours,
        string? serverId,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.RepeatableRead,
            cancellationToken);
        await EstablishSnapshotAsync(connection, transaction, cancellationToken);
        var window = AdminEconomyWindow.Create(hours, timeProvider.GetUtcNow());

        var wealth = await ReadWealthAsync(connection, transaction, cancellationToken);
        var metrics = await ReadWindowMetricsAsync(
            connection,
            transaction,
            window,
            serverId,
            cancellationToken);
        var series = await ReadSeriesAsync(
            connection,
            transaction,
            window,
            serverId,
            wealth.TotalSupply,
            cancellationToken);
        var topBalances = await ReadTopBalancesAsync(
            connection,
            transaction,
            wealth.TotalSupply,
            cancellationToken);
        var products = await ReadProductsAsync(
            connection,
            transaction,
            window,
            serverId,
            cancellationToken);
        var serverVolumes = await ReadServerVolumesAsync(
            connection,
            transaction,
            window,
            cancellationToken);
        var servers = await ReadServerOptionsAsync(
            connection,
            transaction,
            cancellationToken);
        var items = await ReadItemOptionsAsync(
            connection,
            transaction,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return new AdminEconomyOverview(
            window.From,
            window.To,
            window.Hours,
            serverId,
            servers,
            items,
            new AdminEconomySummary(
                wealth.TotalSupply,
                metrics.WindowIssued,
                metrics.TransferVolume,
                metrics.ActivePlayers,
                metrics.OperationCount),
            new AdminEconomyWealthSummary(
                wealth.FundedAccounts,
                wealth.AverageBalance,
                wealth.MedianBalance,
                wealth.P90Balance,
                wealth.TopTenPercentShare),
            series,
            topBalances,
            products,
            serverVolumes);
    }

    public async Task<AdminEconomyItemHistory?> GetItemHistoryAsync(
        int hours,
        string itemId,
        string? serverId,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.RepeatableRead,
            cancellationToken);
        await EstablishSnapshotAsync(connection, transaction, cancellationToken);
        var window = AdminEconomyWindow.Create(hours, timeProvider.GetUtcNow());
        var item = await ReadItemOptionAsync(
            connection,
            transaction,
            itemId,
            cancellationToken);
        if (item is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        var summary = await ReadItemSummaryAsync(
            connection,
            transaction,
            window,
            serverId,
            itemId,
            cancellationToken);
        var series = await ReadItemSeriesAsync(
            connection,
            transaction,
            window,
            serverId,
            itemId,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return new AdminEconomyItemHistory(
            window.From,
            window.To,
            window.Hours,
            serverId,
            item.ItemId,
            item.CurrentUnitPrice,
            item.Enabled,
            summary,
            series);
    }

    private static async Task<WealthSnapshot> ReadWealthAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        const string sql = """
            WITH balances AS (
                SELECT player_uuid, available_balance + frozen_balance AS balance
                FROM launcher.economy_accounts
                WHERE available_balance + frozen_balance > 0
            ), ranked AS (
                SELECT
                    balance,
                    row_number() OVER (ORDER BY balance DESC) AS rank_number,
                    count(*) OVER () AS account_count,
                    sum(balance) OVER () AS total_balance
                FROM balances
            )
            SELECT
                COALESCE(max(total_balance), 0)::numeric,
                COALESCE(max(account_count), 0)::bigint,
                COALESCE(avg(balance), 0)::numeric,
                COALESCE(percentile_cont(0.5) WITHIN GROUP (ORDER BY balance), 0)::numeric,
                COALESCE(percentile_cont(0.9) WITHIN GROUP (ORDER BY balance), 0)::numeric,
                COALESCE(
                    sum(balance) FILTER (
                        WHERE rank_number <= GREATEST(
                            1,
                            CEIL(account_count * 0.1)::bigint
                        )
                    ) / NULLIF(max(total_balance), 0),
                    0
                )::numeric
            FROM ranked;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new WealthSnapshot(
            reader.GetDecimal(0),
            reader.GetInt64(1),
            reader.GetDecimal(2),
            reader.GetDecimal(3),
            reader.GetDecimal(4),
            reader.GetDecimal(5));
    }

    private static async Task EstablishSnapshotAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT 1;", connection, transaction);
        await command.ExecuteScalarAsync(cancellationToken);
    }

    private static async Task<WindowMetrics> ReadWindowMetricsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AdminEconomyWindow window,
        string? serverId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(WindowMetricsSql, connection, transaction);
        AddWindowParameters(command, window, serverId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new WindowMetrics(
            reader.GetDecimal(0),
            reader.GetDecimal(1),
            reader.GetInt64(2),
            reader.GetInt64(3));
    }

    private static async Task<IReadOnlyList<AdminEconomySeriesPoint>> ReadSeriesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AdminEconomyWindow window,
        string? serverId,
        decimal currentSupply,
        CancellationToken cancellationToken)
    {
        var sql = BuildSeriesSql(window.BucketSize);
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        AddWindowParameters(command, window, serverId);
        var deltas = new Dictionary<DateTimeOffset, SeriesDelta>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                deltas[reader.GetFieldValue<DateTimeOffset>(0)] = new SeriesDelta(
                    reader.GetDecimal(1),
                    reader.GetDecimal(2));
            }
        }

        var supply = currentSupply - deltas.Values.Sum(item => item.GlobalIssued);
        var result = new List<AdminEconomySeriesPoint>(window.BucketCount);
        foreach (var bucket in window.Buckets())
        {
            var delta = deltas.GetValueOrDefault(bucket);
            supply += delta?.GlobalIssued ?? 0m;
            result.Add(new AdminEconomySeriesPoint(
                bucket,
                supply,
                delta?.FilteredIssued ?? 0m));
        }

        return result;
    }

    internal static string BuildSeriesSql(TimeSpan bucketSize)
    {
        var unit = bucketSize == TimeSpan.FromHours(1) ? "hour" : "day";
        return $$"""
            SELECT
                date_trunc('{{unit}}', o.created_at AT TIME ZONE 'UTC') AT TIME ZONE 'UTC',
                COALESCE(sum(le.amount) FILTER (
                    WHERE o.operation_kind = 'Sale'
                      AND le.player_uuid IS NOT NULL
                ), 0)::numeric,
                COALESCE(sum(le.amount) FILTER (
                    WHERE o.operation_kind = 'Sale'
                      AND le.player_uuid IS NOT NULL
                      AND ($3::text IS NULL OR o.server_id = $3)
                ), 0)::numeric
            FROM launcher.economy_operations o
            JOIN launcher.economy_ledger_entries le
              ON le.operation_id = o.operation_id
            WHERE o.status = 'Applied'
              AND o.created_at >= $1
              AND o.created_at < $2
            GROUP BY 1
            ORDER BY 1;
            """;
    }

    private static async Task<IReadOnlyList<AdminEconomyPlayerBalance>> ReadTopBalancesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        decimal totalSupply,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                a.player_uuid,
                i.minecraft_name,
                a.available_balance + a.frozen_balance AS balance
            FROM launcher.economy_accounts a
            LEFT JOIN launcher.minecraft_identities i
              ON i.minecraft_uuid = a.player_uuid
            WHERE a.available_balance + a.frozen_balance > 0
            ORDER BY balance DESC, a.player_uuid
            LIMIT 10;
            """;
        var result = new List<AdminEconomyPlayerBalance>();
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var balance = reader.GetDecimal(2);
            result.Add(new AdminEconomyPlayerBalance(
                reader.GetGuid(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                balance,
                totalSupply > 0 ? balance / totalSupply : 0m));
        }

        return result;
    }

    private static async Task<IReadOnlyList<AdminEconomyProductVolume>> ReadProductsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AdminEconomyWindow window,
        string? serverId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                q.item_id,
                sum(q.quantity)::bigint,
                sum(q.total_amount)::numeric,
                count(DISTINCT q.player_uuid)::bigint
            FROM launcher.economy_sale_quotes q
            JOIN launcher.economy_operations o
              ON o.operation_id = q.committed_operation_id
            WHERE q.status = 'Committed'
              AND o.status = 'Applied'
              AND o.created_at >= $1
              AND o.created_at < $2
              AND ($3::text IS NULL OR o.server_id = $3)
            GROUP BY q.item_id
            ORDER BY sum(q.total_amount) DESC, q.item_id
            LIMIT 10;
            """;
        var result = new List<AdminEconomyProductVolume>();
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        AddWindowParameters(command, window, serverId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new AdminEconomyProductVolume(
                reader.GetString(0),
                reader.GetInt64(1),
                reader.GetDecimal(2),
                reader.GetInt64(3)));
        }

        return result;
    }

    private static async Task<IReadOnlyList<AdminEconomyServerVolume>> ReadServerVolumesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AdminEconomyWindow window,
        CancellationToken cancellationToken)
    {
        var result = new List<AdminEconomyServerVolume>();
        await using var command = new NpgsqlCommand(ServerVolumesSql, connection, transaction);
        command.Parameters.AddWithValue(window.From);
        command.Parameters.AddWithValue(window.To);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new AdminEconomyServerVolume(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetDecimal(2),
                reader.GetDecimal(3),
                reader.GetInt64(4),
                reader.GetInt64(5)));
        }

        return result;
    }

    private static async Task<IReadOnlyList<AdminEconomyServerOption>> ReadServerOptionsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT DISTINCT o.server_id, COALESCE(s.display_name, o.server_id)
            FROM launcher.economy_operations o
            LEFT JOIN launcher.servers s ON s.id = o.server_id
            WHERE o.status = 'Applied'
            ORDER BY 2, 1;
            """;
        var result = new List<AdminEconomyServerOption>();
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new AdminEconomyServerOption(
                reader.GetString(0),
                reader.GetString(1)));
        }

        return result;
    }

    private static async Task<IReadOnlyList<AdminEconomyItemOption>> ReadItemOptionsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        var result = new List<AdminEconomyItemOption>();
        await using var command = new NpgsqlCommand(ItemOptionsSql, connection, transaction);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(ReadItemOption(reader));
        }

        return result;
    }

    private static async Task<AdminEconomyItemOption?> ReadItemOptionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string itemId,
        CancellationToken cancellationToken)
    {
        var sql = $"""
            SELECT item_id, current_unit_price, enabled
            FROM ({ItemOptionsSql.Trim().TrimEnd(';')}) item_options
            WHERE item_id = $1;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(itemId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadItemOption(reader)
            : null;
    }

    private static AdminEconomyItemOption ReadItemOption(NpgsqlDataReader reader) =>
        new(
            reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetDecimal(1),
            reader.GetBoolean(2));

    private static async Task<AdminEconomyItemSummary> ReadItemSummaryAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AdminEconomyWindow window,
        string? serverId,
        string itemId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                (array_agg(q.unit_price ORDER BY o.created_at, q.quote_id))[1],
                (array_agg(q.unit_price ORDER BY o.created_at DESC, q.quote_id DESC))[1],
                min(q.unit_price),
                max(q.unit_price),
                COALESCE(sum(q.quantity), 0)::bigint,
                COALESCE(sum(q.total_amount), 0)::numeric,
                count(DISTINCT q.player_uuid)::bigint,
                count(*)::bigint
            FROM launcher.economy_sale_quotes q
            JOIN launcher.economy_operations o
              ON o.operation_id = q.committed_operation_id
            WHERE q.status = 'Committed'
              AND o.status = 'Applied'
              AND o.created_at >= $1
              AND o.created_at < $2
              AND ($3::text IS NULL OR o.server_id = $3)
              AND q.item_id = $4;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        AddItemWindowParameters(command, window, serverId, itemId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        var open = reader.IsDBNull(0) ? (decimal?)null : reader.GetDecimal(0);
        var close = reader.IsDBNull(1) ? (decimal?)null : reader.GetDecimal(1);
        return new AdminEconomyItemSummary(
            open,
            close,
            reader.IsDBNull(2) ? null : reader.GetDecimal(2),
            reader.IsDBNull(3) ? null : reader.GetDecimal(3),
            open is > 0 && close is not null ? (close.Value - open.Value) / open.Value : null,
            reader.GetInt64(4),
            reader.GetDecimal(5),
            reader.GetInt64(6),
            reader.GetInt64(7));
    }

    private static async Task<IReadOnlyList<AdminEconomyItemSeriesPoint>> ReadItemSeriesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AdminEconomyWindow window,
        string? serverId,
        string itemId,
        CancellationToken cancellationToken)
    {
        var sql = BuildItemSeriesSql(window.BucketSize);
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        AddItemWindowParameters(command, window, serverId, itemId);
        var points = new Dictionary<DateTimeOffset, AdminEconomyItemSeriesPoint>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var point = new AdminEconomyItemSeriesPoint(
                    reader.GetFieldValue<DateTimeOffset>(0),
                    reader.GetDecimal(1),
                    reader.GetDecimal(2),
                    reader.GetDecimal(3),
                    reader.GetDecimal(4),
                    reader.GetDecimal(5),
                    reader.GetInt64(6),
                    reader.GetDecimal(7),
                    reader.GetInt64(8),
                    reader.GetInt64(9));
                points[point.At] = point;
            }
        }

        return window.Buckets()
            .Select(bucket => points.GetValueOrDefault(bucket) ?? new AdminEconomyItemSeriesPoint(
                bucket,
                null,
                null,
                null,
                null,
                null,
                0,
                0,
                0,
                0))
            .ToArray();
    }

    internal static string BuildItemSeriesSql(TimeSpan bucketSize)
    {
        var unit = bucketSize == TimeSpan.FromHours(1) ? "hour" : "day";
        return $$"""
            SELECT
                date_trunc('{{unit}}', o.created_at AT TIME ZONE 'UTC') AT TIME ZONE 'UTC',
                (array_agg(q.unit_price ORDER BY o.created_at, q.quote_id))[1],
                (array_agg(q.unit_price ORDER BY o.created_at DESC, q.quote_id DESC))[1],
                (sum(q.total_amount) / NULLIF(sum(q.quantity), 0))::numeric,
                min(q.unit_price),
                max(q.unit_price),
                sum(q.quantity)::bigint,
                sum(q.total_amount)::numeric,
                count(DISTINCT q.player_uuid)::bigint,
                count(*)::bigint
            FROM launcher.economy_sale_quotes q
            JOIN launcher.economy_operations o
              ON o.operation_id = q.committed_operation_id
            WHERE q.status = 'Committed'
              AND o.status = 'Applied'
              AND o.created_at >= $1
              AND o.created_at < $2
              AND ($3::text IS NULL OR o.server_id = $3)
              AND q.item_id = $4
            GROUP BY 1
            ORDER BY 1;
            """;
    }

    private static void AddWindowParameters(
        NpgsqlCommand command,
        AdminEconomyWindow window,
        string? serverId)
    {
        command.Parameters.AddWithValue(window.From);
        command.Parameters.AddWithValue(window.To);
        command.Parameters.AddWithValue(
            NpgsqlDbType.Text,
            serverId is null ? DBNull.Value : serverId);
    }

    private static void AddItemWindowParameters(
        NpgsqlCommand command,
        AdminEconomyWindow window,
        string? serverId,
        string itemId)
    {
        AddWindowParameters(command, window, serverId);
        command.Parameters.AddWithValue(itemId);
    }

    private sealed record WealthSnapshot(
        decimal TotalSupply,
        long FundedAccounts,
        decimal AverageBalance,
        decimal MedianBalance,
        decimal P90Balance,
        decimal TopTenPercentShare);

    private sealed record WindowMetrics(
        decimal WindowIssued,
        decimal TransferVolume,
        long ActivePlayers,
        long OperationCount);

    private sealed record SeriesDelta(
        decimal GlobalIssued,
        decimal FilteredIssued);
}

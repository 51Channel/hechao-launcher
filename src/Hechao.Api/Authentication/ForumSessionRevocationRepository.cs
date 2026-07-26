using Npgsql;

namespace Hechao.Api.Authentication;

public sealed record ForumSessionRevocationDelivery(
    Guid RequestId,
    Guid UserId,
    int AttemptCount);

public sealed class ForumSessionRevocationRepository(NpgsqlDataSource dataSource)
{
    public async Task<Guid> EnqueueAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid userId,
        DateTimeOffset requestedAt,
        CancellationToken cancellationToken)
    {
        var requestId = Guid.NewGuid();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO launcher.forum_session_revocation_outbox
                (id, user_id, requested_at, next_attempt_at)
            VALUES ($1, $2, $3, $3);
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(requestId);
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(requestedAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return requestId;
    }

    public async Task<IReadOnlyList<ForumSessionRevocationDelivery>> ClaimDueAsync(
        DateTimeOffset now,
        TimeSpan lease,
        int batchSize,
        CancellationToken cancellationToken)
    {
        const string sql = """
            WITH due AS (
                SELECT id
                FROM launcher.forum_session_revocation_outbox
                WHERE completed_at IS NULL
                  AND next_attempt_at <= $1
                  AND (locked_until IS NULL OR locked_until <= $1)
                ORDER BY next_attempt_at, requested_at, id
                LIMIT $3
                FOR UPDATE SKIP LOCKED
            )
            UPDATE launcher.forum_session_revocation_outbox AS outbox
            SET locked_until = $2,
                attempt_count = outbox.attempt_count + 1
            FROM due
            WHERE outbox.id = due.id
            RETURNING outbox.id, outbox.user_id, outbox.attempt_count;
            """;

        var deliveries = new List<ForumSessionRevocationDelivery>();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(now);
        command.Parameters.AddWithValue(now.Add(lease));
        command.Parameters.AddWithValue(batchSize);
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                deliveries.Add(new ForumSessionRevocationDelivery(
                    reader.GetGuid(0),
                    reader.GetGuid(1),
                    reader.GetInt32(2)));
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return deliveries;
    }

    public async Task MarkCompletedAsync(
        Guid requestId,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            UPDATE launcher.forum_session_revocation_outbox
            SET completed_at = $2,
                locked_until = NULL,
                last_error = NULL
            WHERE id = $1 AND completed_at IS NULL;
            """,
            connection);
        command.Parameters.AddWithValue(requestId);
        command.Parameters.AddWithValue(completedAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task MarkFailedAsync(
        Guid requestId,
        DateTimeOffset nextAttemptAt,
        string error,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            UPDATE launcher.forum_session_revocation_outbox
            SET next_attempt_at = $2,
                locked_until = NULL,
                last_error = $3
            WHERE id = $1 AND completed_at IS NULL;
            """,
            connection);
        command.Parameters.AddWithValue(requestId);
        command.Parameters.AddWithValue(nextAttemptAt);
        command.Parameters.AddWithValue(
            error.Length <= 500 ? error : error[..500]);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

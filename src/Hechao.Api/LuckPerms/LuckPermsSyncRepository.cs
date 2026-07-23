using System.Text.Json;
using Hechao.Contracts;
using Npgsql;
using NpgsqlTypes;

namespace Hechao.Api.LuckPerms;

public sealed class LuckPermsSyncRepository(NpgsqlDataSource dataSource)
{
    public async Task<LuckPermsSnapshotResponse> ImportAsync(
        LuckPermsSnapshotRequest request,
        CancellationToken cancellationToken)
    {
        var receivedAt = DateTimeOffset.UtcNow;
        var playerJson = JsonSerializer.Serialize(request.Players.Select(player => new
        {
            minecraft_uuid = player.MinecraftUuid,
            minecraft_name = player.MinecraftName,
            primary_group = player.PrimaryGroup
        }));

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await UpsertSnapshotsAsync(
            connection,
            transaction,
            playerJson,
            request.CapturedAt,
            receivedAt,
            cancellationToken);

        if (request.IsFullSnapshot)
        {
            await DeleteMissingSnapshotsAsync(
                connection,
                transaction,
                playerJson,
                request.CapturedAt,
                cancellationToken);
        }

        var updatedIdentities = await UpdateKnownIdentitiesAsync(
            connection,
            transaction,
            cancellationToken);

        await UpdateUserTiersAsync(connection, transaction, cancellationToken);
        await WriteAuditAsync(
            connection,
            transaction,
            request,
            updatedIdentities,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return new LuckPermsSnapshotResponse(request.Players.Count, updatedIdentities, receivedAt);
    }

    private static async Task UpsertSnapshotsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string playerJson,
        DateTimeOffset capturedAt,
        DateTimeOffset receivedAt,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO launcher.luckperms_player_snapshots
                (minecraft_uuid, minecraft_name, primary_group, source_captured_at, received_at)
            SELECT incoming.minecraft_uuid,
                   incoming.minecraft_name,
                   lower(incoming.primary_group),
                   $2,
                   $3
            FROM jsonb_to_recordset($1::jsonb) AS incoming(
                minecraft_uuid uuid,
                minecraft_name text,
                primary_group text
            )
            ON CONFLICT (minecraft_uuid) DO UPDATE
            SET minecraft_name = EXCLUDED.minecraft_name,
                primary_group = EXCLUDED.primary_group,
                source_captured_at = EXCLUDED.source_captured_at,
                received_at = EXCLUDED.received_at
            WHERE EXCLUDED.source_captured_at >= launcher.luckperms_player_snapshots.source_captured_at;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Jsonb, Value = playerJson });
        command.Parameters.AddWithValue(capturedAt);
        command.Parameters.AddWithValue(receivedAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task DeleteMissingSnapshotsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string playerJson,
        DateTimeOffset capturedAt,
        CancellationToken cancellationToken)
    {
        const string sql = """
            DELETE FROM launcher.luckperms_player_snapshots existing
            WHERE existing.source_captured_at <= $2
              AND NOT EXISTS (
                  SELECT 1
                  FROM jsonb_to_recordset($1::jsonb) AS incoming(minecraft_uuid uuid)
                  WHERE incoming.minecraft_uuid = existing.minecraft_uuid
              );
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Jsonb, Value = playerJson });
        command.Parameters.AddWithValue(capturedAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> UpdateKnownIdentitiesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE launcher.minecraft_identities identity
            SET luckperms_primary_group = snapshot.primary_group,
                luckperms_synced_at = snapshot.source_captured_at,
                updated_at = now()
            FROM launcher.luckperms_player_snapshots snapshot
            WHERE snapshot.minecraft_uuid = identity.minecraft_uuid
              AND (
                  identity.luckperms_primary_group IS DISTINCT FROM snapshot.primary_group
                  OR identity.luckperms_synced_at IS DISTINCT FROM snapshot.source_captured_at
              );
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpdateUserTiersAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE launcher.users user_account
            SET access_tier = COALESCE(mapping.access_tier, 'Member'),
                updated_at = now()
            FROM launcher.minecraft_identities identity
            LEFT JOIN launcher.luckperms_group_tier_mappings mapping
                ON mapping.primary_group = identity.luckperms_primary_group
            WHERE identity.user_id = user_account.id
              AND user_account.access_tier IS DISTINCT FROM COALESCE(mapping.access_tier, 'Member');
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task WriteAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        LuckPermsSnapshotRequest request,
        int updatedIdentities,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO launcher.audit_logs
                (action, target_type, target_id, after_data)
            VALUES ('luckperms.snapshot.imported', 'luckperms', 'global', $1);
            """;

        var auditData = JsonSerializer.Serialize(new
        {
            request.CapturedAt,
            request.IsFullSnapshot,
            ImportedPlayers = request.Players.Count,
            UpdatedIdentities = updatedIdentities
        });

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Jsonb, Value = auditData });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

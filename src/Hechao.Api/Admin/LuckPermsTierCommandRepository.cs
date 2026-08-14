using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hechao.Contracts;
using Npgsql;
using NpgsqlTypes;

namespace Hechao.Api.Admin;

public enum AdminLuckPermsTierMutationStatus
{
    Success,
    UserNotFound,
    MinecraftIdentityNotLinked,
    SelfProtection,
    LastAdministrator,
    RevisionConflict,
    CommandPending,
    NoChange
}

public enum LuckPermsTierCompletionStatus
{
    Success,
    CommandNotFound,
    ClaimConflict,
    OutcomeMismatch
}

public sealed record AdminLuckPermsTierMutationResult(
    AdminLuckPermsTierMutationStatus Status,
    AdminLuckPermsTierChangeRecord? Command = null,
    string? CurrentPrimaryGroup = null);

public sealed record LuckPermsTierCompletionResult(
    LuckPermsTierCompletionStatus Status,
    AdminLuckPermsTierChangeRecord? Command = null);

public sealed class LuckPermsTierCommandRepository(NpgsqlDataSource dataSource)
{
    private static readonly JsonSerializerOptions AuditJsonOptions =
        CreateAuditJsonOptions();

    public async Task<AdminLuckPermsTierMutationResult> QueueAsync(
        Guid userId,
        AdminLuckPermsTierChangeRequest request,
        Guid actorUserId,
        IPAddress? sourceIp,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var targetGroup = GroupForTier(request.TargetTier);
        var expectedGroup = request.ExpectedPrimaryGroup.Trim().ToLowerInvariant();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await AcquireAccountSecurityLockAsync(connection, transaction, cancellationToken);

        var target = await ReadTargetForUpdateAsync(
            connection,
            transaction,
            userId,
            cancellationToken);
        if (target is null)
        {
            return new AdminLuckPermsTierMutationResult(
                AdminLuckPermsTierMutationStatus.UserNotFound);
        }

        if (target.MinecraftUuid is null)
        {
            return new AdminLuckPermsTierMutationResult(
                AdminLuckPermsTierMutationStatus.MinecraftIdentityNotLinked);
        }

        if (userId == actorUserId)
        {
            return new AdminLuckPermsTierMutationResult(
                AdminLuckPermsTierMutationStatus.SelfProtection);
        }

        var pending = await ReadPendingForUpdateAsync(
            connection,
            transaction,
            userId,
            cancellationToken);
        if (pending is not null)
        {
            return new AdminLuckPermsTierMutationResult(
                AdminLuckPermsTierMutationStatus.CommandPending,
                pending,
                target.PrimaryGroup);
        }

        if (!string.Equals(
                target.PrimaryGroup,
                expectedGroup,
                StringComparison.Ordinal))
        {
            return new AdminLuckPermsTierMutationResult(
                AdminLuckPermsTierMutationStatus.RevisionConflict,
                CurrentPrimaryGroup: target.PrimaryGroup);
        }

        if (string.Equals(target.PrimaryGroup, targetGroup, StringComparison.Ordinal))
        {
            return new AdminLuckPermsTierMutationResult(
                AdminLuckPermsTierMutationStatus.NoChange,
                CurrentPrimaryGroup: target.PrimaryGroup);
        }

        if (target.AccessTier == AccessTier.Administrator &&
            request.TargetTier != AccessTier.Administrator &&
            !target.IsDisabled &&
            await CountActiveAdministratorsAsync(
                connection,
                transaction,
                cancellationToken) <= 1)
        {
            return new AdminLuckPermsTierMutationResult(
                AdminLuckPermsTierMutationStatus.LastAdministrator);
        }

        var commandId = Guid.NewGuid();
        AdminLuckPermsTierChangeRecord command;
        await using (var insert = new NpgsqlCommand(
                         """
                         INSERT INTO launcher.luckperms_tier_change_commands
                             (id, user_id, minecraft_uuid, expected_primary_group,
                              target_primary_group, target_access_tier, reason,
                              requested_by, requested_at)
                         VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9)
                         RETURNING id, user_id, minecraft_uuid,
                                   expected_primary_group, target_primary_group,
                                   target_access_tier, reason, status,
                                   requested_by, requested_at, claimed_by,
                                   claimed_at, claim_expires_at, attempt_count,
                                   completed_at, observed_primary_group,
                                   failure_code;
                         """,
                         connection,
                         transaction))
        {
            insert.Parameters.AddWithValue(commandId);
            insert.Parameters.AddWithValue(userId);
            insert.Parameters.AddWithValue(target.MinecraftUuid.Value);
            insert.Parameters.AddWithValue(expectedGroup);
            insert.Parameters.AddWithValue(targetGroup);
            insert.Parameters.AddWithValue(request.TargetTier.ToString());
            insert.Parameters.AddWithValue(request.Reason.Trim());
            insert.Parameters.AddWithValue(actorUserId);
            insert.Parameters.AddWithValue(now);
            await using var reader = await insert.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            command = ReadCommand(reader);
        }

        await WriteAuditAsync(
            connection,
            transaction,
            actorUserId,
            sourceIp,
            "luckperms.tier_change.queued",
            commandId.ToString("D"),
            new
            {
                target.PrimaryGroup,
                AccessTier = target.AccessTier.ToString()
            },
            command,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new AdminLuckPermsTierMutationResult(
            AdminLuckPermsTierMutationStatus.Success,
            command,
            target.PrimaryGroup);
    }

    public async Task<AdminLuckPermsTierChangeRecord?> GetPendingForUserAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        const string sql = """
            SELECT id, user_id, minecraft_uuid, expected_primary_group,
                   target_primary_group, target_access_tier, reason, status,
                   requested_by, requested_at, claimed_by, claimed_at,
                   claim_expires_at, attempt_count, completed_at,
                   observed_primary_group, failure_code
            FROM launcher.luckperms_tier_change_commands
            WHERE user_id = $1 AND status IN ('Pending', 'Claimed')
            ORDER BY requested_at DESC
            LIMIT 1;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadCommand(reader) : null;
    }

    public async Task<LuckPermsTierCommandClaimResponse> ClaimAsync(
        string agentId,
        string agentVersion,
        int protocolVersion,
        int limit,
        DateTimeOffset now,
        TimeSpan lease,
        CancellationToken cancellationToken)
    {
        var claimIdentity = FormatAgentClaimIdentity(
            agentId,
            agentVersion,
            protocolVersion);
        const string sql = """
            WITH due AS (
                SELECT id
                FROM launcher.luckperms_tier_change_commands
                WHERE status = 'Pending'
                   OR (status = 'Claimed' AND claim_expires_at <= $1)
                ORDER BY requested_at, id
                LIMIT $4
                FOR UPDATE SKIP LOCKED
            )
            UPDATE launcher.luckperms_tier_change_commands AS tier_command
            SET status = 'Claimed',
                claimed_by = $2,
                claimed_at = $1,
                claim_expires_at = $3,
                attempt_count = tier_command.attempt_count + 1,
                completed_at = NULL,
                observed_primary_group = NULL,
                failure_code = NULL
            FROM due
            WHERE tier_command.id = due.id
            RETURNING tier_command.id,
                      tier_command.minecraft_uuid,
                      tier_command.expected_primary_group,
                      tier_command.target_primary_group,
                      tier_command.target_access_tier,
                      tier_command.attempt_count;
            """;

        var commands = new List<LuckPermsTierCommandDelivery>();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(now);
        command.Parameters.AddWithValue(claimIdentity);
        command.Parameters.AddWithValue(now.Add(lease));
        command.Parameters.AddWithValue(limit);
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                commands.Add(new LuckPermsTierCommandDelivery(
                    reader.GetGuid(0),
                    reader.GetGuid(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    Enum.Parse<AccessTier>(reader.GetString(4), ignoreCase: true),
                    reader.GetInt32(5)));
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return new LuckPermsTierCommandClaimResponse(commands, now);
    }

    public async Task<LuckPermsTierCompletionResult> CompleteAsync(
        Guid commandId,
        LuckPermsTierCommandCompletionRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var observedGroup = request.ObservedPrimaryGroup.Trim().ToLowerInvariant();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var command = await ReadForUpdateAsync(
            connection,
            transaction,
            commandId,
            cancellationToken);
        if (command is null)
        {
            return new LuckPermsTierCompletionResult(
                LuckPermsTierCompletionStatus.CommandNotFound);
        }

        if (command.Status != LuckPermsTierCommandStatus.Claimed ||
            !string.Equals(
                command.ClaimedBy,
                FormatAgentClaimIdentity(
                    request.AgentId,
                    request.AgentVersion,
                    request.ProtocolVersion),
                StringComparison.Ordinal) ||
            command.AttemptCount != request.AttemptCount)
        {
            return new LuckPermsTierCompletionResult(
                LuckPermsTierCompletionStatus.ClaimConflict,
                command);
        }

        if ((request.Outcome == LuckPermsTierCommandOutcome.Applied &&
             !string.Equals(
                 observedGroup,
                 command.TargetPrimaryGroup,
                 StringComparison.Ordinal)) ||
            (request.Outcome == LuckPermsTierCommandOutcome.Conflict &&
             string.Equals(
                 observedGroup,
                 command.TargetPrimaryGroup,
                 StringComparison.Ordinal)))
        {
            return new LuckPermsTierCompletionResult(
                LuckPermsTierCompletionStatus.OutcomeMismatch,
                command);
        }

        var status = request.Outcome switch
        {
            LuckPermsTierCommandOutcome.Applied => LuckPermsTierCommandStatus.Applied,
            LuckPermsTierCommandOutcome.Conflict => LuckPermsTierCommandStatus.Conflict,
            _ => LuckPermsTierCommandStatus.Failed
        };
        await using (var update = new NpgsqlCommand(
                         """
                         UPDATE launcher.luckperms_tier_change_commands
                         SET status = $2,
                             completed_at = $3,
                             observed_primary_group = $4,
                             failure_code = $5
                         WHERE id = $1;
                         """,
                         connection,
                         transaction))
        {
            update.Parameters.AddWithValue(commandId);
            update.Parameters.AddWithValue(status.ToString());
            update.Parameters.AddWithValue(now);
            update.Parameters.AddWithValue(observedGroup);
            AddNullableText(
                update.Parameters,
                request.Outcome == LuckPermsTierCommandOutcome.Failed
                    ? request.FailureCode!.Trim()
                    : null);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        if (status == LuckPermsTierCommandStatus.Applied)
        {
            await ApplyTierSnapshotAsync(
                connection,
                transaction,
                command,
                now,
                cancellationToken);
        }

        await WriteAuditAsync(
            connection,
            transaction,
            actorUserId: null,
            sourceIp: null,
            "luckperms.tier_change.completed",
            commandId.ToString("D"),
            command,
            new
            {
                Status = status.ToString(),
                ObservedPrimaryGroup = observedGroup,
                FailureCode = request.Outcome == LuckPermsTierCommandOutcome.Failed
                    ? request.FailureCode
                    : null,
                AgentId = request.AgentId.Trim(),
                AgentVersion = request.AgentVersion.Trim(),
                request.ProtocolVersion
            },
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new LuckPermsTierCompletionResult(
            LuckPermsTierCompletionStatus.Success,
            await GetByIdAsync(commandId, cancellationToken));
    }

    private async Task<AdminLuckPermsTierChangeRecord?> GetByIdAsync(
        Guid commandId,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        const string sql = """
            SELECT id, user_id, minecraft_uuid, expected_primary_group,
                   target_primary_group, target_access_tier, reason, status,
                   requested_by, requested_at, claimed_by, claimed_at,
                   claim_expires_at, attempt_count, completed_at,
                   observed_primary_group, failure_code
            FROM launcher.luckperms_tier_change_commands
            WHERE id = $1;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(commandId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadCommand(reader) : null;
    }

    private static async Task<TierTarget?> ReadTargetForUpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid userId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT user_account.access_tier,
                   user_account.is_disabled,
                   identity.minecraft_uuid,
                   COALESCE(identity.luckperms_primary_group, 'default')
            FROM launcher.users user_account
            LEFT JOIN launcher.minecraft_identities identity
                ON identity.user_id = user_account.id
            WHERE user_account.id = $1
            FOR UPDATE OF user_account;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new TierTarget(
            Enum.Parse<AccessTier>(reader.GetString(0), ignoreCase: true),
            reader.GetBoolean(1),
            reader.IsDBNull(2) ? null : reader.GetGuid(2),
            reader.GetString(3));
    }

    private static async Task<AdminLuckPermsTierChangeRecord?> ReadPendingForUpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid userId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, user_id, minecraft_uuid, expected_primary_group,
                   target_primary_group, target_access_tier, reason, status,
                   requested_by, requested_at, claimed_by, claimed_at,
                   claim_expires_at, attempt_count, completed_at,
                   observed_primary_group, failure_code
            FROM launcher.luckperms_tier_change_commands
            WHERE user_id = $1 AND status IN ('Pending', 'Claimed')
            ORDER BY requested_at DESC
            LIMIT 1
            FOR UPDATE;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadCommand(reader) : null;
    }

    private static async Task<AdminLuckPermsTierChangeRecord?> ReadForUpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid commandId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, user_id, minecraft_uuid, expected_primary_group,
                   target_primary_group, target_access_tier, reason, status,
                   requested_by, requested_at, claimed_by, claimed_at,
                   claim_expires_at, attempt_count, completed_at,
                   observed_primary_group, failure_code
            FROM launcher.luckperms_tier_change_commands
            WHERE id = $1
            FOR UPDATE;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(commandId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadCommand(reader) : null;
    }

    private static AdminLuckPermsTierChangeRecord ReadCommand(NpgsqlDataReader reader)
    {
        return new AdminLuckPermsTierChangeRecord(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetString(3),
            reader.GetString(4),
            Enum.Parse<AccessTier>(reader.GetString(5), ignoreCase: true),
            reader.GetString(6),
            Enum.Parse<LuckPermsTierCommandStatus>(
                reader.GetString(7),
                ignoreCase: true),
            reader.GetGuid(8),
            new DateTimeOffset(reader.GetDateTime(9)),
            reader.IsDBNull(10) ? null : reader.GetString(10),
            reader.IsDBNull(11)
                ? null
                : new DateTimeOffset(reader.GetDateTime(11)),
            reader.IsDBNull(12)
                ? null
                : new DateTimeOffset(reader.GetDateTime(12)),
            reader.GetInt32(13),
            reader.IsDBNull(14)
                ? null
                : new DateTimeOffset(reader.GetDateTime(14)),
            reader.IsDBNull(15) ? null : reader.GetString(15),
            reader.IsDBNull(16) ? null : reader.GetString(16));
    }

    private static async Task ApplyTierSnapshotAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AdminLuckPermsTierChangeRecord command,
        DateTimeOffset appliedAt,
        CancellationToken cancellationToken)
    {
        await using (var updateIdentity = new NpgsqlCommand(
                         """
                         UPDATE launcher.minecraft_identities
                         SET luckperms_primary_group = $3,
                             luckperms_synced_at = $4,
                             updated_at = $4
                         WHERE user_id = $1 AND minecraft_uuid = $2;
                         """,
                         connection,
                         transaction))
        {
            updateIdentity.Parameters.AddWithValue(command.UserId);
            updateIdentity.Parameters.AddWithValue(command.MinecraftUuid);
            updateIdentity.Parameters.AddWithValue(command.TargetPrimaryGroup);
            updateIdentity.Parameters.AddWithValue(appliedAt);
            await updateIdentity.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var updateSnapshot = new NpgsqlCommand(
                         """
                         UPDATE launcher.luckperms_player_snapshots
                         SET primary_group = $2,
                             source_captured_at = $3,
                             received_at = $3
                         WHERE minecraft_uuid = $1;
                         """,
                         connection,
                         transaction))
        {
            updateSnapshot.Parameters.AddWithValue(command.MinecraftUuid);
            updateSnapshot.Parameters.AddWithValue(command.TargetPrimaryGroup);
            updateSnapshot.Parameters.AddWithValue(appliedAt);
            await updateSnapshot.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var updateUser = new NpgsqlCommand(
            """
            UPDATE launcher.users
            SET access_tier = $2, updated_at = $3
            WHERE id = $1;
            """,
            connection,
            transaction);
        updateUser.Parameters.AddWithValue(command.UserId);
        updateUser.Parameters.AddWithValue(command.TargetAccessTier.ToString());
        updateUser.Parameters.AddWithValue(appliedAt);
        await updateUser.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task AcquireAccountSecurityLockAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(721220003);",
            connection,
            transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> CountActiveAdministratorsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT count(*)::integer
            FROM launcher.users
            WHERE access_tier = 'Administrator' AND NOT is_disabled;
            """,
            connection,
            transaction);
        return (int)(await command.ExecuteScalarAsync(cancellationToken) ?? 0);
    }

    private static string GroupForTier(AccessTier tier) =>
        tier switch
        {
            AccessTier.Member => "default",
            AccessTier.Participant => "vip",
            AccessTier.Collaborator => "admin",
            AccessTier.Administrator => "owner",
            _ => throw new ArgumentOutOfRangeException(nameof(tier))
        };

    internal static string FormatAgentClaimIdentity(
        string agentId,
        string agentVersion,
        int protocolVersion) =>
        $"{agentId.Trim()}@{agentVersion.Trim()}/p{protocolVersion}";

    private static void AddNullableText(
        NpgsqlParameterCollection parameters,
        string? value)
    {
        parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Text,
            Value = value is null ? DBNull.Value : value
        });
    }

    private static async Task WriteAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid? actorUserId,
        IPAddress? sourceIp,
        string action,
        string targetId,
        object? before,
        object? after,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO launcher.audit_logs
                (actor_user_id, action, target_type, target_id, source_ip,
                 before_data, after_data)
            VALUES ($1, $2, 'luckperms_tier_change', $3, $4, $5, $6);
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        AdminPostgresParameters.AddPositional(
            command.Parameters,
            NpgsqlDbType.Uuid,
            actorUserId);
        command.Parameters.AddWithValue(action);
        command.Parameters.AddWithValue(targetId);
        AdminPostgresParameters.AddPositional(
            command.Parameters,
            NpgsqlDbType.Inet,
            sourceIp);
        AdminPostgresParameters.AddPositional(
            command.Parameters,
            NpgsqlDbType.Jsonb,
            before is null ? null : JsonSerializer.Serialize(before, AuditJsonOptions));
        AdminPostgresParameters.AddPositional(
            command.Parameters,
            NpgsqlDbType.Jsonb,
            after is null ? null : JsonSerializer.Serialize(after, AuditJsonOptions));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static JsonSerializerOptions CreateAuditJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private sealed record TierTarget(
        AccessTier AccessTier,
        bool IsDisabled,
        Guid? MinecraftUuid,
        string PrimaryGroup);
}

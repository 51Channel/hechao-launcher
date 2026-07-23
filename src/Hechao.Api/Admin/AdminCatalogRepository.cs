using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hechao.Contracts;
using Npgsql;
using NpgsqlTypes;

namespace Hechao.Api.Admin;

public enum AdminCatalogMutationStatus
{
    Success,
    NotFound,
    RevisionConflict,
    DuplicateId,
    ClientProfileNotFound
}

public sealed record AdminCatalogMutationResult(
    AdminCatalogMutationStatus Status,
    AdminServerRecord? Server = null);

public sealed class AdminCatalogRepository(NpgsqlDataSource dataSource)
{
    private static readonly JsonSerializerOptions AuditJsonOptions = CreateAuditJsonOptions();

    public async Task<IReadOnlyList<AdminServerRecord>> GetServersAsync(
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, display_name, short_name, icon_glyph, status, max_players,
                   minecraft_version, loader, minimum_tier, client_profile_id,
                   velocity_target, sort_order, is_visible, revision, created_at, updated_at
            FROM launcher.servers
            ORDER BY sort_order, id;
            """;

        var servers = new List<AdminServerRecord>();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            servers.Add(ReadServer(reader));
        }

        return servers;
    }

    public async Task<AdminServerRecord?> GetServerAsync(
        string serverId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, display_name, short_name, icon_glyph, status, max_players,
                   minecraft_version, loader, minimum_tier, client_profile_id,
                   velocity_target, sort_order, is_visible, revision, created_at, updated_at
            FROM launcher.servers
            WHERE id = $1;
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(serverId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadServer(reader) : null;
    }

    public async Task<IReadOnlyList<AdminClientProfileRecord>> GetClientProfilesAsync(
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, display_name, version, download_bytes, sha256, published_at,
                   is_active, updated_at
            FROM launcher.client_profiles
            ORDER BY is_active DESC, display_name, id;
            """;

        var profiles = new List<AdminClientProfileRecord>();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            profiles.Add(new AdminClientProfileRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt64(3),
                reader.GetString(4),
                new DateTimeOffset(reader.GetDateTime(5)),
                reader.GetBoolean(6),
                new DateTimeOffset(reader.GetDateTime(7))));
        }

        return profiles;
    }

    public async Task<AdminCatalogMutationResult> CreateServerAsync(
        AdminServerCreateRequest request,
        Guid actorUserId,
        IPAddress? sourceIp,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO launcher.servers
                (id, display_name, short_name, icon_glyph, status, online_players,
                 max_players, minecraft_version, loader, minimum_tier,
                 client_profile_id, velocity_target, sort_order, is_visible)
            VALUES
                ($1, $2, $3, $4, $5, 0, $6, $7, $8, $9, $10, $11, $12, $13)
            RETURNING id, display_name, short_name, icon_glyph, status, max_players,
                      minecraft_version, loader, minimum_tier, client_profile_id,
                      velocity_target, sort_order, is_visible, revision, created_at, updated_at;
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        if (!await ClientProfileExistsAsync(
                connection,
                transaction,
                request.ClientProfileId,
                cancellationToken))
        {
            return new AdminCatalogMutationResult(AdminCatalogMutationStatus.ClientProfileNotFound);
        }

        AdminServerRecord created;
        try
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue(request.Id);
            command.Parameters.AddWithValue(request.DisplayName.Trim());
            command.Parameters.AddWithValue(request.ShortName.Trim());
            command.Parameters.AddWithValue(request.IconGlyph.Trim());
            command.Parameters.AddWithValue(request.Status.ToString());
            command.Parameters.AddWithValue(request.MaxPlayers);
            command.Parameters.AddWithValue(request.MinecraftVersion.Trim());
            command.Parameters.AddWithValue(request.Loader.ToString());
            command.Parameters.AddWithValue(request.MinimumTier.ToString());
            command.Parameters.AddWithValue(request.ClientProfileId);
            command.Parameters.AddWithValue(request.VelocityTarget);
            command.Parameters.AddWithValue(request.SortOrder);
            command.Parameters.AddWithValue(request.IsVisible);
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                await reader.ReadAsync(cancellationToken);
                created = ReadServer(reader);
            }
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return new AdminCatalogMutationResult(AdminCatalogMutationStatus.DuplicateId);
        }

        await WriteAuditAsync(
            connection,
            transaction,
            actorUserId,
            sourceIp,
            "catalog.server.created",
            created.Id,
            before: null,
            after: created,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new AdminCatalogMutationResult(AdminCatalogMutationStatus.Success, created);
    }

    public async Task<AdminCatalogMutationResult> UpdateServerAsync(
        string serverId,
        AdminServerUpdateRequest request,
        Guid actorUserId,
        IPAddress? sourceIp,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE launcher.servers
            SET display_name = $1,
                short_name = $2,
                icon_glyph = $3,
                status = $4,
                online_players = LEAST(online_players, $5),
                max_players = $5,
                minecraft_version = $6,
                loader = $7,
                minimum_tier = $8,
                client_profile_id = $9,
                velocity_target = $10,
                sort_order = $11,
                revision = revision + 1,
                updated_at = now()
            WHERE id = $12
            RETURNING id, display_name, short_name, icon_glyph, status, max_players,
                      minecraft_version, loader, minimum_tier, client_profile_id,
                      velocity_target, sort_order, is_visible, revision, created_at, updated_at;
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var before = await GetServerForUpdateAsync(
            connection,
            transaction,
            serverId,
            cancellationToken);
        if (before is null)
        {
            return new AdminCatalogMutationResult(AdminCatalogMutationStatus.NotFound);
        }

        if (before.Revision != request.ExpectedRevision)
        {
            return new AdminCatalogMutationResult(
                AdminCatalogMutationStatus.RevisionConflict,
                before);
        }

        if (!await ClientProfileExistsAsync(
                connection,
                transaction,
                request.ClientProfileId,
                cancellationToken))
        {
            return new AdminCatalogMutationResult(AdminCatalogMutationStatus.ClientProfileNotFound);
        }

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(request.DisplayName.Trim());
        command.Parameters.AddWithValue(request.ShortName.Trim());
        command.Parameters.AddWithValue(request.IconGlyph.Trim());
        command.Parameters.AddWithValue(request.Status.ToString());
        command.Parameters.AddWithValue(request.MaxPlayers);
        command.Parameters.AddWithValue(request.MinecraftVersion.Trim());
        command.Parameters.AddWithValue(request.Loader.ToString());
        command.Parameters.AddWithValue(request.MinimumTier.ToString());
        command.Parameters.AddWithValue(request.ClientProfileId);
        command.Parameters.AddWithValue(request.VelocityTarget);
        command.Parameters.AddWithValue(request.SortOrder);
        command.Parameters.AddWithValue(serverId);
        AdminServerRecord updated;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            await reader.ReadAsync(cancellationToken);
            updated = ReadServer(reader);
        }

        await WriteAuditAsync(
            connection,
            transaction,
            actorUserId,
            sourceIp,
            "catalog.server.updated",
            serverId,
            before,
            updated,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new AdminCatalogMutationResult(AdminCatalogMutationStatus.Success, updated);
    }

    public async Task<AdminCatalogMutationResult> SetServerVisibilityAsync(
        string serverId,
        AdminServerVisibilityRequest request,
        Guid actorUserId,
        IPAddress? sourceIp,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE launcher.servers
            SET is_visible = $1,
                revision = revision + 1,
                updated_at = now()
            WHERE id = $2
            RETURNING id, display_name, short_name, icon_glyph, status, max_players,
                      minecraft_version, loader, minimum_tier, client_profile_id,
                      velocity_target, sort_order, is_visible, revision, created_at, updated_at;
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var before = await GetServerForUpdateAsync(
            connection,
            transaction,
            serverId,
            cancellationToken);
        if (before is null)
        {
            return new AdminCatalogMutationResult(AdminCatalogMutationStatus.NotFound);
        }

        if (before.Revision != request.ExpectedRevision)
        {
            return new AdminCatalogMutationResult(
                AdminCatalogMutationStatus.RevisionConflict,
                before);
        }

        if (before.IsVisible == request.IsVisible)
        {
            await transaction.CommitAsync(cancellationToken);
            return new AdminCatalogMutationResult(AdminCatalogMutationStatus.Success, before);
        }

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(request.IsVisible);
        command.Parameters.AddWithValue(serverId);
        AdminServerRecord updated;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            await reader.ReadAsync(cancellationToken);
            updated = ReadServer(reader);
        }

        await WriteAuditAsync(
            connection,
            transaction,
            actorUserId,
            sourceIp,
            request.IsVisible ? "catalog.server.restored" : "catalog.server.archived",
            serverId,
            before,
            updated,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new AdminCatalogMutationResult(AdminCatalogMutationStatus.Success, updated);
    }

    public async Task<IReadOnlyList<AdminAuditLogEntry>> GetAuditLogsAsync(
        long? beforeId,
        int limit,
        CancellationToken cancellationToken)
    {
        var sql = beforeId is null
            ? """
                SELECT audit.id, audit.actor_user_id, actor.display_name, audit.action,
                       audit.target_type, audit.target_id, audit.source_ip::text,
                       audit.before_data::text, audit.after_data::text, audit.created_at
                FROM launcher.audit_logs audit
                LEFT JOIN launcher.users actor ON actor.id = audit.actor_user_id
                ORDER BY audit.id DESC
                LIMIT $1;
                """
            : """
                SELECT audit.id, audit.actor_user_id, actor.display_name, audit.action,
                       audit.target_type, audit.target_id, audit.source_ip::text,
                       audit.before_data::text, audit.after_data::text, audit.created_at
                FROM launcher.audit_logs audit
                LEFT JOIN launcher.users actor ON actor.id = audit.actor_user_id
                WHERE audit.id < $1
                ORDER BY audit.id DESC
                LIMIT $2;
                """;

        var entries = new List<AdminAuditLogEntry>();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        if (beforeId is null)
        {
            command.Parameters.AddWithValue(limit);
        }
        else
        {
            command.Parameters.AddWithValue(beforeId.Value);
            command.Parameters.AddWithValue(limit);
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            entries.Add(new AdminAuditLogEntry(
                reader.GetInt64(0),
                reader.IsDBNull(1) ? null : reader.GetGuid(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : ParseJson(reader.GetString(7)),
                reader.IsDBNull(8) ? null : ParseJson(reader.GetString(8)),
                new DateTimeOffset(reader.GetDateTime(9))));
        }

        return entries;
    }

    private static async Task<AdminServerRecord?> GetServerForUpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string serverId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, display_name, short_name, icon_glyph, status, max_players,
                   minecraft_version, loader, minimum_tier, client_profile_id,
                   velocity_target, sort_order, is_visible, revision, created_at, updated_at
            FROM launcher.servers
            WHERE id = $1
            FOR UPDATE;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(serverId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadServer(reader) : null;
    }

    private static async Task<bool> ClientProfileExistsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string profileId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT 1
            FROM launcher.client_profiles
            WHERE id = $1 AND is_active
            FOR SHARE;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(profileId);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private static async Task WriteAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid actorUserId,
        IPAddress? sourceIp,
        string action,
        string targetId,
        AdminServerRecord? before,
        AdminServerRecord? after,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO launcher.audit_logs
                (actor_user_id, action, target_type, target_id, source_ip, before_data, after_data)
            VALUES ($1, $2, 'server', $3, $4, $5, $6);
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(actorUserId);
        command.Parameters.AddWithValue(action);
        command.Parameters.AddWithValue(targetId);
        var sourceIpParameter = command.Parameters.Add("sourceIp", NpgsqlDbType.Inet);
        sourceIpParameter.Value = sourceIp is null ? DBNull.Value : sourceIp;
        var beforeParameter = command.Parameters.Add("beforeData", NpgsqlDbType.Jsonb);
        beforeParameter.Value = before is null
            ? DBNull.Value
            : JsonSerializer.Serialize(before, AuditJsonOptions);
        var afterParameter = command.Parameters.Add("afterData", NpgsqlDbType.Jsonb);
        afterParameter.Value = after is null
            ? DBNull.Value
            : JsonSerializer.Serialize(after, AuditJsonOptions);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static AdminServerRecord ReadServer(NpgsqlDataReader reader)
    {
        return new AdminServerRecord(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            Enum.Parse<ServerStatus>(reader.GetString(4), ignoreCase: true),
            reader.GetInt32(5),
            reader.GetString(6),
            Enum.Parse<ModLoaderKind>(reader.GetString(7), ignoreCase: true),
            Enum.Parse<AccessTier>(reader.GetString(8), ignoreCase: true),
            reader.GetString(9),
            reader.GetString(10),
            reader.GetInt32(11),
            reader.GetBoolean(12),
            reader.GetInt64(13),
            new DateTimeOffset(reader.GetDateTime(14)),
            new DateTimeOffset(reader.GetDateTime(15)));
    }

    private static JsonElement ParseJson(string value)
    {
        using var document = JsonDocument.Parse(value);
        return document.RootElement.Clone();
    }

    private static JsonSerializerOptions CreateAuditJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}

using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace Hechao.Api.Database;

public sealed class DatabaseMigrator(NpgsqlDataSource dataSource, ILogger<DatabaseMigrator> logger)
{
    private const string BootstrapSql = """
        CREATE SCHEMA IF NOT EXISTS launcher AUTHORIZATION CURRENT_USER;
        CREATE TABLE IF NOT EXISTS launcher.schema_migrations (
            version integer PRIMARY KEY,
            name text NOT NULL,
            checksum character(64) NOT NULL,
            applied_at timestamp with time zone NOT NULL DEFAULT now()
        );
        """;

    private static readonly Migration[] Migrations =
    [
        new(1, "initial_catalog_and_identity", "Hechao.Api.Database.Migrations.001_initial_catalog_and_identity.sql"),
        new(2, "authentication_and_luckperms", "Hechao.Api.Database.Migrations.002_authentication_and_luckperms.sql"),
        new(3, "velocity_authorization", "Hechao.Api.Database.Migrations.003_velocity_authorization.sql"),
        new(4, "server_heartbeats", "Hechao.Api.Database.Migrations.004_server_heartbeats.sql"),
        new(5, "admin_catalog_revision", "Hechao.Api.Database.Migrations.005_admin_catalog_revision.sql"),
        new(6, "admin_web_sessions", "Hechao.Api.Database.Migrations.006_admin_web_sessions.sql"),
        new(7, "hechao_accounts", "Hechao.Api.Database.Migrations.007_hechao_accounts.sql"),
        new(8, "forum_account_bridge", "Hechao.Api.Database.Migrations.008_forum_account_bridge.sql"),
        new(9, "diagnostic_uploads", "Hechao.Api.Database.Migrations.009_diagnostic_uploads.sql"),
        new(10, "admin_access_and_server_schedules", "Hechao.Api.Database.Migrations.010_admin_access_and_server_schedules.sql"),
        new(11, "admin_account_security", "Hechao.Api.Database.Migrations.011_admin_account_security.sql"),
        new(12, "forum_session_revocation_outbox", "Hechao.Api.Database.Migrations.012_forum_session_revocation_outbox.sql"),
        new(13, "luckperms_tier_change_commands", "Hechao.Api.Database.Migrations.013_luckperms_tier_change_commands.sql"),
        new(14, "client_profile_release_channels", "Hechao.Api.Database.Migrations.014_client_profile_release_channels.sql"),
        new(15, "launcher_telemetry", "Hechao.Api.Database.Migrations.015_launcher_telemetry.sql"),
        new(16, "server_runtime_metrics", "Hechao.Api.Database.Migrations.016_server_runtime_metrics.sql"),
        new(17, "operational_alerts", "Hechao.Api.Database.Migrations.017_operational_alerts.sql"),
        new(18, "protocol_translation_routes", "Hechao.Api.Database.Migrations.018_protocol_translation_routes.sql"),
        new(19, "internal_server_roles", "Hechao.Api.Database.Migrations.019_internal_server_roles.sql"),
        new(20, "server_control", "Hechao.Api.Database.Migrations.020_server_control.sql"),
        new(21, "admin_trusted_devices", "Hechao.Api.Database.Migrations.021_admin_trusted_devices.sql"),
        new(22, "package_imports", "Hechao.Api.Database.Migrations.022_package_imports.sql"),
        new(23, "package_deployment", "Hechao.Api.Database.Migrations.023_package_deployment.sql"),
        new(24, "server_directory_deletion", "Hechao.Api.Database.Migrations.024_server_directory_deletion.sql"),
        new(25, "server_control_host_memory", "Hechao.Api.Database.Migrations.025_server_control_host_memory.sql"),
        new(26, "package_publisher_progress", "Hechao.Api.Database.Migrations.026_package_publisher_progress.sql"),
        new(27, "client_profile_lifecycle", "Hechao.Api.Database.Migrations.027_client_profile_lifecycle.sql"),
        new(28, "activity_plans", "Hechao.Api.Database.Migrations.028_activity_plans.sql"),
        new(29, "dynamic_deployment_slots", "Hechao.Api.Database.Migrations.029_dynamic_deployment_slots.sql"),
        new(30, "independent_deployment_slots", "Hechao.Api.Database.Migrations.030_independent_deployment_slots.sql")
    ];

    internal static IReadOnlyList<int> RegisteredMigrationVersions { get; } =
        Migrations.Select(migration => migration.Version).ToArray();

    internal static IReadOnlyList<string> RegisteredMigrationResources { get; } =
        Migrations.Select(migration => migration.ResourceName).ToArray();

    public async Task ApplyAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await ExecuteAsync(connection, transaction, "SELECT pg_advisory_xact_lock(721220001);", cancellationToken);
        await ExecuteAsync(connection, transaction, BootstrapSql, cancellationToken);

        var appliedMigrations = await ReadAppliedMigrationsAsync(connection, transaction, cancellationToken);
        foreach (var migration in Migrations)
        {
            var sql = ReadEmbeddedSql(migration.ResourceName);
            var checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sql))).ToLowerInvariant();

            if (appliedMigrations.TryGetValue(migration.Version, out var existingChecksum))
            {
                if (!string.Equals(existingChecksum, checksum, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Database migration {migration.Version} checksum mismatch.");
                }

                continue;
            }

            logger.LogInformation("Applying database migration {Version}: {Name}", migration.Version, migration.Name);
            await ExecuteAsync(connection, transaction, sql, cancellationToken);

            await using var insert = new NpgsqlCommand(
                "INSERT INTO launcher.schema_migrations (version, name, checksum) VALUES ($1, $2, $3);",
                connection,
                transaction);
            insert.Parameters.AddWithValue(migration.Version);
            insert.Parameters.AddWithValue(migration.Name);
            insert.Parameters.AddWithValue(checksum);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task<Dictionary<int, string>> ReadAppliedMigrationsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<int, string>();
        await using var command = new NpgsqlCommand(
            "SELECT version, checksum FROM launcher.schema_migrations ORDER BY version;",
            connection,
            transaction);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(reader.GetInt32(0), reader.GetString(1));
        }

        return result;
    }

    private static async Task ExecuteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string ReadEmbeddedSql(string resourceName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Missing embedded database migration: {resourceName}");
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private sealed record Migration(int Version, string Name, string ResourceName);
}

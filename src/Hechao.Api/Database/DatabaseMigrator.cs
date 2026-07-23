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
        new(5, "admin_catalog_revision", "Hechao.Api.Database.Migrations.005_admin_catalog_revision.sql")
    ];

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

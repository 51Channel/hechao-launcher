using Hechao.Api.Database;

namespace Hechao.Api.Tests;

public sealed class DatabaseMigrationTests
{
    [Fact]
    public void RegisteredMigrations_CoverEveryEmbeddedSqlResourceInOrder()
    {
        var embeddedResources = typeof(DatabaseMigrator).Assembly
            .GetManifestResourceNames()
            .Where(name =>
                name.StartsWith(
                    "Hechao.Api.Database.Migrations.",
                    StringComparison.Ordinal) &&
                name.EndsWith(".sql", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            Enumerable.Range(1, embeddedResources.Length),
            DatabaseMigrator.RegisteredMigrationVersions);
        Assert.Equal(
            embeddedResources,
            DatabaseMigrator.RegisteredMigrationResources);
    }

    [Fact]
    public void ProtocolTranslationMigration_IsEmbeddedAndDefaultsClosed()
    {
        const string resourceName =
            "Hechao.Api.Database.Migrations.018_protocol_translation_routes.sql";
        using var stream = typeof(DatabaseMigrator).Assembly
            .GetManifestResourceStream(resourceName);

        Assert.NotNull(stream);
        using var reader = new StreamReader(stream);
        var sql = reader.ReadToEnd();

        Assert.Contains("allow_protocol_translation", sql, StringComparison.Ordinal);
        Assert.Contains("DEFAULT false", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("UPDATE launcher.servers", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InternalServerRoleMigration_IsolatesLobbyButKeepsMonitoring()
    {
        const string resourceName =
            "Hechao.Api.Database.Migrations.019_internal_server_roles.sql";
        using var stream = typeof(DatabaseMigrator).Assembly
            .GetManifestResourceStream(resourceName);

        Assert.NotNull(stream);
        using var reader = new StreamReader(stream);
        var sql = reader.ReadToEnd();

        Assert.Contains("server_role", sql, StringComparison.Ordinal);
        Assert.Contains("monitoring_enabled", sql, StringComparison.Ordinal);
        Assert.Contains("'Infrastructure'", sql, StringComparison.Ordinal);
        Assert.Contains("lower(id) = 'lobby'", sql, StringComparison.Ordinal);
        Assert.Contains("is_visible = false", sql, StringComparison.Ordinal);
        Assert.Contains(
            "allow_protocol_translation = false",
            sql,
            StringComparison.Ordinal);
        Assert.Contains("monitoring_enabled = true", sql, StringComparison.Ordinal);
        Assert.Contains(
            "servers_infrastructure_isolation_check",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "servers_lobby_is_always_infrastructure_check",
            sql,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ServerControlMigration_UsesStructuredQueueAndNoShellPayload()
    {
        const string resourceName =
            "Hechao.Api.Database.Migrations.020_server_control.sql";
        using var stream = typeof(DatabaseMigrator).Assembly
            .GetManifestResourceStream(resourceName);

        Assert.NotNull(stream);
        using var reader = new StreamReader(stream);
        var sql = reader.ReadToEnd();

        Assert.Contains("server_control_targets", sql, StringComparison.Ordinal);
        Assert.Contains("server_control_operations", sql, StringComparison.Ordinal);
        Assert.Contains("server_control_commands", sql, StringComparison.Ordinal);
        Assert.Contains(
            "'Start', 'Stop', 'ConsoleCommand', 'ApplySettings'",
            sql,
            StringComparison.Ordinal);
        Assert.DoesNotContain("shell", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("powershell", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cmd.exe", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PackageImportMigration_UsesReviewGateAndLeasedPublisherJobs()
    {
        const string resourceName =
            "Hechao.Api.Database.Migrations.022_package_imports.sql";
        using var stream = typeof(DatabaseMigrator).Assembly
            .GetManifestResourceStream(resourceName);

        Assert.NotNull(stream);
        using var reader = new StreamReader(stream);
        var sql = reader.ReadToEnd();

        Assert.Contains("package_imports", sql, StringComparison.Ordinal);
        Assert.Contains("AwaitingReview", sql, StringComparison.Ordinal);
        Assert.Contains("QueuedForPublishing", sql, StringComparison.Ordinal);
        Assert.Contains("publisher_lease_expires_at", sql, StringComparison.Ordinal);
        Assert.Contains("package_import_events", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("access_key", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private_key", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PackageDeploymentMigration_AddsOnlyStructuredDeploymentCommands()
    {
        const string resourceName =
            "Hechao.Api.Database.Migrations.023_package_deployment.sql";
        using var stream = typeof(DatabaseMigrator).Assembly
            .GetManifestResourceStream(resourceName);

        Assert.NotNull(stream);
        using var reader = new StreamReader(stream);
        var sql = reader.ReadToEnd();

        Assert.Contains("package_deployment_enabled", sql, StringComparison.Ordinal);
        Assert.Contains("'DeployPackage'", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("shell", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("powershell", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("server_directory", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ServerDirectoryDeletionMigration_IsExplicitAndStructured()
    {
        const string resourceName =
            "Hechao.Api.Database.Migrations.024_server_directory_deletion.sql";
        using var stream = typeof(DatabaseMigrator).Assembly
            .GetManifestResourceStream(resourceName);

        Assert.NotNull(stream);
        using var reader = new StreamReader(stream);
        var sql = reader.ReadToEnd();

        Assert.Contains("server_deletion_enabled", sql, StringComparison.Ordinal);
        Assert.Contains("server_files_present", sql, StringComparison.Ordinal);
        Assert.Contains("'DeleteServerFiles'", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("shell", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("powershell", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("server_directory text", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ServerControlHostMemoryMigration_StoresValidatedCapacity()
    {
        const string resourceName =
            "Hechao.Api.Database.Migrations.025_server_control_host_memory.sql";
        using var stream = typeof(DatabaseMigrator).Assembly
            .GetManifestResourceStream(resourceName);

        Assert.NotNull(stream);
        using var reader = new StreamReader(stream);
        var sql = reader.ReadToEnd();

        Assert.Contains("host_total_memory_mib integer", sql, StringComparison.Ordinal);
        Assert.Contains("BETWEEN 1024 AND 1048576", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DEFAULT", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ClientProfileLifecycleMigration_PreservesReleasesAndRequiresInactiveArchive()
    {
        const string resourceName =
            "Hechao.Api.Database.Migrations.027_client_profile_lifecycle.sql";
        using var stream = typeof(DatabaseMigrator).Assembly
            .GetManifestResourceStream(resourceName);

        Assert.NotNull(stream);
        using var reader = new StreamReader(stream);
        var sql = reader.ReadToEnd();

        Assert.Contains("archived_at", sql, StringComparison.Ordinal);
        Assert.Contains("archived_by", sql, StringComparison.Ordinal);
        Assert.Contains("archive_reason", sql, StringComparison.Ordinal);
        Assert.Contains("AND NOT is_active", sql, StringComparison.Ordinal);
        Assert.Contains("BETWEEN 4 AND 280", sql, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "DELETE FROM launcher.client_profile_releases",
            sql,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "DELETE FROM launcher.servers",
            sql,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ActivityPlanMigration_EnforcesOnePublishedHalfOpenSchedule()
    {
        const string resourceName =
            "Hechao.Api.Database.Migrations.028_activity_plans.sql";
        using var stream = typeof(DatabaseMigrator).Assembly
            .GetManifestResourceStream(resourceName);

        Assert.NotNull(stream);
        using var reader = new StreamReader(stream);
        var sql = reader.ReadToEnd();

        Assert.Contains("activity_package_import_id", sql, StringComparison.Ordinal);
        Assert.Contains("activity_plan_status", sql, StringComparison.Ordinal);
        Assert.Contains("activity_plan_deployments", sql, StringComparison.Ordinal);
        Assert.Contains("EXCLUDE USING gist", sql, StringComparison.Ordinal);
        Assert.Contains("tstzrange(opens_at, closes_at, '[)')", sql, StringComparison.Ordinal);
        Assert.Contains(
            "WHERE (activity_plan_status = 'Published')",
            sql,
            StringComparison.Ordinal);
        Assert.Contains("deployed_package_import_id", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE FROM launcher.servers", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DynamicDeploymentSlotMigration_UsesStructuredProvisioningCommands()
    {
        const string resourceName =
            "Hechao.Api.Database.Migrations.029_dynamic_deployment_slots.sql";
        using var stream = typeof(DatabaseMigrator).Assembly
            .GetManifestResourceStream(resourceName);

        Assert.NotNull(stream);
        using var reader = new StreamReader(stream);
        var sql = reader.ReadToEnd();

        Assert.Contains("deployment_slots", sql, StringComparison.Ordinal);
        Assert.Contains("'CreateDeploymentSlot'", sql, StringComparison.Ordinal);
        Assert.Contains("'Provisioning', 'Ready', 'Failed'", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("server_directory", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("powershell", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cmd.exe", sql, StringComparison.OrdinalIgnoreCase);
    }
}

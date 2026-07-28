using Hechao.Api.Database;

namespace Hechao.Api.Tests;

public sealed class DatabaseMigrationTests
{
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
}

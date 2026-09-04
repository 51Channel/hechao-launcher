using System.Globalization;
using Hechao.Contracts;
using Hechao.Launcher.Converters;

namespace Hechao.Launcher.Tests;

public sealed class ServerIsActivityConverterTests
{
    [Theory]
    [InlineData("survival2", null, false)]
    [InlineData("legacy-event", null, true)]
    [InlineData("survival2", ServerCatalogSection.Activity, true)]
    [InlineData("legacy-event", ServerCatalogSection.Permanent, false)]
    public void Convert_UsesTheSharedCatalogClassification(
        string serverId,
        ServerCatalogSection? catalogSection,
        bool expected)
    {
        var converter = new ServerIsActivityConverter();

        var actual = converter.Convert(
            CreateServer(serverId, catalogSection),
            typeof(bool),
            null,
            CultureInfo.InvariantCulture);

        Assert.Equal(expected, Assert.IsType<bool>(actual));
    }

    private static ServerSummary CreateServer(
        string id,
        ServerCatalogSection? catalogSection) =>
        new(
            id,
            "测试服务器",
            "测试",
            "测",
            ServerStatus.Online,
            0,
            20,
            "1.21.11",
            ModLoaderKind.Paper,
            AccessTier.Member,
            "test-profile",
            CatalogSection: catalogSection);
}

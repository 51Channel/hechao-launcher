using System.Globalization;
using Hechao.Contracts;
using Hechao.Launcher.Converters;

namespace Hechao.Launcher.Tests;

public sealed class ServerListMetaTextConverterTests
{
    [Fact]
    public void ActivityMetadata_UsesCompactLocalSchedule()
    {
        var opensAt = new DateTimeOffset(2026, 8, 4, 10, 0, 0, TimeSpan.Zero);
        var closesAt = opensAt.AddHours(3);
        var converter = new ServerListMetaTextConverter();

        var text = Assert.IsType<string>(converter.Convert(
            CreateServer(ServerCatalogSection.Activity, opensAt, closesAt),
            typeof(string),
            null,
            CultureInfo.GetCultureInfo("zh-CN")));

        Assert.Contains(opensAt.ToLocalTime().ToString("M月d日 HH:mm"), text);
        Assert.Contains(closesAt.ToLocalTime().ToString("HH:mm"), text);
    }

    [Fact]
    public void PermanentServerMetadata_RemainsMinecraftVersion()
    {
        var converter = new ServerListMetaTextConverter();

        var text = converter.Convert(
            CreateServer(ServerCatalogSection.Permanent, null, null),
            typeof(string),
            null,
            CultureInfo.InvariantCulture);

        Assert.Equal("1.21.11", text);
    }

    private static ServerSummary CreateServer(
        ServerCatalogSection section,
        DateTimeOffset? opensAt,
        DateTimeOffset? closesAt) =>
        new(
            "metadata-test",
            "元数据测试服",
            "测",
            "测",
            ServerStatus.Closed,
            0,
            30,
            "1.21.11",
            ModLoaderKind.NeoForge,
            AccessTier.Member,
            "metadata-profile",
            OpensAt: opensAt,
            ClosesAt: closesAt,
            CatalogSection: section);
}

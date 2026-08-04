using Hechao.Contracts;

namespace Hechao.Api.Catalog;

internal static class PublicActivityCatalogProjector
{
    public static PublicActivityCatalogSnapshot Create(
        LauncherCatalogSnapshot catalog) =>
        new(
            catalog.GeneratedAt,
            catalog.Servers
                .Where(server =>
                    server.CatalogSection == ServerCatalogSection.Activity)
                .Select(server => new PublicActivitySummary(
                    server.Id,
                    server.Name,
                    server.Status,
                    server.Announcement,
                    server.OpensAt,
                    server.ClosesAt,
                    server.MaxPlayers,
                    server.MinecraftVersion,
                    server.Loader,
                    server.MinimumTier))
                .ToArray());
}

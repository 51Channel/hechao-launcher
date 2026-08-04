using Hechao.Contracts;

namespace Hechao.Launcher.ViewModels;

internal static class ServerCatalogPresentation
{
    public static bool IsPlayerServer(ServerSummary server) =>
        !string.Equals(server.Id, "lobby", StringComparison.OrdinalIgnoreCase);

    public static bool IsActivityServer(ServerSummary server) =>
        // Cached catalogs from before CatalogSection used survival2 as the sole permanent server.
        server.CatalogSection switch
        {
            ServerCatalogSection.Activity => true,
            ServerCatalogSection.Permanent => false,
            _ => !string.Equals(
                server.Id,
                "survival2",
                StringComparison.OrdinalIgnoreCase),
        };

    public static string FormatSchedule(
        DateTimeOffset? opensAt,
        DateTimeOffset? closesAt)
    {
        if (opensAt is null && closesAt is null)
        {
            return "开放时间待定";
        }

        if (opensAt is not null && closesAt is not null)
        {
            return $"本地时间 {FormatLocalTime(opensAt.Value)} - {FormatLocalTime(closesAt.Value)}";
        }

        return opensAt is not null
            ? $"本地时间 {FormatLocalTime(opensAt.Value)} 开放"
            : $"开放至本地时间 {FormatLocalTime(closesAt!.Value)}";
    }

    public static string FormatCompactSchedule(
        DateTimeOffset? opensAt,
        DateTimeOffset? closesAt)
    {
        if (opensAt is null && closesAt is null)
        {
            return "开放时间待定";
        }

        if (opensAt is not null && closesAt is not null)
        {
            var localOpen = opensAt.Value.ToLocalTime();
            var localClose = closesAt.Value.ToLocalTime();
            return localOpen.Date == localClose.Date
                ? $"开放 {localOpen:M月d日 HH:mm} - {localClose:HH:mm}"
                : $"开放 {localOpen:M月d日 HH:mm} - {localClose:M月d日 HH:mm}";
        }

        return opensAt is not null
            ? $"开放 {FormatLocalTime(opensAt.Value)}"
            : $"开放至 {FormatLocalTime(closesAt!.Value)}";
    }

    private static string FormatLocalTime(DateTimeOffset value) =>
        value.ToLocalTime().ToString("M月d日 HH:mm");
}

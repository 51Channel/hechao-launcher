using System.Globalization;
using System.Windows.Data;
using Hechao.Contracts;
using Hechao.Launcher.ViewModels;

namespace Hechao.Launcher.Converters;

public sealed class ServerListMetaTextConverter : IValueConverter
{
    public object Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        if (value is not ServerSummary server)
        {
            return string.Empty;
        }

        return ServerCatalogPresentation.IsActivityServer(server)
            ? ServerCatalogPresentation.FormatCompactSchedule(
                server.OpensAt,
                server.ClosesAt)
            : server.MinecraftVersion;
    }

    public object ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture) =>
        throw new NotSupportedException();
}

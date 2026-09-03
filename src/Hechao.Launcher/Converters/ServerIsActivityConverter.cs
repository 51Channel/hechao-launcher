using System.Globalization;
using System.Windows.Data;
using Hechao.Contracts;
using Hechao.Launcher.ViewModels;

namespace Hechao.Launcher.Converters;

public sealed class ServerIsActivityConverter : IValueConverter
{
    public object Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture) =>
        value is ServerSummary server &&
        ServerCatalogPresentation.IsActivityServer(server);

    public object ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture) =>
        throw new NotSupportedException();
}

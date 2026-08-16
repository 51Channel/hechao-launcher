using System.Windows;

namespace Hechao.Modpack.Inspector;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var window = new MainWindow();
        window.Show();
        if (e.Args.FirstOrDefault() is { Length: > 0 } archivePath)
        {
            window.BeginInspection(archivePath);
        }
    }
}

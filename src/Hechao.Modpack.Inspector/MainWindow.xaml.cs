using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Hechao.Modpack.Inspector;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    public void BeginInspection(string archivePath)
    {
        Dispatcher.BeginInvoke(async () => await viewModel.InspectAsync(archivePath));
    }

    private async void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择要检查的整合包",
            Filter = "整合包 (*.zip;*.mrpack)|*.zip;*.mrpack|ZIP 归档 (*.zip)|*.zip|Modrinth 整合包 (*.mrpack)|*.mrpack",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == true)
        {
            await viewModel.InspectAsync(dialog.FileName);
        }
    }

    private async void Reinspect_Click(object sender, RoutedEventArgs e) =>
        await viewModel.ReinspectAsync();

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (viewModel.Report is null)
        {
            return;
        }

        var suggestedName = Path.GetFileNameWithoutExtension(viewModel.Report.ArchiveName) + "-部署检查报告.json";
        var dialog = new SaveFileDialog
        {
            Title = "导出部署检查报告",
            Filter = "JSON 报告 (*.json)|*.json",
            FileName = suggestedName,
            AddExtension = true,
            DefaultExt = ".json"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            await viewModel.ExportAsync(dialog.FileName);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                $"报告导出失败：{exception.Message}",
                "无法导出报告",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void Filter_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string filter })
        {
            viewModel.SetFilter(filter);
        }
    }

    private void Window_DragEnter(object sender, DragEventArgs e) =>
        SetDragEffect(e);

    private void Window_DragOver(object sender, DragEventArgs e) =>
        SetDragEffect(e);

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (!TryGetArchivePath(e, out var archivePath))
        {
            return;
        }

        await viewModel.InspectAsync(archivePath);
    }

    private void SetDragEffect(DragEventArgs e)
    {
        e.Effects = !viewModel.IsBusy && TryGetArchivePath(e, out _)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private static bool TryGetArchivePath(DragEventArgs e, out string archivePath)
    {
        archivePath = string.Empty;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop) ||
            e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: 1 } files)
        {
            return false;
        }

        var extension = Path.GetExtension(files[0]);
        if (!extension.Equals(".zip", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".mrpack", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        archivePath = files[0];
        return true;
    }
}

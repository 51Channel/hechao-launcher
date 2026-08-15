using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Hechao.Launcher.Mac.ViewModels;

namespace Hechao.Launcher.Mac;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private async void ChooseClientDirectory_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (DataContext is not LauncherMacViewModel viewModel)
        {
            return;
        }

        var folders = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = "选择赫朝游戏数据目录",
                AllowMultiple = false
            });
        var folder = folders.FirstOrDefault();
        if (folder?.TryGetLocalPath() is { } path)
        {
            viewModel.SetClientDirectory(path);
        }
    }
}

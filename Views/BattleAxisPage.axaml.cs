using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using EndFieldFightHelper.ViewModels;

namespace EndFieldFightHelper.Views;

public partial class BattleAxisPage : UserControl
{
    public BattleAxisPage()
    {
        InitializeComponent();
    }

    private async void OnImportClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择 Endaxis JSON 文件",
            FileTypeFilter = [new FilePickerFileType("JSON 文件") { Patterns = ["*.json"] }],
            AllowMultiple = false,
        });

        if (files.Count == 0) return;

        if (DataContext is BattleAxisViewModel vm)
        {
            var path = files[0].Path.LocalPath;
            await vm.LoadFromFile(path);
        }
    }
}

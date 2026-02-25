using Avalonia.Controls;
using Avalonia.Interactivity;
using EndFieldFightHelper.ViewModels;
using SukiUI.Models;

namespace EndFieldFightHelper.Views;

public partial class SettingsPage : UserControl
{
    public SettingsPage()
    {
        InitializeComponent();
    }

    private void OnColorThemeClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: SukiColorTheme theme }
            && DataContext is SettingsViewModel vm)
        {
            vm.SetColorThemeCommand.Execute(theme);
        }
    }
}

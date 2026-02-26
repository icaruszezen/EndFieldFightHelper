using Avalonia.Controls;
using Avalonia.Interactivity;
using EndFieldFightHelper.Models;
using EndFieldFightHelper.ViewModels;

namespace EndFieldFightHelper.Views;

public partial class InputTestPage : UserControl
{
    public InputTestPage()
    {
        InitializeComponent();

        MethodComboBox.SelectionChanged += (_, _) =>
        {
            if (MethodComboBox.SelectedItem is ComboBoxItem item && item.Tag is InputMethod method)
            {
                if (DataContext is InputTestViewModel vm)
                    vm.SelectedMethod = method;
            }
        };

        MouseButtonComboBox.SelectionChanged += (_, _) =>
        {
            if (MouseButtonComboBox.SelectedItem is ComboBoxItem item && item.Tag is MouseButton button)
            {
                if (DataContext is InputTestViewModel vm)
                    vm.SelectedMouseButton = button;
            }
        };
    }

    private void QuickKey_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tagStr
            && int.TryParse(tagStr, out var vk)
            && DataContext is InputTestViewModel vm)
        {
            vm.KeyCode = vk;
        }
    }
}

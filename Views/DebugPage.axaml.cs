using Avalonia.Controls;
using Avalonia.Interactivity;
using EndFieldFightHelper.ViewModels;

namespace EndFieldFightHelper.Views;

public partial class DebugPage : UserControl
{
    public DebugPage()
    {
        InitializeComponent();
    }

    private void OnInnerTabChecked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DebugViewModel vm && sender is RadioButton rb && rb.IsChecked == true)
            vm.SelectedTabIndex = ReferenceEquals(rb, DetectionTab) ? 1 : 0;
    }
}

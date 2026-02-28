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
        {
            if (ReferenceEquals(rb, CaptureTab)) vm.SelectedTabIndex = 0;
            else if (ReferenceEquals(rb, DetectionTab)) vm.SelectedTabIndex = 1;
            else if (ReferenceEquals(rb, CharacterTab)) vm.SelectedTabIndex = 2;
        }
    }
}

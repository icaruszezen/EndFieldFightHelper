using System.Collections.Specialized;
using Avalonia.Controls;
using EndFieldFightHelper.ViewModels;

namespace EndFieldFightHelper.Views;

public partial class HomePage : UserControl
{
    public HomePage()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (DataContext is HomeViewModel vm)
        {
            vm.LogMessages.CollectionChanged += OnLogMessagesChanged;
        }
    }

    private void OnLogMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var scrollViewer = this.FindControl<ScrollViewer>("LogScrollViewer");
        scrollViewer?.ScrollToEnd();
    }
}

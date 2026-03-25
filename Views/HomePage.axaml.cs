using System;
using System.Collections.Specialized;
using Avalonia.Controls;
using EndFieldFightHelper.ViewModels;

namespace EndFieldFightHelper.Views;

public partial class HomePage : UserControl
{
    private HomeViewModel? _previousVm;

    public HomePage()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_previousVm != null)
            _previousVm.LogMessages.CollectionChanged -= OnLogMessagesChanged;

        _previousVm = DataContext as HomeViewModel;

        if (_previousVm != null)
            _previousVm.LogMessages.CollectionChanged += OnLogMessagesChanged;
    }

    private void OnLogMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var scrollViewer = this.FindControl<ScrollViewer>("LogScrollViewer");
        scrollViewer?.ScrollToEnd();
    }
}

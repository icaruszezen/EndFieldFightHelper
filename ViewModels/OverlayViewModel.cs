using System;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EndFieldFightHelper.Models;

namespace EndFieldFightHelper.ViewModels;

public partial class OverlayViewModel : ViewModelBase
{
    private const int MaxLogLines = 50;

    [ObservableProperty]
    private bool _isEditMode;

    [ObservableProperty]
    private double _backgroundOpacity = OverlayDefaults.Opacity;

    public ObservableCollection<string> LogMessages { get; } = new();

    public event Action<double, double, double, double>? PositionSizeChanged;

    public OverlayViewModel()
    {
    }

    public void AddLog(string message)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            AddLogCore(message);
        }
        else
        {
            Dispatcher.UIThread.Post(() => AddLogCore(message));
        }
    }

    private void AddLogCore(string message)
    {
        LogMessages.Add(message);
        while (LogMessages.Count > MaxLogLines)
            LogMessages.RemoveAt(0);
    }

    [RelayCommand]
    private void ToggleEditMode()
    {
        IsEditMode = !IsEditMode;
    }

    public void NotifyPositionSizeChanged(double x, double y, double width, double height)
    {
        PositionSizeChanged?.Invoke(x, y, width, height);
    }
}

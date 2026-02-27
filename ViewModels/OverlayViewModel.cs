using System;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace EndFieldFightHelper.ViewModels;

public partial class OverlayViewModel : ViewModelBase
{
    private const int MaxLogLines = 50;

    [ObservableProperty]
    private string _overlayText = "自定义内容示例";

    [ObservableProperty]
    private bool _isEditMode;

    public ObservableCollection<string> LogMessages { get; } = new();

    public event Action<bool>? EditModeChanged;
    public event Action<double, double, double, double>? PositionSizeChanged;

    public OverlayViewModel()
    {
    }

    public void UpdateText(string text)
    {
        OverlayText = text;
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

    partial void OnIsEditModeChanged(bool value)
    {
        EditModeChanged?.Invoke(value);
    }

    public void NotifyPositionSizeChanged(double x, double y, double width, double height)
    {
        PositionSizeChanged?.Invoke(x, y, width, height);
    }
}

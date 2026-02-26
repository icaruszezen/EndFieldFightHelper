using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EndFieldFightHelper.Helpers;
using EndFieldFightHelper.Models;
using EndFieldFightHelper.Services;

namespace EndFieldFightHelper.ViewModels;

public partial class InputTestViewModel : ViewModelBase
{
    private readonly IInputService _inputService;
    private CancellationTokenSource? _waitCts;

    [ObservableProperty]
    private ObservableCollection<WindowInfo> _windows = new();

    [ObservableProperty]
    private WindowInfo? _selectedWindow;

    [ObservableProperty]
    private InputMethod _selectedMethod = InputMethod.PostMessage;

    [ObservableProperty]
    private bool _waitForForeground;

    [ObservableProperty]
    private bool _isWaitingForForeground;

    [ObservableProperty]
    private string _waitStatusText = "";

    [ObservableProperty]
    private string _statusMessage = "选择一个窗口开始测试";

    // Keyboard
    [ObservableProperty]
    private int _keyCode = 0x41; // 'A'

    // Mouse
    [ObservableProperty]
    private int _mouseX = 100;

    [ObservableProperty]
    private int _mouseY = 100;

    [ObservableProperty]
    private MouseButton _selectedMouseButton = MouseButton.Left;

    // Simulated move
    [ObservableProperty]
    private int _fromX;

    [ObservableProperty]
    private int _fromY;

    [ObservableProperty]
    private int _toX = 200;

    [ObservableProperty]
    private int _toY = 200;

    [ObservableProperty]
    private int _moveDuration = 500;

    [ObservableProperty]
    private int _moveSteps = 20;

    [ObservableProperty]
    private ObservableCollection<string> _logMessages = new();

    public InputTestViewModel(IInputService inputService)
    {
        _inputService = inputService;
        RefreshWindows();
    }

    partial void OnSelectedMethodChanged(InputMethod value)
    {
        _inputService.Method = value;
        AddLog($"切换输入模式: {value}");
    }

    [RelayCommand]
    private void RefreshWindows()
    {
        Windows.Clear();
        var windows = Win32Helper.GetVisibleWindows();
        foreach (var w in windows)
            Windows.Add(w);
        StatusMessage = $"找到 {Windows.Count} 个窗口";
    }

    [RelayCommand]
    private void ClearLog()
    {
        LogMessages.Clear();
    }

    [RelayCommand]
    private void CancelWait()
    {
        _waitCts?.Cancel();
    }

    // --- Keyboard Commands ---

    [RelayCommand]
    private async Task SendKeyPressAsync()
    {
        if (!TryGetHandle(out var hWnd)) return;
        if (!await EnsureForegroundAsync(hWnd)) return;
        await _inputService.SendKeyPressAsync(hWnd, KeyCode);
        AddLog($"KeyPress VK=0x{KeyCode:X2}");
    }

    [RelayCommand]
    private async Task SendKeyDownAsync()
    {
        if (!TryGetHandle(out var hWnd)) return;
        if (!await EnsureForegroundAsync(hWnd)) return;
        _inputService.SendKeyDown(hWnd, KeyCode);
        AddLog($"KeyDown VK=0x{KeyCode:X2}");
    }

    [RelayCommand]
    private async Task SendKeyUpAsync()
    {
        if (!TryGetHandle(out var hWnd)) return;
        if (!await EnsureForegroundAsync(hWnd)) return;
        _inputService.SendKeyUp(hWnd, KeyCode);
        AddLog($"KeyUp VK=0x{KeyCode:X2}");
    }

    // --- Mouse Button Commands ---

    [RelayCommand]
    private async Task SendMouseClickAsync()
    {
        if (!TryGetHandle(out var hWnd)) return;
        if (!await EnsureForegroundAsync(hWnd)) return;
        await _inputService.SendMouseClickAsync(hWnd, SelectedMouseButton, MouseX, MouseY);
        AddLog($"MouseClick {SelectedMouseButton} ({MouseX},{MouseY})");
    }

    [RelayCommand]
    private async Task SendMouseDownAsync()
    {
        if (!TryGetHandle(out var hWnd)) return;
        if (!await EnsureForegroundAsync(hWnd)) return;
        _inputService.SendMouseDown(hWnd, SelectedMouseButton, MouseX, MouseY);
        AddLog($"MouseDown {SelectedMouseButton} ({MouseX},{MouseY})");
    }

    [RelayCommand]
    private async Task SendMouseUpAsync()
    {
        if (!TryGetHandle(out var hWnd)) return;
        if (!await EnsureForegroundAsync(hWnd)) return;
        _inputService.SendMouseUp(hWnd, SelectedMouseButton, MouseX, MouseY);
        AddLog($"MouseUp {SelectedMouseButton} ({MouseX},{MouseY})");
    }

    // --- Mouse Move Commands ---

    [RelayCommand]
    private async Task SendMouseMoveAsync()
    {
        if (!TryGetHandle(out var hWnd)) return;
        if (!await EnsureForegroundAsync(hWnd)) return;
        _inputService.SendMouseMove(hWnd, MouseX, MouseY);
        AddLog($"MouseMove ({MouseX},{MouseY})");
    }

    [RelayCommand]
    private async Task SimulateMouseMoveAsync()
    {
        if (!TryGetHandle(out var hWnd)) return;
        if (!await EnsureForegroundAsync(hWnd)) return;
        AddLog($"SimulateMove ({FromX},{FromY})->({ToX},{ToY}) {MoveDuration}ms {MoveSteps}步");
        await _inputService.SimulateMouseMoveAsync(hWnd, FromX, FromY, ToX, ToY, MoveDuration, MoveSteps);
        AddLog("SimulateMove 完成");
    }

    // --- Helpers ---

    private bool TryGetHandle(out IntPtr hWnd)
    {
        if (SelectedWindow == null)
        {
            StatusMessage = "请先选择一个窗口";
            hWnd = IntPtr.Zero;
            return false;
        }
        hWnd = SelectedWindow.Handle;
        return true;
    }

    private async Task<bool> EnsureForegroundAsync(IntPtr hWnd)
    {
        if (!WaitForForeground)
            return true;

        _waitCts?.Cancel();
        _waitCts = new CancellationTokenSource();
        var ct = _waitCts.Token;

        IsWaitingForForeground = true;
        WaitStatusText = "等待目标窗口到前台，请点击游戏窗口...";
        AddLog("等待目标窗口成为前台...");

        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (Win32Helper.GetForegroundWindow() == hWnd)
                {
                    IsWaitingForForeground = false;
                    WaitStatusText = "";
                    AddLog("目标窗口已到前台，执行操作");
                    return true;
                }
                await Task.Delay(100, ct);
            }
        }
        catch (OperationCanceledException)
        {
        }

        IsWaitingForForeground = false;
        WaitStatusText = "";
        AddLog("等待已取消");
        return false;
    }

    private void AddLog(string message)
    {
        var entry = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        LogMessages.Insert(0, entry);
    }
}

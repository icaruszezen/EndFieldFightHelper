using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using EndFieldFightHelper.Models;
using EndFieldFightHelper.Services;
using EndFieldFightHelper.ViewModels;
using EndFieldFightHelper.Views;
using SukiUI.Controls;
using SukiUI.Toasts;

namespace EndFieldFightHelper;

public partial class MainWindow : SukiWindow
{
    private readonly MainWindowViewModel _viewModel;
    private readonly HotkeyService _hotkeyService;
    private TrayIconService? _trayIconService;
    private bool _isClosingConfirmed;
    public static ISukiToastManager ToastManager { get; } = new SukiToastManager();

    public MainWindow()
    {
        InitializeComponent();
        
        ToastHost.Manager = ToastManager;
        
        _viewModel = new MainWindowViewModel(ToastManager);
        DataContext = _viewModel;
        
        _hotkeyService = new HotkeyService();
        _hotkeyService.ScreenshotHotkeyPressed += OnScreenshotHotkeyPressed;
        _hotkeyService.RefreshHotkeyPressed += OnRefreshHotkeyPressed;
        
        Loaded += OnLoaded;
        Closed += OnClosed;
        Closing += OnClosing;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle != IntPtr.Zero)
        {
            _hotkeyService.Register(handle);
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _hotkeyService.Unregister();
        if (_viewModel.SettingsViewModel is IDisposable disposableSettings)
        {
            disposableSettings.Dispose();
        }

        if (Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    private void OnScreenshotHotkeyPressed(object? sender, EventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _viewModel.ScreenshotViewModel.TriggerCapture();
        });
    }

    private void OnRefreshHotkeyPressed(object? sender, EventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _viewModel.ScreenshotViewModel.RefreshWindowsCommand.Execute(null);
        });
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_isClosingConfirmed)
        {
            _trayIconService?.Dispose();
            return;
        }

        var closeAction = _viewModel.SettingsViewModel.CloseAction;

        if (closeAction == CloseAction.MinimizeToTray)
        {
            e.Cancel = true;
            MinimizeToTray();
            return;
        }

        if (closeAction == CloseAction.Exit)
        {
            _isClosingConfirmed = true;
            return;
        }

        e.Cancel = true;
        await ShowClosingDialogAsync();
    }

    private async System.Threading.Tasks.Task ShowClosingDialogAsync()
    {
        var dialog = new ClosingDialog();
        await dialog.ShowDialog(this);

        var action = dialog.SelectedAction;
        var remember = dialog.RememberChoice;

        if (action == CloseAction.Ask)
        {
            return;
        }

        if (remember)
        {
            _viewModel.SettingsViewModel.CloseAction = action;
        }

        if (action == CloseAction.MinimizeToTray)
        {
            MinimizeToTray();
        }
        else if (action == CloseAction.Exit)
        {
            _isClosingConfirmed = true;
            Close();
        }
    }

    private void MinimizeToTray()
    {
        _trayIconService ??= new TrayIconService(this);
        _trayIconService.MinimizeToTray();
    }
}

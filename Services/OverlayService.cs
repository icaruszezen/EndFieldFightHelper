using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using EndFieldFightHelper.Helpers;
using EndFieldFightHelper.ViewModels;
using EndFieldFightHelper.Views;

namespace EndFieldFightHelper.Services;

public sealed class OverlayService : IDisposable
{
    private readonly SettingsViewModel _settingsViewModel;
    private OverlayWindow? _window;
    private bool _lastClickThrough;
    private OverlayViewModel? _overlayViewModel;

    public OverlayService(SettingsViewModel settingsViewModel)
    {
        _settingsViewModel = settingsViewModel;
    }

    public void ApplySettings(
        bool enabled,
        bool clickThrough,
        double x,
        double y,
        double width,
        double height,
        double opacity)
    {
        _lastClickThrough = clickThrough;

        Dispatcher.UIThread.Post(() =>
        {
            if (!enabled)
            {
                HideWindow();
                return;
            }

            var window = EnsureWindow();
            window.Opacity = Math.Clamp(opacity, 0.1, 1.0);
            window.Width = Math.Max(50, width);
            window.Height = Math.Max(30, height);
            window.Position = new PixelPoint((int)Math.Round(x), (int)Math.Round(y));

            if (!window.IsVisible)
            {
                window.Show();
            }

            ApplyClickThrough(window, _lastClickThrough);
        });
    }

    private OverlayWindow EnsureWindow()
    {
        if (_window != null)
        {
            return _window;
        }

        _window = new OverlayWindow
        {
            DataContext = _overlayViewModel != null ? _overlayViewModel : _settingsViewModel
        };
        _window.Opened += (_, _) => ApplyClickThrough(_window, _lastClickThrough);
        _window.Closed += (_, _) => _window = null;

        return _window;
    }

    public void SetOverlayDataContext(OverlayViewModel overlayViewModel)
    {
        _overlayViewModel = overlayViewModel;
        if (_window != null)
        {
            _window.DataContext = overlayViewModel;
        }
    }

    private static void ApplyClickThrough(Window? window, bool clickThrough)
    {
        if (window == null)
        {
            return;
        }

        var handle = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        Win32Helper.SetWindowExStyle(handle, Win32Helper.WS_EX_TOOLWINDOW, true);
        Win32Helper.SetWindowExStyle(handle, Win32Helper.WS_EX_LAYERED, true);
        Win32Helper.SetWindowExStyle(handle, Win32Helper.WS_EX_TRANSPARENT, clickThrough);
    }

    private void HideWindow()
    {
        if (_window == null)
        {
            return;
        }

        _window.Hide();
    }

    public void Dispose()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_window == null)
            {
                return;
            }

            _window.Close();
            _window = null;
        });
    }
}

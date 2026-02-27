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
    private OverlayWindow? _window;
    private OverlayViewModel? _overlayViewModel;

    public event Action<double, double, double, double>? OverlayPositionSizeChanged;

    public OverlayService()
    {
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
        Dispatcher.UIThread.Post(() =>
        {
            if (!enabled)
            {
                HideWindow();
                return;
            }

            var window = EnsureWindow();
            if (window == null) return;

            window.Opacity = Math.Clamp(opacity, 0.1, 1.0);
            window.Width = Math.Max(50, width);
            window.Height = Math.Max(30, height);
            window.Position = new PixelPoint((int)Math.Round(x), (int)Math.Round(y));

            if (!window.IsVisible)
            {
                window.Show();
            }

            ApplyWindowStyles(window);
        });
    }

    private OverlayWindow? EnsureWindow()
    {
        if (_window != null)
        {
            return _window;
        }

        if (_overlayViewModel == null) return null;

        _window = new OverlayWindow
        {
            DataContext = _overlayViewModel
        };
        _window.Opened += (_, _) => ApplyWindowStyles(_window);
        _window.Closed += (_, _) => _window = null;

        return _window;
    }

    public void SetOverlayDataContext(OverlayViewModel overlayViewModel)
    {
        if (_overlayViewModel != null)
        {
            _overlayViewModel.PositionSizeChanged -= OnPositionSizeChanged;
        }

        _overlayViewModel = overlayViewModel;
        _overlayViewModel.PositionSizeChanged += OnPositionSizeChanged;

        if (_window != null)
        {
            _window.DataContext = overlayViewModel;
        }
    }

    private void OnPositionSizeChanged(double x, double y, double width, double height)
    {
        OverlayPositionSizeChanged?.Invoke(x, y, width, height);
    }

    private static void ApplyWindowStyles(Window? window)
    {
        if (window == null) return;

        var handle = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero) return;

        Win32Helper.SetWindowExStyle(handle, Win32Helper.WS_EX_TOOLWINDOW, true);
        Win32Helper.SetWindowExStyle(handle, Win32Helper.WS_EX_LAYERED, true);
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
        if (_overlayViewModel != null)
        {
            _overlayViewModel.PositionSizeChanged -= OnPositionSizeChanged;
        }

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

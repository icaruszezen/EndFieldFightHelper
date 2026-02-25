using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace EndFieldFightHelper.Services;

public class TrayIconService : IDisposable
{
    private readonly Window _mainWindow;
    private TrayIcon? _trayIcon;
    private bool _disposed;

    public TrayIconService(Window mainWindow)
    {
        _mainWindow = mainWindow;
    }

    public void MinimizeToTray()
    {
        if (_trayIcon == null)
        {
            CreateTrayIcon();
        }

        _mainWindow.Hide();
    }

    private void CreateTrayIcon()
    {
        _trayIcon = new TrayIcon
        {
            ToolTipText = "EndField Fight Helper",
            IsVisible = true
        };

        try
        {
            var iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "icon.ico");
            if (System.IO.File.Exists(iconPath))
            {
                _trayIcon.Icon = new WindowIcon(iconPath);
            }
        }
        catch
        {
            // Icon loading failed, continue without icon
        }

        var menu = new NativeMenu();

        var showItem = new NativeMenuItem("显示窗口");
        showItem.Click += (_, _) => ShowWindow();
        menu.Add(showItem);

        menu.Add(new NativeMenuItemSeparator());

        var exitItem = new NativeMenuItem("退出");
        exitItem.Click += (_, _) => ExitApplication();
        menu.Add(exitItem);

        _trayIcon.Menu = menu;
        _trayIcon.Clicked += (_, _) => ShowWindow();
    }

    private void ShowWindow()
    {
        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    private void ExitApplication()
    {
        Dispose();
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_trayIcon != null)
        {
            _trayIcon.IsVisible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using EndFieldFightHelper.Models;

namespace EndFieldFightHelper.Helpers;

public static class Win32Helper
{
    #region Window Enumeration

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    public static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    #endregion

    #region Window Rect

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    public static RECT GetWindowRectDwm(IntPtr hWnd)
    {
        if (DwmGetWindowAttribute(hWnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT rect, Marshal.SizeOf<RECT>()) == 0)
        {
            return rect;
        }
        GetWindowRect(hWnd, out rect);
        return rect;
    }

    #endregion

    #region Monitor

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    public const uint MONITOR_DEFAULTTONULL = 0;
    public const uint MONITOR_DEFAULTTOPRIMARY = 1;
    public const uint MONITOR_DEFAULTTONEAREST = 2;

    #endregion

    #region GDI Functions

    [DllImport("user32.dll")]
    public static extern IntPtr GetWindowDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

    [DllImport("gdi32.dll")]
    public static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    public static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight,
        IntPtr hdcSrc, int nXSrc, int nYSrc, uint dwRop);

    public const uint SRCCOPY = 0x00CC0020;

    #endregion

    #region PrintWindow

    [DllImport("user32.dll")]
    public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);

    public const uint PW_CLIENTONLY = 0x00000001;
    public const uint PW_RENDERFULLCONTENT = 0x00000002;

    #endregion

    #region Hotkey

    [DllImport("user32.dll")]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;

    public const int WM_HOTKEY = 0x0312;

    #endregion

    #region Window Styles

    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TRANSPARENT = 0x00000020;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_LAYERED = 0x00080000;

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_FRAMECHANGED = 0x0020;

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern IntPtr GetWindowLongPtr32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern IntPtr SetWindowLongPtr32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);

    private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
    {
        return IntPtr.Size == 8
            ? GetWindowLongPtr64(hWnd, nIndex)
            : GetWindowLongPtr32(hWnd, nIndex);
    }

    private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
    {
        return IntPtr.Size == 8
            ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong)
            : SetWindowLongPtr32(hWnd, nIndex, dwNewLong);
    }

    public static void SetWindowExStyle(IntPtr hWnd, int exStyle, bool enabled)
    {
        var styles = GetWindowLongPtr(hWnd, GWL_EXSTYLE).ToInt64();
        if (enabled)
        {
            styles |= exStyle;
        }
        else
        {
            styles &= ~exStyle;
        }

        SetWindowLongPtr(hWnd, GWL_EXSTYLE, new IntPtr(styles));
        SetWindowPos(hWnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
    }

    #endregion

    #region Helper Methods

    public static List<WindowInfo> GetVisibleWindows()
    {
        var windows = new List<WindowInfo>();
        var shellWindow = GetShellWindow();

        EnumWindows((hWnd, lParam) =>
        {
            if (hWnd == shellWindow) return true;
            if (!IsWindowVisible(hWnd)) return true;

            var length = GetWindowTextLength(hWnd);
            if (length == 0) return true;

            var sb = new StringBuilder(length + 1);
            GetWindowText(hWnd, sb, sb.Capacity);
            var title = sb.ToString();

            if (string.IsNullOrWhiteSpace(title)) return true;

            GetWindowThreadProcessId(hWnd, out uint processId);
            var processName = string.Empty;

            try
            {
                var process = Process.GetProcessById((int)processId);
                processName = process.ProcessName;
            }
            catch
            {
                // Process may have exited
            }

            windows.Add(new WindowInfo
            {
                Handle = hWnd,
                Title = title,
                ProcessName = processName,
                ProcessId = (int)processId
            });

            return true;
        }, IntPtr.Zero);

        return windows;
    }

    #endregion
}

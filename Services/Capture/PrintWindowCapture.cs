using System;
using System.Drawing;
using EndFieldFightHelper.Helpers;

namespace EndFieldFightHelper.Services.Capture;

/// <summary>
/// 使用 PrintWindow API 进行窗口截图。
/// 性能优化：缓存 GDI 资源（内存 DC、兼容位图），避免每次截图重新创建。
/// </summary>
public static class PrintWindowCapture
{
    private static IntPtr _cachedScreenDC;
    private static IntPtr _cachedMemDC;
    private static IntPtr _cachedHBitmap;
    private static IntPtr _cachedOldBitmap;
    private static int _cachedWidth;
    private static int _cachedHeight;
    private static readonly object _lock = new();

    public static Bitmap? Capture(IntPtr hWnd)
    {
        Win32Helper.GetClientRect(hWnd, out var clientRect);
        if (clientRect.Width <= 0 || clientRect.Height <= 0) return null;

        lock (_lock)
        {
            try
            {
                EnsureResources(clientRect.Width, clientRect.Height);
                if (_cachedMemDC == IntPtr.Zero) return null;

                bool success = Win32Helper.PrintWindow(hWnd, _cachedMemDC,
                    Win32Helper.PW_CLIENTONLY | Win32Helper.PW_RENDERFULLCONTENT);
                if (!success)
                {
                    success = Win32Helper.PrintWindow(hWnd, _cachedMemDC, Win32Helper.PW_CLIENTONLY);
                }

                if (!success) return null;

                return Image.FromHbitmap(_cachedHBitmap);
            }
            catch
            {
                ReleaseResources();
                return null;
            }
        }
    }

    public static void Release()
    {
        lock (_lock)
        {
            ReleaseResources();
        }
    }

    private static void EnsureResources(int width, int height)
    {
        if (_cachedMemDC != IntPtr.Zero && _cachedWidth == width && _cachedHeight == height)
            return;

        ReleaseResources();

        _cachedScreenDC = Win32Helper.GetDC(IntPtr.Zero);
        if (_cachedScreenDC == IntPtr.Zero) return;

        _cachedMemDC = Win32Helper.CreateCompatibleDC(_cachedScreenDC);
        if (_cachedMemDC == IntPtr.Zero)
        {
            ReleaseResources();
            return;
        }

        _cachedHBitmap = Win32Helper.CreateCompatibleBitmap(_cachedScreenDC, width, height);
        if (_cachedHBitmap == IntPtr.Zero)
        {
            ReleaseResources();
            return;
        }

        _cachedOldBitmap = Win32Helper.SelectObject(_cachedMemDC, _cachedHBitmap);
        _cachedWidth = width;
        _cachedHeight = height;
    }

    private static void ReleaseResources()
    {
        if (_cachedOldBitmap != IntPtr.Zero && _cachedMemDC != IntPtr.Zero)
            Win32Helper.SelectObject(_cachedMemDC, _cachedOldBitmap);
        if (_cachedHBitmap != IntPtr.Zero)
            Win32Helper.DeleteObject(_cachedHBitmap);
        if (_cachedMemDC != IntPtr.Zero)
            Win32Helper.DeleteDC(_cachedMemDC);
        if (_cachedScreenDC != IntPtr.Zero)
            Win32Helper.ReleaseDC(IntPtr.Zero, _cachedScreenDC);

        _cachedScreenDC = IntPtr.Zero;
        _cachedMemDC = IntPtr.Zero;
        _cachedHBitmap = IntPtr.Zero;
        _cachedOldBitmap = IntPtr.Zero;
        _cachedWidth = 0;
        _cachedHeight = 0;
    }
}

using System;
using System.Drawing;
using EndFieldFightHelper.Helpers;

namespace EndFieldFightHelper.Services.Capture;

public static class BitBltCapture
{
    public static Bitmap? Capture(IntPtr hWnd)
    {
        var rect = Win32Helper.GetWindowRectDwm(hWnd);
        if (rect.Width <= 0 || rect.Height <= 0) return null;

        IntPtr hdcSrc = Win32Helper.GetDC(IntPtr.Zero);
        if (hdcSrc == IntPtr.Zero) return null;

        IntPtr hdcDest = IntPtr.Zero;
        IntPtr hBitmap = IntPtr.Zero;
        IntPtr hOld = IntPtr.Zero;

        try
        {
            hdcDest = Win32Helper.CreateCompatibleDC(hdcSrc);
            hBitmap = Win32Helper.CreateCompatibleBitmap(hdcSrc, rect.Width, rect.Height);
            hOld = Win32Helper.SelectObject(hdcDest, hBitmap);

            bool success = Win32Helper.BitBlt(
                hdcDest, 0, 0, rect.Width, rect.Height,
                hdcSrc, rect.Left, rect.Top, Win32Helper.SRCCOPY);

            Win32Helper.SelectObject(hdcDest, hOld);

            if (!success) return null;

            return Image.FromHbitmap(hBitmap);
        }
        finally
        {
            if (hBitmap != IntPtr.Zero) Win32Helper.DeleteObject(hBitmap);
            if (hdcDest != IntPtr.Zero) Win32Helper.DeleteDC(hdcDest);
            Win32Helper.ReleaseDC(IntPtr.Zero, hdcSrc);
        }
    }
}

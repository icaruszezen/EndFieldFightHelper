using System;
using System.Drawing;
using EndFieldFightHelper.Helpers;

namespace EndFieldFightHelper.Services.Capture;

public static class GdiPlusCapture
{
    public static Bitmap? Capture(IntPtr hWnd)
    {
        var rect = Win32Helper.GetWindowRectDwm(hWnd);
        if (rect.Width <= 0 || rect.Height <= 0) return null;

        var bitmap = new Bitmap(rect.Width, rect.Height);
        using var graphics = Graphics.FromImage(bitmap);
        
        graphics.CopyFromScreen(
            rect.Left, rect.Top,
            0, 0,
            new Size(rect.Width, rect.Height),
            CopyPixelOperation.SourceCopy);

        return bitmap;
    }
}

using System;
using System.Drawing;
using EndFieldFightHelper.Models;
using EndFieldFightHelper.Services.Capture;

namespace EndFieldFightHelper.Services;

public class ScreenshotService : IScreenshotService
{
    private CaptureMethod? _lastMethod;

    public Bitmap? CaptureWindow(IntPtr hWnd, CaptureMethod method)
    {
        if (_lastMethod == CaptureMethod.WindowsGraphicsCapture && method != CaptureMethod.WindowsGraphicsCapture)
        {
            WindowsGraphicsCaptureCapture.Release();
        }
        _lastMethod = method;

        return method switch
        {
            CaptureMethod.PrintWindow => PrintWindowCapture.Capture(hWnd),
            CaptureMethod.GdiPlusCopyFromScreen => GdiPlusCapture.Capture(hWnd),
            CaptureMethod.BitBlt => BitBltCapture.Capture(hWnd),
            CaptureMethod.DxgiDesktopDuplication => DxgiDesktopDuplicationCapture.Capture(hWnd),
            CaptureMethod.WindowsGraphicsCapture => WindowsGraphicsCaptureCapture.Capture(hWnd),
            _ => null
        };
    }

    public Bitmap? CaptureWindow(WindowInfo window, CaptureMethod method)
    {
        return CaptureWindow(window.Handle, method);
    }
}

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
        if (_lastMethod != null && _lastMethod != method)
        {
            ReleaseCaptureMethod(_lastMethod.Value);
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

    private static void ReleaseCaptureMethod(CaptureMethod method)
    {
        switch (method)
        {
            case CaptureMethod.PrintWindow:
                PrintWindowCapture.Release();
                break;
            case CaptureMethod.DxgiDesktopDuplication:
                DxgiDesktopDuplicationCapture.Release();
                break;
            case CaptureMethod.WindowsGraphicsCapture:
                WindowsGraphicsCaptureCapture.Release();
                break;
        }
    }
}

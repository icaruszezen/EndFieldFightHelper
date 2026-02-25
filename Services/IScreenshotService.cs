using System;
using System.Drawing;
using EndFieldFightHelper.Models;

namespace EndFieldFightHelper.Services;

public interface IScreenshotService
{
    Bitmap? CaptureWindow(IntPtr hWnd, CaptureMethod method);
    Bitmap? CaptureWindow(WindowInfo window, CaptureMethod method);
}

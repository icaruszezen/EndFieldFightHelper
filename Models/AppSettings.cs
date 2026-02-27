namespace EndFieldFightHelper.Models;

public class AppSettings
{
    public CaptureMethod DefaultCaptureMethod { get; set; } = CaptureMethod.PrintWindow;
    public bool IsDarkTheme { get; set; } = false;
    public string ScreenshotHotkey { get; set; } = "Ctrl+Shift+S";
    public string RefreshHotkey { get; set; } = "Ctrl+Shift+R";
    public string ThemeColorName { get; set; } = "Orange";
    public bool OverlayEnabled { get; set; } = false;
    public bool OverlayClickThrough { get; set; } = true;
    public double OverlayX { get; set; } = OverlayDefaults.X;
    public double OverlayY { get; set; } = OverlayDefaults.Y;
    public double OverlayWidth { get; set; } = OverlayDefaults.Width;
    public double OverlayHeight { get; set; } = OverlayDefaults.Height;
    public double OverlayOpacity { get; set; } = OverlayDefaults.Opacity;
    public string OverlayText { get; set; } = "自定义内容示例";
    public CloseAction CloseAction { get; set; } = CloseAction.Ask;
    public string YoloModelPath { get; set; } = "";
    public string GpuMode { get; set; } = "cpu";
    public int GpuDeviceId { get; set; } = 0;
    public float YoloConfidence { get; set; } = 0.3f;
    public float YoloIoU { get; set; } = 0.45f;
    public int CaptureFrameRateLimit { get; set; } = 60;
}

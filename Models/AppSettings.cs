namespace EndFieldFightHelper.Models;

public class AppSettings
{
    public CaptureMethod DefaultCaptureMethod { get; set; } = CaptureMethod.PrintWindow;
    public bool IsDarkTheme { get; set; } = false;
    public string ScreenshotHotkey { get; set; } = "Ctrl+Shift+S";
    public string RefreshHotkey { get; set; } = "Ctrl+Shift+R";
    public string ThemeColorName { get; set; } = "Blue";
    public bool OverlayEnabled { get; set; } = false;
    public bool OverlayClickThrough { get; set; } = true;
    public double OverlayX { get; set; } = 50;
    public double OverlayY { get; set; } = 50;
    public double OverlayWidth { get; set; } = 300;
    public double OverlayHeight { get; set; } = 120;
    public double OverlayOpacity { get; set; } = 0.85;
    public string OverlayText { get; set; } = "自定义内容示例";
    public CloseAction CloseAction { get; set; } = CloseAction.Ask;
}

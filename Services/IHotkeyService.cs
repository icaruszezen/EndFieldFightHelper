using System;

namespace EndFieldFightHelper.Services;

public interface IHotkeyService
{
    event EventHandler? ScreenshotHotkeyPressed;
    event EventHandler? RefreshHotkeyPressed;
    
    void Register(IntPtr windowHandle);
    void Unregister();
    void ProcessHotkey(int hotkeyId);
}

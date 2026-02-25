using System;
using EndFieldFightHelper.Helpers;

namespace EndFieldFightHelper.Services;

public class HotkeyService : IHotkeyService
{
    private IntPtr _windowHandle;
    private bool _isRegistered;
    
    private const int HOTKEY_SCREENSHOT = 1;
    private const int HOTKEY_REFRESH = 2;

    public event EventHandler? ScreenshotHotkeyPressed;
    public event EventHandler? RefreshHotkeyPressed;

    public void Register(IntPtr windowHandle)
    {
        if (_isRegistered) return;
        
        _windowHandle = windowHandle;
        
        Win32Helper.RegisterHotKey(
            _windowHandle, 
            HOTKEY_SCREENSHOT, 
            Win32Helper.MOD_CONTROL | Win32Helper.MOD_SHIFT | Win32Helper.MOD_NOREPEAT, 
            0x53); // S key
        
        Win32Helper.RegisterHotKey(
            _windowHandle, 
            HOTKEY_REFRESH, 
            Win32Helper.MOD_CONTROL | Win32Helper.MOD_SHIFT | Win32Helper.MOD_NOREPEAT, 
            0x52); // R key
        
        _isRegistered = true;
    }

    public void Unregister()
    {
        if (!_isRegistered) return;
        
        Win32Helper.UnregisterHotKey(_windowHandle, HOTKEY_SCREENSHOT);
        Win32Helper.UnregisterHotKey(_windowHandle, HOTKEY_REFRESH);
        
        _isRegistered = false;
    }

    public void ProcessHotkey(int hotkeyId)
    {
        switch (hotkeyId)
        {
            case HOTKEY_SCREENSHOT:
                ScreenshotHotkeyPressed?.Invoke(this, EventArgs.Empty);
                break;
            case HOTKEY_REFRESH:
                RefreshHotkeyPressed?.Invoke(this, EventArgs.Empty);
                break;
        }
    }
}

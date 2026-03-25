using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using EndFieldFightHelper.Helpers;
using EndFieldFightHelper.Models;

namespace EndFieldFightHelper.Services;

public class InputService : IInputService
{
    private const int KeyRepeatThresholdMs = 100;
    private const int KeyRepeatIntervalMs = 33;

    public InputMethod Method { get; set; } = InputMethod.PostMessage;

    public void SendKeyDown(IntPtr hWnd, int vkCode)
    {
        if (Method == InputMethod.PostMessage)
            PostMessageKeyDown(hWnd, vkCode);
        else
            SendInputKeyDown(vkCode);
    }

    public void SendKeyUp(IntPtr hWnd, int vkCode)
    {
        if (Method == InputMethod.PostMessage)
            PostMessageKeyUp(hWnd, vkCode);
        else
            SendInputKeyUp(vkCode);
    }

    public async Task SendKeyPressAsync(IntPtr hWnd, int vkCode, int holdMs = 50)
    {
        SendKeyDown(hWnd, vkCode);

        if (Method == InputMethod.PostMessage && holdMs > KeyRepeatThresholdMs)
        {
            var elapsed = 0;
            while (elapsed + KeyRepeatIntervalMs < holdMs)
            {
                await Task.Delay(KeyRepeatIntervalMs);
                elapsed += KeyRepeatIntervalMs;
                PostMessageKeyDownRepeat(hWnd, vkCode);
            }

            var remaining = holdMs - elapsed;
            if (remaining > 0)
                await Task.Delay(remaining);
        }
        else
        {
            await Task.Delay(holdMs);
        }

        SendKeyUp(hWnd, vkCode);
    }

    public void SendMouseDown(IntPtr hWnd, MouseButton button, int x, int y)
    {
        if (Method == InputMethod.PostMessage)
            PostMessageMouseDown(hWnd, button, x, y);
        else
            SendInputMouseDown(hWnd, button, x, y);
    }

    public void SendMouseUp(IntPtr hWnd, MouseButton button, int x, int y)
    {
        if (Method == InputMethod.PostMessage)
            PostMessageMouseUp(hWnd, button, x, y);
        else
            SendInputMouseUp(hWnd, button, x, y);
    }

    public async Task SendMouseClickAsync(IntPtr hWnd, MouseButton button, int x, int y, int holdMs = 50)
    {
        SendMouseDown(hWnd, button, x, y);
        await Task.Delay(holdMs);
        SendMouseUp(hWnd, button, x, y);
    }

    public void SendMouseMove(IntPtr hWnd, int x, int y)
    {
        if (Method == InputMethod.PostMessage)
            PostMessageMouseMove(hWnd, x, y);
        else
            SendInputMouseMove(hWnd, x, y);
    }

    public async Task SimulateMouseMoveAsync(IntPtr hWnd, int fromX, int fromY, int toX, int toY,
        int durationMs = 500, int steps = 20)
    {
        if (steps < 1) steps = 1;
        var delayPerStep = durationMs / steps;

        for (var i = 0; i <= steps; i++)
        {
            var t = (double)i / steps;
            var cx = (int)(fromX + (toX - fromX) * t);
            var cy = (int)(fromY + (toY - fromY) * t);
            SendMouseMove(hWnd, cx, cy);
            if (i < steps)
                await Task.Delay(delayPerStep);
        }
    }

    #region PostMessage Implementation

    private static void PostMessageKeyDown(IntPtr hWnd, int vkCode)
    {
        var scanCode = Win32Helper.MapVirtualKey((uint)vkCode, Win32Helper.MAPVK_VK_TO_VSC);
        var lParam = Win32Helper.MakeKeyLParam(1, scanCode, false, false);
        Win32Helper.PostMessage(hWnd, Win32Helper.WM_KEYDOWN, (IntPtr)vkCode, lParam);
    }

    private static void PostMessageKeyDownRepeat(IntPtr hWnd, int vkCode)
    {
        var scanCode = Win32Helper.MapVirtualKey((uint)vkCode, Win32Helper.MAPVK_VK_TO_VSC);
        var lParam = Win32Helper.MakeKeyLParam(1, scanCode, false, false, previousKeyDown: true);
        Win32Helper.PostMessage(hWnd, Win32Helper.WM_KEYDOWN, (IntPtr)vkCode, lParam);
    }

    private static void PostMessageKeyUp(IntPtr hWnd, int vkCode)
    {
        var scanCode = Win32Helper.MapVirtualKey((uint)vkCode, Win32Helper.MAPVK_VK_TO_VSC);
        var lParam = Win32Helper.MakeKeyLParam(1, scanCode, false, true);
        Win32Helper.PostMessage(hWnd, Win32Helper.WM_KEYUP, (IntPtr)vkCode, lParam);
    }

    private static void PostMessageMouseDown(IntPtr hWnd, MouseButton button, int x, int y)
    {
        var lParam = Win32Helper.MakeLParam(x, y);
        var (msg, wParam) = button switch
        {
            MouseButton.Left => (Win32Helper.WM_LBUTTONDOWN, (IntPtr)Win32Helper.MK_LBUTTON),
            MouseButton.Right => (Win32Helper.WM_RBUTTONDOWN, (IntPtr)Win32Helper.MK_RBUTTON),
            MouseButton.Middle => (Win32Helper.WM_MBUTTONDOWN, (IntPtr)Win32Helper.MK_MBUTTON),
            _ => (Win32Helper.WM_LBUTTONDOWN, (IntPtr)Win32Helper.MK_LBUTTON)
        };
        Win32Helper.PostMessage(hWnd, msg, wParam, lParam);
    }

    private static void PostMessageMouseUp(IntPtr hWnd, MouseButton button, int x, int y)
    {
        var lParam = Win32Helper.MakeLParam(x, y);
        var msg = button switch
        {
            MouseButton.Left => Win32Helper.WM_LBUTTONUP,
            MouseButton.Right => Win32Helper.WM_RBUTTONUP,
            MouseButton.Middle => Win32Helper.WM_MBUTTONUP,
            _ => Win32Helper.WM_LBUTTONUP
        };
        Win32Helper.PostMessage(hWnd, msg, IntPtr.Zero, lParam);
    }

    private static void PostMessageMouseMove(IntPtr hWnd, int x, int y)
    {
        var lParam = Win32Helper.MakeLParam(x, y);
        Win32Helper.PostMessage(hWnd, Win32Helper.WM_MOUSEMOVE, IntPtr.Zero, lParam);
    }

    #endregion

    #region SendInput Implementation

    private static void SendInputKeyDown(int vkCode)
    {
        var scanCode = (ushort)Win32Helper.MapVirtualKey((uint)vkCode, Win32Helper.MAPVK_VK_TO_VSC);
        var input = new Win32Helper.INPUT
        {
            Type = Win32Helper.INPUT_KEYBOARD,
            U = new Win32Helper.INPUTUNION
            {
                Keyboard = new Win32Helper.KEYBDINPUT
                {
                    wVk = (ushort)vkCode,
                    wScan = scanCode,
                    dwFlags = Win32Helper.KEYEVENTF_KEYDOWN
                }
            }
        };
        Win32Helper.SendInput(1, new[] { input }, Marshal.SizeOf<Win32Helper.INPUT>());
    }

    private static void SendInputKeyUp(int vkCode)
    {
        var scanCode = (ushort)Win32Helper.MapVirtualKey((uint)vkCode, Win32Helper.MAPVK_VK_TO_VSC);
        var input = new Win32Helper.INPUT
        {
            Type = Win32Helper.INPUT_KEYBOARD,
            U = new Win32Helper.INPUTUNION
            {
                Keyboard = new Win32Helper.KEYBDINPUT
                {
                    wVk = (ushort)vkCode,
                    wScan = scanCode,
                    dwFlags = Win32Helper.KEYEVENTF_KEYUP
                }
            }
        };
        Win32Helper.SendInput(1, new[] { input }, Marshal.SizeOf<Win32Helper.INPUT>());
    }

    private static void SendInputMouseDown(IntPtr hWnd, MouseButton button, int x, int y)
    {
        var moveInput = MakeAbsoluteMoveInput(hWnd, x, y);
        var flags = button switch
        {
            MouseButton.Left => Win32Helper.MOUSEEVENTF_LEFTDOWN,
            MouseButton.Right => Win32Helper.MOUSEEVENTF_RIGHTDOWN,
            MouseButton.Middle => Win32Helper.MOUSEEVENTF_MIDDLEDOWN,
            _ => Win32Helper.MOUSEEVENTF_LEFTDOWN
        };
        var clickInput = new Win32Helper.INPUT
        {
            Type = Win32Helper.INPUT_MOUSE,
            U = new Win32Helper.INPUTUNION
            {
                Mouse = new Win32Helper.MOUSEINPUT { dwFlags = flags }
            }
        };
        Win32Helper.SendInput(2, new[] { moveInput, clickInput }, Marshal.SizeOf<Win32Helper.INPUT>());
    }

    private static void SendInputMouseUp(IntPtr hWnd, MouseButton button, int x, int y)
    {
        var moveInput = MakeAbsoluteMoveInput(hWnd, x, y);
        var flags = button switch
        {
            MouseButton.Left => Win32Helper.MOUSEEVENTF_LEFTUP,
            MouseButton.Right => Win32Helper.MOUSEEVENTF_RIGHTUP,
            MouseButton.Middle => Win32Helper.MOUSEEVENTF_MIDDLEUP,
            _ => Win32Helper.MOUSEEVENTF_LEFTUP
        };
        var releaseInput = new Win32Helper.INPUT
        {
            Type = Win32Helper.INPUT_MOUSE,
            U = new Win32Helper.INPUTUNION
            {
                Mouse = new Win32Helper.MOUSEINPUT { dwFlags = flags }
            }
        };
        Win32Helper.SendInput(2, new[] { moveInput, releaseInput }, Marshal.SizeOf<Win32Helper.INPUT>());
    }

    private static void SendInputMouseMove(IntPtr hWnd, int x, int y)
    {
        var moveInput = MakeAbsoluteMoveInput(hWnd, x, y);
        Win32Helper.SendInput(1, new[] { moveInput }, Marshal.SizeOf<Win32Helper.INPUT>());
    }

    public void SendRelativeMouseMove(int dx, int dy)
    {
        var input = new Win32Helper.INPUT
        {
            Type = Win32Helper.INPUT_MOUSE,
            U = new Win32Helper.INPUTUNION
            {
                Mouse = new Win32Helper.MOUSEINPUT
                {
                    dx = dx,
                    dy = dy,
                    dwFlags = Win32Helper.MOUSEEVENTF_MOVE
                }
            }
        };
        Win32Helper.SendInput(1, new[] { input }, Marshal.SizeOf<Win32Helper.INPUT>());
    }

    public async Task SimulateRelativeMouseMoveAsync(int dx, int dy, int durationMs = 300, int steps = 15)
    {
        if (steps < 1) steps = 1;
        var delayPerStep = durationMs / steps;

        for (var i = 1; i <= steps; i++)
        {
            var stepDx = dx * i / steps - dx * (i - 1) / steps;
            var stepDy = dy * i / steps - dy * (i - 1) / steps;
            SendRelativeMouseMove(stepDx, stepDy);
            if (i < steps)
                await Task.Delay(delayPerStep);
        }
    }

    private static Win32Helper.INPUT MakeAbsoluteMoveInput(IntPtr hWnd, int x, int y)
    {
        var pt = new Win32Helper.POINT { X = x, Y = y };
        Win32Helper.ClientToScreen(hWnd, ref pt);

        var virtualLeft = Win32Helper.GetSystemMetrics(Win32Helper.SM_XVIRTUALSCREEN);
        var virtualTop = Win32Helper.GetSystemMetrics(Win32Helper.SM_YVIRTUALSCREEN);
        var virtualWidth = Win32Helper.GetSystemMetrics(Win32Helper.SM_CXVIRTUALSCREEN);
        var virtualHeight = Win32Helper.GetSystemMetrics(Win32Helper.SM_CYVIRTUALSCREEN);

        var absoluteX = (int)(((pt.X - virtualLeft) * 65535.0) / virtualWidth);
        var absoluteY = (int)(((pt.Y - virtualTop) * 65535.0) / virtualHeight);

        return new Win32Helper.INPUT
        {
            Type = Win32Helper.INPUT_MOUSE,
            U = new Win32Helper.INPUTUNION
            {
                Mouse = new Win32Helper.MOUSEINPUT
                {
                    dx = absoluteX,
                    dy = absoluteY,
                    dwFlags = Win32Helper.MOUSEEVENTF_MOVE
                             | Win32Helper.MOUSEEVENTF_ABSOLUTE
                             | Win32Helper.MOUSEEVENTF_VIRTUALDESK
                }
            }
        };
    }

    #endregion
}

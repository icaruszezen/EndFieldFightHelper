using System;
using System.Threading.Tasks;
using EndFieldFightHelper.Models;

namespace EndFieldFightHelper.Services;

public interface IInputService
{
    InputMethod Method { get; set; }

    void SendKeyDown(IntPtr hWnd, int vkCode);
    void SendKeyUp(IntPtr hWnd, int vkCode);
    Task SendKeyPressAsync(IntPtr hWnd, int vkCode, int holdMs = 50);

    void SendMouseDown(IntPtr hWnd, MouseButton button, int x, int y);
    void SendMouseUp(IntPtr hWnd, MouseButton button, int x, int y);
    Task SendMouseClickAsync(IntPtr hWnd, MouseButton button, int x, int y, int holdMs = 50);

    void SendMouseMove(IntPtr hWnd, int x, int y);
    Task SimulateMouseMoveAsync(IntPtr hWnd, int fromX, int fromY, int toX, int toY,
        int durationMs = 500, int steps = 20);
}

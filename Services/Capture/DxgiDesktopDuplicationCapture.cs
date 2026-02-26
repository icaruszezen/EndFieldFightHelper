using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using EndFieldFightHelper.Helpers;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace EndFieldFightHelper.Services.Capture;

public static class DxgiDesktopDuplicationCapture
{
    private static ID3D11Device? _cachedDevice;
    private static ID3D11DeviceContext? _cachedContext;
    private static IDXGIOutputDuplication? _cachedDuplication;
    private static IDXGIOutput1? _cachedOutput1;
    private static IntPtr _cachedMonitor;
    private static ID3D11Texture2D? _cachedStagingTexture;
    private static uint _cachedStagingWidth;
    private static uint _cachedStagingHeight;
    private static readonly object _lock = new();

    public static Bitmap? Capture(IntPtr hWnd)
    {
        var windowRect = Win32Helper.GetWindowRectDwm(hWnd);
        if (windowRect.Width <= 0 || windowRect.Height <= 0) return null;

        var hMonitor = Win32Helper.MonitorFromWindow(hWnd, Win32Helper.MONITOR_DEFAULTTONEAREST);
        if (hMonitor == IntPtr.Zero) return null;

        lock (_lock)
        {
            try
            {
                bool cacheValid = EnsureResources(hMonitor);

                if (!cacheValid || _cachedDuplication == null || _cachedDevice == null || _cachedContext == null)
                    return null;

                IDXGIResource? desktopResource = null;
                OutduplFrameInfo frameInfo = default;
                bool gotValidFrame = false;

                for (int attempt = 0; attempt < 10; attempt++)
                {
                    var acquireResult = _cachedDuplication.AcquireNextFrame(100, out frameInfo, out desktopResource);

                    if (acquireResult.Failure)
                    {
                        desktopResource?.Dispose();
                        desktopResource = null;

                        if (acquireResult.Code == unchecked((int)0x887A0026))
                        {
                            ReleaseResources();
                            if (!EnsureResources(hMonitor)) return null;
                        }
                        continue;
                    }

                    if (frameInfo.LastPresentTime != 0 || frameInfo.AccumulatedFrames > 0)
                    {
                        gotValidFrame = true;
                        break;
                    }

                    desktopResource?.Dispose();
                    desktopResource = null;
                    _cachedDuplication.ReleaseFrame();
                    System.Threading.Thread.Sleep(1);
                }

                if (!gotValidFrame || desktopResource == null) return null;

                ID3D11Texture2D desktopTexture;
                try
                {
                    desktopTexture = desktopResource.QueryInterface<ID3D11Texture2D>();
                }
                finally
                {
                    desktopResource.Dispose();
                }

                Texture2DDescription texDesc;
                try
                {
                    texDesc = desktopTexture.Description;

                    EnsureStagingTexture(texDesc.Width, texDesc.Height, texDesc.Format);

                    if (_cachedStagingTexture == null) return null;

                    _cachedContext.CopyResource(_cachedStagingTexture, desktopTexture);
                }
                finally
                {
                    desktopTexture.Dispose();
                }

                _cachedDuplication.ReleaseFrame();

                var mapped = _cachedContext.Map(_cachedStagingTexture, 0, MapMode.Read);

                try
                {
                    var outputDesc = _cachedOutput1!.Description;
                    int desktopLeft = outputDesc.DesktopCoordinates.Left;
                    int desktopTop = outputDesc.DesktopCoordinates.Top;

                    int srcX = windowRect.Left - desktopLeft;
                    int srcY = windowRect.Top - desktopTop;
                    int cropWidth = windowRect.Width;
                    int cropHeight = windowRect.Height;

                    if (srcX < 0) { cropWidth += srcX; srcX = 0; }
                    if (srcY < 0) { cropHeight += srcY; srcY = 0; }
                    int texWidth = (int)texDesc.Width;
                    int texHeight = (int)texDesc.Height;
                    if (srcX + cropWidth > texWidth) cropWidth = texWidth - srcX;
                    if (srcY + cropHeight > texHeight) cropHeight = texHeight - srcY;

                    if (cropWidth <= 0 || cropHeight <= 0) return null;

                    var bitmap = new Bitmap(cropWidth, cropHeight, PixelFormat.Format32bppArgb);
                    var bitmapData = bitmap.LockBits(
                        new Rectangle(0, 0, cropWidth, cropHeight),
                        ImageLockMode.WriteOnly,
                        PixelFormat.Format32bppArgb);

                    try
                    {
                        int srcRowPitch = (int)mapped.RowPitch;
                        int dstRowPitch = bitmapData.Stride;
                        int bytesPerPixel = 4;

                        for (int y = 0; y < cropHeight; y++)
                        {
                            var srcOffset = (srcY + y) * srcRowPitch + srcX * bytesPerPixel;
                            var dstOffset = y * dstRowPitch;

                            unsafe
                            {
                                Buffer.MemoryCopy(
                                    (byte*)mapped.DataPointer + srcOffset,
                                    (byte*)bitmapData.Scan0 + dstOffset,
                                    dstRowPitch,
                                    cropWidth * bytesPerPixel);
                            }
                        }
                    }
                    finally
                    {
                        bitmap.UnlockBits(bitmapData);
                    }

                    return bitmap;
                }
                finally
                {
                    _cachedContext.Unmap(_cachedStagingTexture, 0);
                }
            }
            catch
            {
                return null;
            }
        }
    }

    private static bool EnsureResources(IntPtr hMonitor)
    {
        if (_cachedDevice != null && _cachedDuplication != null && _cachedMonitor == hMonitor)
            return true;

        ReleaseResources();

        try
        {
            D3D11.D3D11CreateDevice(
                null,
                DriverType.Hardware,
                DeviceCreationFlags.BgraSupport,
                null!,
                out _cachedDevice,
                out _cachedContext);

            if (_cachedDevice == null || _cachedContext == null) return false;

            using var dxgiDevice = _cachedDevice.QueryInterface<IDXGIDevice>();
            using var adapter = dxgiDevice.GetAdapter();

            IDXGIOutput? targetOutput = null;
            uint outputIndex = 0;

            while (true)
            {
                var result = adapter.EnumOutputs(outputIndex, out var output);
                if (result.Failure || output == null) break;

                if (output.Description.Monitor == hMonitor)
                {
                    targetOutput = output;
                    break;
                }

                output.Dispose();
                outputIndex++;
            }

            if (targetOutput == null)
            {
                var fallbackResult = adapter.EnumOutputs(0u, out targetOutput);
                if (fallbackResult.Failure || targetOutput == null) return false;
            }

            _cachedOutput1 = targetOutput.QueryInterface<IDXGIOutput1>();
            targetOutput.Dispose();

            _cachedDuplication = _cachedOutput1.DuplicateOutput(_cachedDevice);
            _cachedMonitor = hMonitor;

            return true;
        }
        catch
        {
            ReleaseResources();
            return false;
        }
    }

    private static void EnsureStagingTexture(uint width, uint height, Format format)
    {
        if (_cachedStagingTexture != null && _cachedStagingWidth == width && _cachedStagingHeight == height)
            return;

        _cachedStagingTexture?.Dispose();
        _cachedStagingTexture = null;

        if (_cachedDevice == null) return;

        var stagingDesc = new Texture2DDescription
        {
            Width = width,
            Height = height,
            MipLevels = 1,
            ArraySize = 1,
            Format = format,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CPUAccessFlags = CpuAccessFlags.Read,
            MiscFlags = ResourceOptionFlags.None
        };

        _cachedStagingTexture = _cachedDevice.CreateTexture2D(stagingDesc);
        _cachedStagingWidth = width;
        _cachedStagingHeight = height;
    }

    public static void Release()
    {
        lock (_lock)
        {
            ReleaseResources();
        }
    }

    private static void ReleaseResources()
    {
        _cachedDuplication?.Dispose();
        _cachedDuplication = null;
        _cachedOutput1?.Dispose();
        _cachedOutput1 = null;
        _cachedStagingTexture?.Dispose();
        _cachedStagingTexture = null;
        _cachedContext?.Dispose();
        _cachedContext = null;
        _cachedDevice?.Dispose();
        _cachedDevice = null;
        _cachedMonitor = IntPtr.Zero;
        _cachedStagingWidth = 0;
        _cachedStagingHeight = 0;
    }
}

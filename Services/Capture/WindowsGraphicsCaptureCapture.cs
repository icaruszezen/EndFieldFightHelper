using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace EndFieldFightHelper.Services.Capture;

/// <summary>
/// 使用 Windows Graphics Capture API 进行窗口截图。
/// 支持截取被遮挡的窗口、硬件加速/DirectX 内容。
/// 通过 IsBorderRequired = false 禁用黄色捕获边框（需要 Windows 11+）。
/// 最低系统要求：Windows 10 1903 (build 18362)。
/// </summary>
public static class WindowsGraphicsCaptureCapture
{
    #region COM Interop Definitions

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        IntPtr CreateForWindow(
            [In] IntPtr window,
            [In] ref Guid iid);
    }

    private static readonly Guid IDirect3DDxgiInterfaceAccessIid = new("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");

    private static IntPtr GetDxgiInterfaceFromSurface(object surface, Guid iid)
    {
        IntPtr surfacePtr;
        if (surface is IWinRTObject winrtObj)
        {
            surfacePtr = winrtObj.NativeObject.ThisPtr;
            Marshal.AddRef(surfacePtr);
        }
        else
        {
            surfacePtr = Marshal.GetIUnknownForObject(surface);
        }

        try
        {
            Guid accessIid = IDirect3DDxgiInterfaceAccessIid;
            int hr = Marshal.QueryInterface(surfacePtr, ref accessIid, out IntPtr accessPtr);
            Marshal.ThrowExceptionForHR(hr);
            try
            {
                unsafe
                {
                    IntPtr* vtable = *(IntPtr**)accessPtr;
                    var getInterfaceFn = (delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)vtable[3];
                    IntPtr outPtr;
                    hr = getInterfaceFn(accessPtr, &iid, &outPtr);
                    Marshal.ThrowExceptionForHR(hr);
                    return outPtr;
                }
            }
            finally
            {
                Marshal.Release(accessPtr);
            }
        }
        finally
        {
            Marshal.Release(surfacePtr);
        }
    }

    [DllImport("d3d11.dll", EntryPoint = "CreateDirect3D11DeviceFromDXGIDevice",
        SetLastError = true, PreserveSig = false)]
    private static extern void CreateDirect3D11DeviceFromDXGIDevice(
        IntPtr dxgiDevice,
        out IntPtr graphicsDevice);

    [DllImport("combase.dll", PreserveSig = false)]
    private static extern void RoGetActivationFactory(
        IntPtr activatableClassId,
        [In] ref Guid iid,
        [Out, MarshalAs(UnmanagedType.IUnknown)] out object factory);

    [DllImport("combase.dll", PreserveSig = false)]
    private static extern void WindowsCreateString(
        [MarshalAs(UnmanagedType.LPWStr)] string sourceString,
        int length,
        out IntPtr hstring);

    [DllImport("combase.dll")]
    private static extern void WindowsDeleteString(IntPtr hstring);

    #endregion

    private static ID3D11Device? _cachedDevice;
    private static ID3D11DeviceContext? _cachedContext;
    private static IDirect3DDevice? _cachedD3DDevice;
    private static ID3D11Texture2D? _cachedStagingTexture;
    private static uint _cachedStagingWidth;
    private static uint _cachedStagingHeight;
    private static readonly object _lock = new();

    private static IntPtr _activeHwnd;
    private static GraphicsCaptureItem? _captureItem;
    private static Direct3D11CaptureFramePool? _framePool;
    private static GraphicsCaptureSession? _captureSession;
    private static readonly ManualResetEventSlim _frameReadyEvent = new(false);
    private static volatile bool _sessionInvalidated;
    private static Windows.Graphics.SizeInt32 _framePoolSize;

    private static readonly Guid IGraphicsCaptureItemIid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    private static readonly Guid ID3D11Texture2DIid = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

    public static Bitmap? Capture(IntPtr hWnd)
    {
        if (!GraphicsCaptureSession.IsSupported())
            return null;

        lock (_lock)
        {
            try
            {
                if (!EnsureDevice())
                    return null;

                bool isNewSession = false;

                if (_sessionInvalidated || _framePool == null || _captureSession == null || _activeHwnd != hWnd)
                {
                    DisposeSession();

                    var item = CreateItemForWindow(hWnd);
                    if (item == null) return null;

                    var itemSize = item.Size;
                    if (itemSize.Width <= 0 || itemSize.Height <= 0)
                        return null;

                    _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                        _cachedD3DDevice!,
                        DirectXPixelFormat.B8G8R8A8UIntNormalized,
                        2,
                        itemSize);
                    _framePoolSize = itemSize;

                    _captureSession = _framePool.CreateCaptureSession(item);

                    try { _captureSession.IsBorderRequired = false; }
                    catch { /* Not supported on current OS version */ }

#pragma warning disable CA1416
                    try { _captureSession.IsCursorCaptureEnabled = false; }
                    catch { /* Not supported on current OS version */ }
#pragma warning restore CA1416

                    _captureItem = item;
                    _activeHwnd = hWnd;

                    _framePool.FrameArrived += (_, _) => _frameReadyEvent.Set();
                    item.Closed += (_, _) => _sessionInvalidated = true;

                    _captureSession.StartCapture();
                    isNewSession = true;
                }

                if (isNewSession)
                {
                    _frameReadyEvent.Wait(2000);
                }
                _frameReadyEvent.Reset();

                Direct3D11CaptureFrame? latestFrame = null;
                Direct3D11CaptureFrame? temp;
                while ((temp = _framePool!.TryGetNextFrame()) != null)
                {
                    latestFrame?.Dispose();
                    latestFrame = temp;
                }

                if (latestFrame == null) return null;

                var contentSize = latestFrame.ContentSize;
                if (contentSize.Width > 0 && contentSize.Height > 0 &&
                    (contentSize.Width != _framePoolSize.Width || contentSize.Height != _framePoolSize.Height))
                {
                    var newSize = contentSize;
                    _framePool!.Recreate(_cachedD3DDevice!, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, newSize);
                    _framePoolSize = newSize;

                    latestFrame.Dispose();
                    latestFrame = null;
                    _frameReadyEvent.Reset();
                    _frameReadyEvent.Wait(200);

                    Direct3D11CaptureFrame? resized;
                    while ((resized = _framePool.TryGetNextFrame()) != null)
                    {
                        latestFrame?.Dispose();
                        latestFrame = resized;
                    }
                    if (latestFrame == null) return null;
                }

                try
                {
                    return ConvertFrameToBitmap(latestFrame);
                }
                finally
                {
                    latestFrame.Dispose();
                }
            }
            catch
            {
                return null;
            }
        }
    }

    public static void Release()
    {
        lock (_lock)
        {
            ReleaseResources();
        }
    }

    private static bool EnsureDevice()
    {
        if (_cachedDevice != null && _cachedContext != null && _cachedD3DDevice != null)
            return true;

        ReleaseResources();

        try
        {
            D3D11.D3D11CreateDevice(
                null,
                DriverType.Hardware,
                DeviceCreationFlags.BgraSupport,
                null,
                out _cachedDevice,
                out _cachedContext);

            if (_cachedDevice == null || _cachedContext == null) return false;

            using var dxgiDevice = _cachedDevice.QueryInterface<IDXGIDevice>();
            CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out IntPtr d3dDevicePtr);
            try
            {
                _cachedD3DDevice = MarshalInterface<IDirect3DDevice>.FromAbi(d3dDevicePtr);
            }
            finally
            {
                Marshal.Release(d3dDevicePtr);
            }

            return true;
        }
        catch
        {
            ReleaseResources();
            return false;
        }
    }

    private static GraphicsCaptureItem? CreateItemForWindow(IntPtr hWnd)
    {
        IntPtr hstring = IntPtr.Zero;
        try
        {
            Guid interopGuid = typeof(IGraphicsCaptureItemInterop).GUID;

            const string className = "Windows.Graphics.Capture.GraphicsCaptureItem";
            WindowsCreateString(className, className.Length, out hstring);

            RoGetActivationFactory(
                hstring,
                ref interopGuid,
                out object factoryObj);

            var interop = (IGraphicsCaptureItemInterop)factoryObj;
            Guid itemGuid = IGraphicsCaptureItemIid;
            IntPtr itemPtr = interop.CreateForWindow(hWnd, ref itemGuid);

            try
            {
                return MarshalInspectable<GraphicsCaptureItem>.FromAbi(itemPtr);
            }
            finally
            {
                Marshal.Release(itemPtr);
            }
        }
        catch
        {
            return null;
        }
        finally
        {
            if (hstring != IntPtr.Zero)
                WindowsDeleteString(hstring);
        }
    }

    private static Bitmap? ConvertFrameToBitmap(Direct3D11CaptureFrame frame)
    {
        if (_cachedDevice == null || _cachedContext == null) return null;

        IntPtr texturePtr = GetDxgiInterfaceFromSurface(frame.Surface, ID3D11Texture2DIid);

        using var frameTexture = new ID3D11Texture2D(texturePtr);
        var texDesc = frameTexture.Description;

        var contentSize = frame.ContentSize;
        int width = Math.Min(contentSize.Width, (int)texDesc.Width);
        int height = Math.Min(contentSize.Height, (int)texDesc.Height);
        if (width <= 0 || height <= 0) return null;

        EnsureStagingTexture(texDesc.Width, texDesc.Height, texDesc.Format);
        if (_cachedStagingTexture == null) return null;

        _cachedContext.CopyResource(_cachedStagingTexture, frameTexture);

        var mapped = _cachedContext.Map(_cachedStagingTexture, 0, MapMode.Read);
        try
        {
            var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            var bitmapData = bitmap.LockBits(
                new Rectangle(0, 0, width, height),
                ImageLockMode.WriteOnly,
                PixelFormat.Format32bppArgb);

            try
            {
                int srcRowPitch = (int)mapped.RowPitch;
                int dstRowPitch = bitmapData.Stride;
                int bytesPerPixel = 4;

                for (int y = 0; y < height; y++)
                {
                    unsafe
                    {
                        Buffer.MemoryCopy(
                            (byte*)mapped.DataPointer + y * srcRowPitch,
                            (byte*)bitmapData.Scan0 + y * dstRowPitch,
                            dstRowPitch,
                            width * bytesPerPixel);
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

    private static void DisposeSession()
    {
        _captureSession?.Dispose();
        _captureSession = null;
        _framePool?.Dispose();
        _framePool = null;
        _captureItem = null;
        _activeHwnd = IntPtr.Zero;
        _sessionInvalidated = false;
        _frameReadyEvent.Reset();
        _framePoolSize = default;
    }

    private static void ReleaseResources()
    {
        DisposeSession();
        _cachedStagingTexture?.Dispose();
        _cachedStagingTexture = null;
        _cachedD3DDevice?.Dispose();
        _cachedD3DDevice = null;
        _cachedContext?.Dispose();
        _cachedContext = null;
        _cachedDevice?.Dispose();
        _cachedDevice = null;
        _cachedStagingWidth = 0;
        _cachedStagingHeight = 0;
    }
}

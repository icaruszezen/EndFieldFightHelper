using System;
using System.Drawing;
using System.Threading;

namespace EndFieldFightHelper.Models;

public sealed class SharedCaptureFrame : IDisposable
{
    private long _frameId;
    private Bitmap? _image;
    private readonly object _lock = new();

    public long FrameId => Volatile.Read(ref _frameId);

    public void Update(Bitmap newImage)
    {
        Bitmap? old;
        lock (_lock)
        {
            old = _image;
            _image = newImage;
            _frameId++;
        }
        old?.Dispose();
    }

    /// <summary>
    /// Returns a cloned bitmap if a newer frame is available; null otherwise.
    /// Caller owns the returned bitmap and must dispose it.
    /// </summary>
    public (long frameId, Bitmap image)? CloneLatest(long lastSeenId)
    {
        lock (_lock)
        {
            if (_image == null || _frameId == lastSeenId)
                return null;
            return (_frameId, new Bitmap(_image));
        }
    }

    public void Clear()
    {
        Bitmap? old;
        lock (_lock)
        {
            old = _image;
            _image = null;
            _frameId = 0;
        }
        old?.Dispose();
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _image?.Dispose();
            _image = null;
        }
    }
}

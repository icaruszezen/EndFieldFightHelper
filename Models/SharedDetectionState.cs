using System.Collections.Generic;
using System.Threading;

namespace EndFieldFightHelper.Models;

public sealed class SharedDetectionState
{
    private long _frameId;
    private IReadOnlyList<DetectionResult>? _results;
    private readonly object _lock = new();

    public long FrameId => Volatile.Read(ref _frameId);

    public void Update(long frameId, IReadOnlyList<DetectionResult> results)
    {
        lock (_lock)
        {
            _frameId = frameId;
            _results = results;
        }
    }

    /// <summary>
    /// Returns the latest results if the frame id differs from lastSeenId; null otherwise.
    /// The returned list is immutable and safe to use without synchronization.
    /// </summary>
    public (long frameId, IReadOnlyList<DetectionResult> results)? GetLatest(long lastSeenId)
    {
        lock (_lock)
        {
            if (_results == null || _frameId == lastSeenId)
                return null;
            return (_frameId, _results);
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _frameId = 0;
            _results = null;
        }
    }
}

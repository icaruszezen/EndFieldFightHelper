using System;
using System.Threading;

namespace EndFieldFightHelper.Models;

public sealed class SharedUltimateChargeState
{
    public const int MaxSlots = 4;

    private long _frameId;
    private readonly bool[] _isCharging = new bool[MaxSlots];
    private int _activeSlotCount;
    private readonly object _lock = new();

    public long FrameId => Volatile.Read(ref _frameId);

    public void Update(long frameId, bool[] isCharging, int activeSlotCount)
    {
        lock (_lock)
        {
            _frameId = frameId;
            _activeSlotCount = Math.Min(activeSlotCount, MaxSlots);
            for (var i = 0; i < MaxSlots; i++)
                _isCharging[i] = i < isCharging.Length && isCharging[i];
        }
    }

    /// <summary>
    /// Returns the latest result if the frame id differs from lastSeenId; null otherwise.
    /// The returned array is a copy and safe to use without synchronization.
    /// </summary>
    public (long frameId, bool[] isCharging, int activeSlotCount)? GetLatest(long lastSeenId)
    {
        lock (_lock)
        {
            if (_frameId == 0 || _frameId == lastSeenId)
                return null;
            var copy = new bool[MaxSlots];
            _isCharging.CopyTo(copy, 0);
            return (_frameId, copy, _activeSlotCount);
        }
    }

    /// <summary>
    /// Returns the current value unconditionally (no frameId check).
    /// The returned array is a copy and safe to use without synchronization.
    /// </summary>
    public (bool[] isCharging, int activeSlotCount) GetCurrent()
    {
        lock (_lock)
        {
            var copy = new bool[MaxSlots];
            _isCharging.CopyTo(copy, 0);
            return (copy, _activeSlotCount);
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _frameId = 0;
            _activeSlotCount = 0;
            for (var i = 0; i < MaxSlots; i++)
                _isCharging[i] = false;
        }
    }
}

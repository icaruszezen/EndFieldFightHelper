using System.Threading;

namespace EndFieldFightHelper.Models;

public sealed class SharedActiveCharacterState
{
    private long _frameId;
    private int _slotIndex;
    private string? _characterName;
    private readonly object _lock = new();

    public long FrameId => Volatile.Read(ref _frameId);

    public void Update(long frameId, int slotIndex, string? characterName)
    {
        lock (_lock)
        {
            _frameId = frameId;
            _slotIndex = slotIndex;
            _characterName = characterName;
        }
    }

    /// <summary>
    /// Returns the latest result if the frame id differs from lastSeenId; null otherwise.
    /// </summary>
    public (long frameId, int slotIndex, string? characterName)? GetLatest(long lastSeenId)
    {
        lock (_lock)
        {
            if (_frameId == 0 || _frameId == lastSeenId)
                return null;
            return (_frameId, _slotIndex, _characterName);
        }
    }

    /// <summary>
    /// Returns the current value unconditionally (no frameId check).
    /// </summary>
    public (int slotIndex, string? characterName) GetCurrent()
    {
        lock (_lock)
        {
            return (_slotIndex, _characterName);
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _frameId = 0;
            _slotIndex = 0;
            _characterName = null;
        }
    }
}

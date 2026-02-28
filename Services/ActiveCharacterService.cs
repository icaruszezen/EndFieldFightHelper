using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EndFieldFightHelper.Models;

namespace EndFieldFightHelper.Services;

public sealed class ActiveCharacterService : IDisposable, IPipelineStatusProvider
{
    private readonly SharedDetectionState _sharedDetection;
    private readonly SharedCaptureFrame _sharedCapture;
    private readonly Func<int, CharacterInfo?> _getSlot;
    private readonly Func<int> _getTeamCount;

    private CancellationTokenSource? _cts;
    private Task? _recognitionTask;

    private volatile string? _lastErrorMessage;
    private volatile string? _currentCharacterName;
    private volatile int _currentSlotIndex;

    private double _scaleX = 1.0;
    private double _scaleY = 1.0;
    private volatile bool _isScaleInitialized;
    private bool _hasWarnedMismatch;

    public SharedActiveCharacterState SharedActiveCharacter { get; } = new();

    public bool IsRunning { get { var cts = _cts; return cts != null && !cts.IsCancellationRequested; } }
    public double ScaleX => _scaleX;
    public double ScaleY => _scaleY;
    public bool IsScaleInitialized => _isScaleInitialized;

    public static ReadOnlySpan<(int X, int Y, int W, int H)> GetSlotRegions() => SlotRegions;
    public CharacterInfo? GetSlotCharacter(int index) => _getSlot(index);

    string IPipelineStatusProvider.PipelineName => "角色识别";
    string? IPipelineStatusProvider.LastErrorMessage => _lastErrorMessage;

    IReadOnlyList<PipelineMetric> IPipelineStatusProvider.GetMetrics()
    {
        var name = _currentCharacterName;
        var slot = _currentSlotIndex;
        return
        [
            new("当前主控", name ?? "未识别"),
            new("位置", slot == 0 ? "-" : $"{slot}号位"),
        ];
    }

    public event Action<string>? Log;

    private const string ActiveCharLabel = "当前角色";
    private const int RefWidth = 1920;
    private const int RefHeight = 1080;

    private static readonly (int X, int Y, int W, int H)[] SlotRegions =
    [
        (30, 820, 115, 150),    // 1号位（最左）
        (145, 820, 115, 150),    // 2号位
        (260, 820, 115, 150),   // 3号位
        (375, 820, 115, 150),   // 4号位（最右）
    ];

    public ActiveCharacterService(
        SharedDetectionState sharedDetection,
        SharedCaptureFrame sharedCapture,
        Func<int, CharacterInfo?> getSlot,
        Func<int> getTeamCount)
    {
        _sharedDetection = sharedDetection;
        _sharedCapture = sharedCapture;
        _getSlot = getSlot;
        _getTeamCount = getTeamCount;
    }

    public void Start()
    {
        if (IsRunning) return;

        _lastErrorMessage = null;
        _currentCharacterName = null;
        _currentSlotIndex = 0;
        _isScaleInitialized = false;
        _hasWarnedMismatch = false;
        SharedActiveCharacter.Clear();

        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        _recognitionTask = Task.Run(() => RecognitionLoop(token), token);
        Log?.Invoke("角色识别线程已启动");
    }

    public void Stop()
    {
        if (_cts == null) return;

        _cts.Cancel();
        try
        {
            _recognitionTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }

        _cts.Dispose();
        _cts = null;
        _recognitionTask = null;
        SharedActiveCharacter.Clear();

        Log?.Invoke("角色识别线程已停止");
    }

    private async Task RecognitionLoop(CancellationToken token)
    {
        long lastSeenId = 0;

        while (!token.IsCancellationRequested)
        {
            try
            {
                if (!_isScaleInitialized)
                {
                    var frame = _sharedCapture.CloneLatest(0);
                    if (frame == null)
                    {
                        await Task.Delay(10, token);
                        continue;
                    }

                    _scaleX = frame.Value.image.Width / (double)RefWidth;
                    _scaleY = frame.Value.image.Height / (double)RefHeight;
                    frame.Value.image.Dispose();
                    _isScaleInitialized = true;
                }

                var latest = _sharedDetection.GetLatest(lastSeenId);
                if (latest == null)
                {
                    await Task.Delay(5, token);
                    continue;
                }

                lastSeenId = latest.Value.frameId;

                DetectionResult? activeCharBox = null;
                foreach (var result in latest.Value.results)
                {
                    if (result.Name.Contains(ActiveCharLabel, StringComparison.OrdinalIgnoreCase))
                    {
                        activeCharBox = result;
                        break;
                    }
                }

                if (activeCharBox == null)
                    continue;

                var centerX = activeCharBox.X + activeCharBox.Width / 2;
                var centerY = activeCharBox.Y + activeCharBox.Height / 2;
                var refCenterX = (int)(centerX / _scaleX);
                var refCenterY = (int)(centerY / _scaleY);

                var matchedSlot = 0;
                for (var i = 0; i < SlotRegions.Length; i++)
                {
                    var (rx, ry, rw, rh) = SlotRegions[i];
                    if (refCenterX >= rx && refCenterX <= rx + rw &&
                        refCenterY >= ry && refCenterY <= ry + rh)
                    {
                        matchedSlot = i + 1;
                        break;
                    }
                }

                if (matchedSlot == 0)
                    continue;

                var character = _getSlot(matchedSlot);
                var charName = character?.Name;

                if (character == null && !_hasWarnedMismatch)
                {
                    var teamCount = _getTeamCount();
                    Log?.Invoke($"当前角色位于{matchedSlot}号位，但配队中该位置为空（已配置{teamCount}人），请检查配队设置");
                    _hasWarnedMismatch = true;
                }

                _currentSlotIndex = matchedSlot;
                _currentCharacterName = charName;
                SharedActiveCharacter.Update(lastSeenId, matchedSlot, charName);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _lastErrorMessage = ex.Message;
                Log?.Invoke($"角色识别线程异常: {ex.Message}");
                await Task.Delay(100, token);
            }
        }
    }

    public void Dispose()
    {
        Stop();
    }
}

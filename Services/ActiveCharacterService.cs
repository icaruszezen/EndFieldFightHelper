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
    private readonly bool[] _ultChargeBuffer = new bool[SharedUltimateChargeState.MaxSlots];

    public SharedActiveCharacterState SharedActiveCharacter { get; } = new();
    public SharedUltimateChargeState SharedUltimateCharge { get; } = new();

    public bool IsRunning { get { var cts = _cts; return cts != null && !cts.IsCancellationRequested; } }
    public double ScaleX => _scaleX;
    public double ScaleY => _scaleY;
    public bool IsScaleInitialized => _isScaleInitialized;

    public static ReadOnlySpan<(int X, int Y, int W, int H)> GetSlotRegions() => SlotRegions;
    public static ReadOnlySpan<(int X, int Y, int W, int H)> GetUltimateRegions() => UltimateRegions;
    public CharacterInfo? GetSlotCharacter(int index) => _getSlot(index);
    public int GetTeamCount() => _getTeamCount();

    string IPipelineStatusProvider.PipelineName => "角色识别";
    string? IPipelineStatusProvider.LastErrorMessage => _lastErrorMessage;

    IReadOnlyList<PipelineMetric> IPipelineStatusProvider.GetMetrics()
    {
        var name = _currentCharacterName;
        var slot = _currentSlotIndex;
        var (charging, activeCount) = SharedUltimateCharge.GetCurrent();

        var metrics = new List<PipelineMetric>
        {
            new("当前主控", name ?? "未识别"),
            new("位置", slot == 0 ? "-" : $"{slot}号位"),
        };

        for (var i = 0; i < activeCount && i < SharedUltimateChargeState.MaxSlots; i++)
            metrics.Add(new($"{i + 1}号位终结技", charging[i] ? "充能中" : "充能完成"));

        return metrics;
    }

    public event Action<string>? Log;

    private const int RefWidth = 1920;
    private const int RefHeight = 1080;
    private const double ExpectedAspectRatio = 16.0 / 9.0;
    private const double AspectRatioTolerance = 0.05;

    private static readonly (int X, int Y, int W, int H)[] SlotRegions =
    [
        (30, 800, 115, 250),    // 1号位（最左）
        (145, 800, 115, 250),    // 2号位
        (260, 800, 115, 250),   // 3号位
        (375, 800, 115, 250),   // 4号位（最右）
    ];

    // 右下角终结技圆圈区域（物理位置 0-3 从左到右，需根据实际截图校准）
    private static readonly (int X, int Y, int W, int H)[] UltimateRegions =
    [
        (1530, 750, 95, 300),    // 物理位置 0（最左）
        (1625, 750, 95, 300),    // 物理位置 1
        (1720, 750, 95, 300),    // 物理位置 2
        (1815, 750, 95, 300),    // 物理位置 3（最右）
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
        SharedUltimateCharge.Clear();

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
        SharedUltimateCharge.Clear();

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

                    var w = frame.Value.image.Width;
                    var h = frame.Value.image.Height;
                    _scaleX = w / (double)RefWidth;
                    _scaleY = h / (double)RefHeight;
                    frame.Value.image.Dispose();
                    _isScaleInitialized = true;

                    var aspect = (double)w / h;
                    if (Math.Abs(aspect - ExpectedAspectRatio) > AspectRatioTolerance)
                        Log?.Invoke($"检测到非 16:9 分辨率 ({w}x{h}, 宽高比 {aspect:F2})，角色槽位和终结技区域的坐标可能不准确");
                }

                var latest = _sharedDetection.GetLatest(lastSeenId);
                if (latest == null)
                {
                    await Task.Delay(5, token);
                    continue;
                }

                lastSeenId = latest.Value.frameId;

                ProcessActiveCharacter(latest.Value.results, lastSeenId);
                ProcessUltimateCharge(latest.Value.results, lastSeenId);
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

    private void ProcessActiveCharacter(IReadOnlyList<DetectionResult> results, long frameId)
    {
        DetectionResult? activeCharBox = null;
        foreach (var result in results)
        {
            if (result.Name.Contains(YoloLabels.ActiveCharacter, StringComparison.OrdinalIgnoreCase))
            {
                activeCharBox = result;
                break;
            }
        }

        if (activeCharBox == null)
            return;

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
            return;

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
        SharedActiveCharacter.Update(frameId, matchedSlot, charName);
    }

    private void ProcessUltimateCharge(IReadOnlyList<DetectionResult> results, long frameId)
    {
        var teamCount = Math.Min(_getTeamCount(), SharedUltimateChargeState.MaxSlots);
        if (teamCount <= 0)
        {
            SharedUltimateCharge.Update(frameId, _ultChargeBuffer, 0);
            return;
        }

        var offset = SharedUltimateChargeState.MaxSlots - teamCount;

        for (var i = 0; i < SharedUltimateChargeState.MaxSlots; i++)
            _ultChargeBuffer[i] = false;

        foreach (var result in results)
        {
            if (!result.Name.Contains(YoloLabels.UltimateCharge, StringComparison.OrdinalIgnoreCase))
                continue;

            var cx = (int)((result.X + result.Width / 2) / _scaleX);
            var cy = (int)((result.Y + result.Height / 2) / _scaleY);

            for (var i = offset; i < UltimateRegions.Length; i++)
            {
                var (rx, ry, rw, rh) = UltimateRegions[i];
                if (cx >= rx && cx <= rx + rw && cy >= ry && cy <= ry + rh)
                {
                    var slot = i - offset;
                    _ultChargeBuffer[slot] = true;
                    break;
                }
            }
        }

        SharedUltimateCharge.Update(frameId, _ultChargeBuffer, teamCount);
    }

    public void Dispose()
    {
        Stop();
    }
}

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EndFieldFightHelper.Models;

namespace EndFieldFightHelper.Services;

public sealed class BattleStateService : IDisposable, IPipelineStatusProvider
{
    private readonly SharedDetectionState _sharedDetection;
    private readonly IInputService _inputService;

    private CancellationTokenSource? _cts;
    private Task? _monitorTask;
    private IntPtr _hWnd;

    private volatile bool _isBattleActive;
    private volatile bool _areBothMarkersVisible;
    private volatile bool _isScanning;
    private long _lastBothDetectedTick;
    private long _battleEnteredTick;
    private long _enterCount;
    private long _exitCount;
    private int _battleExitGuard;
    private volatile string? _lastErrorMessage;

    private const int BattleExitDelayMs = 2_000;
    private const int MarkerLostDebounceMs = 200;
    private const int CameraScanDeltaX = 1000;
    private const int ScanWaitMs = 300;
    private const int ScanMoveDurationMs = 1000;
    private const int ScanMoveSteps = 40;

    public bool IsRunning { get { var cts = _cts; return cts != null && !cts.IsCancellationRequested; } }
    public bool IsBattleActive => _isBattleActive;
    public bool AreBothMarkersVisible => _areBothMarkersVisible;

    string IPipelineStatusProvider.PipelineName => "战斗状态";
    string? IPipelineStatusProvider.LastErrorMessage => _lastErrorMessage;

    IReadOnlyList<PipelineMetric> IPipelineStatusProvider.GetMetrics()
    {
        var active = _isBattleActive;
        var bothVisible = _areBothMarkersVisible;
        var enters = Volatile.Read(ref _enterCount);
        var exits = Volatile.Read(ref _exitCount);

        var scanning = _isScanning;

        string status;
        if (scanning)
        {
            status = "视角扫描中";
        }
        else if (!active)
        {
            status = "非战斗";
        }
        else if (bothVisible)
        {
            status = "战斗中";
        }
        else
        {
            var elapsed = Environment.TickCount64 - Volatile.Read(ref _lastBothDetectedTick);
            status = $"战斗中 (标记丢失 {elapsed / 1000}s)";
        }

        var metrics = new List<PipelineMetric>
        {
            new("当前状态", status),
            new("标记可见", bothVisible ? "角色+血条" : active ? "部分丢失" : "-"),
            new("进入次数", enters.ToString()),
            new("退出次数", exits.ToString()),
        };

        if (active)
        {
            var duration = Environment.TickCount64 - Volatile.Read(ref _battleEnteredTick);
            metrics.Add(new("持续时间", $"{duration / 1000}s"));
        }

        return metrics;
    }

    public event Action? BattleEntered;
    public event Action? BattleExited;
    public event Func<Task>? CameraScanStarting;
    public event Action<string>? Log;

    public BattleStateService(SharedDetectionState sharedDetection, IInputService inputService)
    {
        _sharedDetection = sharedDetection;
        _inputService = inputService;
    }

    public void Start(IntPtr hWnd)
    {
        if (IsRunning) return;

        _hWnd = hWnd;
        _isBattleActive = false;
        _areBothMarkersVisible = false;
        _isScanning = false;
        _lastErrorMessage = null;
        Volatile.Write(ref _lastBothDetectedTick, 0);
        Volatile.Write(ref _battleEnteredTick, 0);
        Volatile.Write(ref _enterCount, 0);
        Volatile.Write(ref _exitCount, 0);
        Volatile.Write(ref _battleExitGuard, 0);

        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        _monitorTask = Task.Run(() => MonitorLoop(token), token);
        Log?.Invoke("战斗状态监控线程已启动");
    }

    public void Stop()
    {
        if (_cts == null) return;

        _cts.Cancel();
        try
        {
            _monitorTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }

        var wasActive = _isBattleActive;
        _isBattleActive = false;
        _areBothMarkersVisible = false;

        _cts.Dispose();
        _cts = null;
        _monitorTask = null;

        if (wasActive && Interlocked.CompareExchange(ref _battleExitGuard, 1, 0) == 0)
            BattleExited?.Invoke();

        Log?.Invoke("战斗状态监控线程已停止");
    }

    private async Task MonitorLoop(CancellationToken token)
    {
        long lastSeenId = 0;
        var wasBothVisible = false;
        long markerLostTick = 0;

        while (!token.IsCancellationRequested)
        {
            try
            {
                var latest = _sharedDetection.GetLatest(lastSeenId);
                if (latest == null)
                {
                    await Task.Delay(5, token);
                    continue;
                }

                lastSeenId = latest.Value.frameId;

                var hasActiveChar = false;
                var hasHealthBar = false;
                foreach (var result in latest.Value.results)
                {
                    if (!hasActiveChar && result.Name.Contains(YoloLabels.ActiveCharacter, StringComparison.OrdinalIgnoreCase))
                        hasActiveChar = true;
                    if (!hasHealthBar && result.Name.Contains(YoloLabels.HealthBar, StringComparison.OrdinalIgnoreCase))
                        hasHealthBar = true;
                    if (hasActiveChar && hasHealthBar)
                        break;
                }

                var hasBothMarkers = hasActiveChar && hasHealthBar;
                if (hasBothMarkers)
                {
                    markerLostTick = 0;
                    _areBothMarkersVisible = true;
                }
                else
                {
                    if (markerLostTick == 0)
                        markerLostTick = Environment.TickCount64;
                    if (Environment.TickCount64 - markerLostTick >= MarkerLostDebounceMs)
                        _areBothMarkersVisible = false;
                }

                if (hasBothMarkers)
                {
                    Volatile.Write(ref _lastBothDetectedTick, Environment.TickCount64);

                    if (!_isBattleActive)
                    {
                        _isBattleActive = true;
                        Volatile.Write(ref _battleExitGuard, 0);
                        Volatile.Write(ref _battleEnteredTick, Environment.TickCount64);
                        Interlocked.Increment(ref _enterCount);
                        wasBothVisible = true;
                        Log?.Invoke("同时检测到当前角色和血条，进入战斗状态");
                        BattleEntered?.Invoke();
                    }
                    else if (!wasBothVisible)
                    {
                        wasBothVisible = true;
                        Log?.Invoke("当前角色和血条已恢复，管道恢复运行");
                    }
                }
                else if (_isBattleActive)
                {
                    if (wasBothVisible && !_areBothMarkersVisible)
                    {
                        wasBothVisible = false;
                        Log?.Invoke("当前角色或血条丢失，战技/终结技/连携技已暂停");
                    }

                    var elapsed = Environment.TickCount64 - Volatile.Read(ref _lastBothDetectedTick);
                    if (elapsed >= BattleExitDelayMs)
                    {
                        Log?.Invoke($"标记丢失超过 {BattleExitDelayMs / 1000}s，开始视角扫描...");
                        var scanHandler = CameraScanStarting;
                        if (scanHandler != null)
                        {
                            foreach (var handler in scanHandler.GetInvocationList())
                                await ((Func<Task>)handler).Invoke();
                        }

                        var foundEnemy = await PerformCameraScanAsync(token);
                        if (foundEnemy)
                        {
                            Volatile.Write(ref _lastBothDetectedTick, Environment.TickCount64);
                            Volatile.Write(ref _battleExitGuard, 0);
                            wasBothVisible = false;
                            Log?.Invoke("视角扫描发现敌人血条，继续战斗");
                            BattleEntered?.Invoke();
                        }
                        else
                        {
                            _isBattleActive = false;
                            _areBothMarkersVisible = false;
                            Interlocked.Increment(ref _exitCount);
                            Log?.Invoke("视角扫描未发现敌人，退出战斗状态");
                            if (Interlocked.CompareExchange(ref _battleExitGuard, 1, 0) == 0)
                                BattleExited?.Invoke();
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _lastErrorMessage = ex.Message;
                Log?.Invoke($"战斗状态监控线程异常: {ex.Message}");
                await Task.Delay(100, token);
            }
        }
    }

    private async Task<bool> PerformCameraScanAsync(CancellationToken token)
    {
        _isScanning = true;
        try
        {
            var preFrameId = _sharedDetection.FrameId;

            await _inputService.SimulateRelativeMouseMoveAsync(
                -CameraScanDeltaX, 0, ScanMoveDurationMs, ScanMoveSteps);
            await Task.Delay(ScanWaitMs, token);

            if (await CheckForHealthBarAsync(preFrameId, token))
                return true;

            preFrameId = _sharedDetection.FrameId;

            await _inputService.SimulateRelativeMouseMoveAsync(
                CameraScanDeltaX * 2, 0, ScanMoveDurationMs, ScanMoveSteps);
            await Task.Delay(ScanWaitMs, token);

            if (await CheckForHealthBarAsync(preFrameId, token))
                return true;

            await _inputService.SimulateRelativeMouseMoveAsync(
                -CameraScanDeltaX, 0, ScanMoveDurationMs, ScanMoveSteps);

            return false;
        }
        finally
        {
            _isScanning = false;
        }
    }

    private async Task<bool> CheckForHealthBarAsync(long afterFrameId, CancellationToken token)
    {
        const int maxWaitMs = 500;
        var deadline = Environment.TickCount64 + maxWaitMs;

        while (Environment.TickCount64 < deadline)
        {
            token.ThrowIfCancellationRequested();

            var latest = _sharedDetection.GetLatest(afterFrameId);
            if (latest != null)
            {
                foreach (var result in latest.Value.results)
                {
                    if (result.Name.Contains(YoloLabels.HealthBar, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                return false;
            }

            await Task.Delay(10, token);
        }

        return false;
    }

    public void Dispose()
    {
        Stop();
    }
}

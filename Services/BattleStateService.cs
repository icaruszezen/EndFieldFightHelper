using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EndFieldFightHelper.Models;

namespace EndFieldFightHelper.Services;

public sealed class BattleStateService : IDisposable, IPipelineStatusProvider
{
    private readonly SharedDetectionState _sharedDetection;

    private CancellationTokenSource? _cts;
    private Task? _monitorTask;

    private volatile bool _isBattleActive;
    private long _lastCharDetectedTick;
    private long _battleEnteredTick;
    private long _enterCount;
    private long _exitCount;
    private volatile string? _lastErrorMessage;

    private const string ActiveCharLabel = "当前角色";
    private const int BattleExitDelayMs = 5_000;

    public bool IsRunning { get { var cts = _cts; return cts != null && !cts.IsCancellationRequested; } }
    public bool IsBattleActive => _isBattleActive;

    string IPipelineStatusProvider.PipelineName => "战斗状态";
    string? IPipelineStatusProvider.LastErrorMessage => _lastErrorMessage;

    IReadOnlyList<PipelineMetric> IPipelineStatusProvider.GetMetrics()
    {
        var active = _isBattleActive;
        var enters = Volatile.Read(ref _enterCount);
        var exits = Volatile.Read(ref _exitCount);

        string status;
        if (!active)
        {
            status = "非战斗";
        }
        else
        {
            var elapsed = Environment.TickCount64 - Volatile.Read(ref _lastCharDetectedTick);
            status = elapsed < 1000 ? "战斗中" : $"战斗中 (丢失 {elapsed / 1000}s)";
        }

        var metrics = new List<PipelineMetric>
        {
            new("当前状态", status),
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
    public event Action<string>? Log;

    public BattleStateService(SharedDetectionState sharedDetection)
    {
        _sharedDetection = sharedDetection;
    }

    public void Start()
    {
        if (IsRunning) return;

        _isBattleActive = false;
        _lastErrorMessage = null;
        Volatile.Write(ref _lastCharDetectedTick, 0);
        Volatile.Write(ref _battleEnteredTick, 0);
        Volatile.Write(ref _enterCount, 0);
        Volatile.Write(ref _exitCount, 0);

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

        _cts.Dispose();
        _cts = null;
        _monitorTask = null;

        if (wasActive)
            BattleExited?.Invoke();

        Log?.Invoke("战斗状态监控线程已停止");
    }

    private async Task MonitorLoop(CancellationToken token)
    {
        long lastSeenId = 0;

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
                foreach (var result in latest.Value.results)
                {
                    if (result.Name.Contains(ActiveCharLabel, StringComparison.OrdinalIgnoreCase))
                    {
                        hasActiveChar = true;
                        break;
                    }
                }

                if (hasActiveChar)
                {
                    Volatile.Write(ref _lastCharDetectedTick, Environment.TickCount64);
                    if (!_isBattleActive)
                    {
                        _isBattleActive = true;
                        Volatile.Write(ref _battleEnteredTick, Environment.TickCount64);
                        Interlocked.Increment(ref _enterCount);
                        Log?.Invoke("检测到当前角色，进入战斗状态");
                        BattleEntered?.Invoke();
                    }
                }
                else if (_isBattleActive)
                {
                    var elapsed = Environment.TickCount64 - Volatile.Read(ref _lastCharDetectedTick);
                    if (elapsed >= BattleExitDelayMs)
                    {
                        _isBattleActive = false;
                        Interlocked.Increment(ref _exitCount);
                        Log?.Invoke($"丢失当前角色超过 {BattleExitDelayMs / 1000}s，退出战斗状态");
                        BattleExited?.Invoke();
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

    public void Dispose()
    {
        Stop();
    }
}

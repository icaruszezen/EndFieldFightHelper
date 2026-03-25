using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EndFieldFightHelper.Helpers;
using EndFieldFightHelper.Models;

namespace EndFieldFightHelper.Services;

public sealed class AutoUltimateService : IDisposable, IPipelineStatusProvider
{
    private readonly SharedUltimateChargeState _sharedUltCharge;
    private readonly IInputService _inputService;
    private readonly Func<bool> _isPausedProvider;

    private CancellationTokenSource? _cts;
    private Task? _ultimateTask;

    private readonly long[] _ultimateCounts = new long[SharedUltimateChargeState.MaxSlots];
    private volatile string? _lastErrorMessage;

    private const int KeyHoldMs = 1500;
    private const int CooldownMs = 5_000;

    public bool IsRunning { get { var cts = _cts; return cts != null && !cts.IsCancellationRequested; } }

    string IPipelineStatusProvider.PipelineName => "自动终结技";
    string? IPipelineStatusProvider.LastErrorMessage => _lastErrorMessage;

    IReadOnlyList<PipelineMetric> IPipelineStatusProvider.GetMetrics()
    {
        var paused = IsRunning && _isPausedProvider();
        var (_, activeCount) = _sharedUltCharge.GetCurrent();
        var metrics = new List<PipelineMetric>
        {
            new("状态", paused ? "已暂停" : IsRunning ? "运行中" : "-")
        };
        for (var i = 0; i < activeCount && i < SharedUltimateChargeState.MaxSlots; i++)
            metrics.Add(new($"{i + 1}号位释放", Volatile.Read(ref _ultimateCounts[i]).ToString()));
        return metrics;
    }

    public event Action<string>? Log;

    public AutoUltimateService(SharedUltimateChargeState sharedUltCharge, IInputService inputService,
        Func<bool> isPausedProvider)
    {
        _sharedUltCharge = sharedUltCharge;
        _inputService = inputService;
        _isPausedProvider = isPausedProvider;
    }

    public void Start(IntPtr hWnd)
    {
        if (IsRunning) return;

        for (var i = 0; i < _ultimateCounts.Length; i++)
            Volatile.Write(ref _ultimateCounts[i], 0);
        _lastErrorMessage = null;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        _ultimateTask = Task.Run(() => UltimateLoop(hWnd, token), token);
        Log?.Invoke("自动终结技线程已启动");
    }

    public void Stop()
    {
        if (_cts == null) return;

        _cts.Cancel();
        try
        {
            if (_ultimateTask?.Wait(TimeSpan.FromSeconds(2)) == false)
                Log?.Invoke("警告：自动终结技线程未能在超时内结束");
        }
        catch (AggregateException ex)
        {
            foreach (var inner in ex.Flatten().InnerExceptions)
            {
                if (inner is not OperationCanceledException)
                    Log?.Invoke($"自动终结技线程异常: {inner.Message}");
            }
        }

        _cts.Dispose();
        _cts = null;
        _ultimateTask = null;

        Log?.Invoke("自动终结技线程已停止");
    }

    private async Task UltimateLoop(IntPtr hWnd, CancellationToken token)
    {
        long lastSeenId = 0;
        var cooldownUntil = new long[SharedUltimateChargeState.MaxSlots];
        var wasCharging = new bool[SharedUltimateChargeState.MaxSlots];
        var chargeReady = new bool[SharedUltimateChargeState.MaxSlots];

        while (!token.IsCancellationRequested)
        {
            try
            {
                if (_isPausedProvider())
                {
                    await Task.Delay(10, token);
                    continue;
                }

                var latest = _sharedUltCharge.GetLatest(lastSeenId);
                if (latest == null)
                {
                    await Task.Delay(10, token);
                    continue;
                }

                lastSeenId = latest.Value.frameId;
                var isCharging = latest.Value.isCharging;
                var activeCount = latest.Value.activeSlotCount;
                var now = Environment.TickCount64;

                for (var i = 0; i < activeCount && i < SharedUltimateChargeState.MaxSlots; i++)
                {
                    if (isCharging[i])
                    {
                        wasCharging[i] = true;
                        chargeReady[i] = false;
                        continue;
                    }

                    if (wasCharging[i] && !chargeReady[i])
                        chargeReady[i] = true;

                    if (!chargeReady[i] || now < cooldownUntil[i])
                        continue;

                    var vk = Win32Helper.VK_1 + i;
                    Log?.Invoke($"{i + 1}号位终结技充能完成，发送长按数字键 {i + 1}");
                    await _inputService.SendKeyPressAsync(hWnd, vk, KeyHoldMs);
                    cooldownUntil[i] = Environment.TickCount64 + CooldownMs;
                    chargeReady[i] = false;
                    wasCharging[i] = false;
                    Interlocked.Increment(ref _ultimateCounts[i]);
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _lastErrorMessage = ex.Message;
                Log?.Invoke($"自动终结技线程异常: {ex.Message}");
                await Task.Delay(100, token);
            }
        }
    }

    public void Dispose()
    {
        Stop();
    }
}

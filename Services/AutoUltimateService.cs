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
        var (_, activeCount) = _sharedUltCharge.GetCurrent();
        var metrics = new List<PipelineMetric>();
        for (var i = 0; i < activeCount && i < SharedUltimateChargeState.MaxSlots; i++)
            metrics.Add(new($"{i + 1}号位释放", Volatile.Read(ref _ultimateCounts[i]).ToString()));
        return metrics;
    }

    public event Action<string>? Log;

    public AutoUltimateService(SharedUltimateChargeState sharedUltCharge, IInputService inputService)
    {
        _sharedUltCharge = sharedUltCharge;
        _inputService = inputService;
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
            _ultimateTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
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

        while (!token.IsCancellationRequested)
        {
            try
            {
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
                    if (isCharging[i] || now < cooldownUntil[i])
                        continue;

                    var vk = Win32Helper.VK_1 + i;
                    Log?.Invoke($"{i + 1}号位终结技充能完成，发送长按数字键 {i + 1}");
                    await _inputService.SendKeyPressAsync(hWnd, vk, KeyHoldMs);
                    cooldownUntil[i] = Environment.TickCount64 + CooldownMs;
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

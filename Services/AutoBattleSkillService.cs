using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EndFieldFightHelper.Helpers;
using EndFieldFightHelper.Models;

namespace EndFieldFightHelper.Services;

public sealed class AutoBattleSkillService : IDisposable, IPipelineStatusProvider
{
    private readonly SharedDetectionState _sharedDetection;
    private readonly IInputService _inputService;
    private readonly Func<int> _teamCountProvider;
    private readonly Func<bool> _isPausedProvider;

    private CancellationTokenSource? _cts;
    private Task? _battleSkillTask;

    private long _battleSkillCount;
    private long _lastSkillTimestamp;
    private volatile string? _lastErrorMessage;
    private int _currentSlotIndex;
    private volatile int[]? _customSkillOrder;

    private const string SkillChargeLabel = YoloLabels.SkillCharged;
    private const int SkillCooldownMs = 500;

    public bool IsRunning { get { var cts = _cts; return cts != null && !cts.IsCancellationRequested; } }
    public long BattleSkillCount => Volatile.Read(ref _battleSkillCount);
    public long LastSkillTimestamp => Volatile.Read(ref _lastSkillTimestamp);

    string IPipelineStatusProvider.PipelineName => "自动战技";
    string? IPipelineStatusProvider.LastErrorMessage => _lastErrorMessage;

    IReadOnlyList<PipelineMetric> IPipelineStatusProvider.GetMetrics()
    {
        var paused = IsRunning && _isPausedProvider();
        return [new("战技次数", BattleSkillCount.ToString()), new("状态", paused ? "已暂停" : IsRunning ? "运行中" : "-")];
    }

    public event Action<string>? Log;

    public AutoBattleSkillService(SharedDetectionState sharedDetection, IInputService inputService,
        Func<int> teamCountProvider, Func<bool> isPausedProvider)
    {
        _sharedDetection = sharedDetection;
        _inputService = inputService;
        _teamCountProvider = teamCountProvider;
        _isPausedProvider = isPausedProvider;
    }

    public void SetSkillOrder(int[]? order)
    {
        _customSkillOrder = order;
        _currentSlotIndex = 0;
    }

    public void Start(IntPtr hWnd)
    {
        if (IsRunning) return;

        Volatile.Write(ref _battleSkillCount, 0);
        Volatile.Write(ref _lastSkillTimestamp, 0);
        _lastErrorMessage = null;
        _currentSlotIndex = 0;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        _battleSkillTask = Task.Run(() => BattleSkillLoop(hWnd, token), token);
        Log?.Invoke("自动战技线程已启动");
    }

    public void Stop()
    {
        if (_cts == null) return;

        _cts.Cancel();
        bool finished;
        try
        {
            finished = _battleSkillTask?.Wait(TimeSpan.FromSeconds(2)) != false;
            if (!finished)
                Log?.Invoke("警告：自动战技线程未能在超时内结束");
        }
        catch (AggregateException ex)
        {
            finished = true;
            foreach (var inner in ex.Flatten().InnerExceptions)
            {
                if (inner is not OperationCanceledException)
                    Log?.Invoke($"自动战技线程异常: {inner.Message}");
            }
        }

        if (finished)
        {
            _cts.Dispose();
        }
        else
        {
            var leakedCts = _cts;
            var leakedTask = _battleSkillTask;
            _ = (leakedTask ?? Task.CompletedTask).ContinueWith(_ => leakedCts.Dispose(), TaskScheduler.Default);
        }
        _cts = null;
        _battleSkillTask = null;

        Log?.Invoke("自动战技线程已停止");
    }

    private const int MaxConsecutiveErrors = 20;

    private async Task BattleSkillLoop(IntPtr hWnd, CancellationToken token)
    {
        long lastSeenId = 0;
        int consecutiveErrors = 0;

        while (!token.IsCancellationRequested)
        {
            try
            {
                if (_isPausedProvider())
                {
                    await Task.Delay(10, token);
                    continue;
                }

                var latest = _sharedDetection.GetLatest(lastSeenId);
                if (latest == null)
                {
                    await Task.Delay(5, token);
                    continue;
                }

                lastSeenId = latest.Value.frameId;

                var chargeCount = 0;
                foreach (var result in latest.Value.results)
                {
                    if (result.Name.Contains(SkillChargeLabel, StringComparison.OrdinalIgnoreCase))
                        chargeCount++;
                }

                if (chargeCount >= 1)
                {
                    var now = Environment.TickCount64;
                    if (now - Volatile.Read(ref _lastSkillTimestamp) >= SkillCooldownMs)
                    {
                        var order = _customSkillOrder;
                        int slotIndex;
                        if (order is { Length: > 0 })
                        {
                            _currentSlotIndex = _currentSlotIndex % order.Length;
                            slotIndex = order[_currentSlotIndex];
                            _currentSlotIndex = (_currentSlotIndex + 1) % order.Length;
                        }
                        else
                        {
                            var teamCount = _teamCountProvider();
                            if (teamCount <= 0) continue;
                            _currentSlotIndex = _currentSlotIndex % teamCount;
                            slotIndex = _currentSlotIndex;
                            _currentSlotIndex = (_currentSlotIndex + 1) % teamCount;
                        }

                        var vk = Win32Helper.VK_1 + slotIndex;
                        await _inputService.SendKeyPressAsync(hWnd, vk);
                        Volatile.Write(ref _lastSkillTimestamp, Environment.TickCount64);
                        Interlocked.Increment(ref _battleSkillCount);
                        Log?.Invoke($"技力充能完成，发送数字键 {slotIndex + 1}");
                    }
                }

                consecutiveErrors = 0;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                consecutiveErrors++;
                _lastErrorMessage = ex.Message;
                if (consecutiveErrors >= MaxConsecutiveErrors)
                {
                    Log?.Invoke($"自动战技线程连续 {MaxConsecutiveErrors} 次失败，已停止: {ex.Message}");
                    break;
                }
                var backoffMs = Math.Min(100 * (1 << Math.Min(consecutiveErrors - 1, 5)), 5000);
                Log?.Invoke($"自动战技线程异常 ({consecutiveErrors}/{MaxConsecutiveErrors}): {ex.Message}");
                await Task.Delay(backoffMs, token);
            }
        }
    }

    public void Dispose()
    {
        Stop();
    }
}

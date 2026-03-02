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
    private volatile string? _lastErrorMessage;
    private int _currentSlotIndex;

    private const string SkillChargeLabel = "技力充能完成";
    private const int SkillCooldownMs = 500;

    public bool IsRunning { get { var cts = _cts; return cts != null && !cts.IsCancellationRequested; } }
    public long BattleSkillCount => Volatile.Read(ref _battleSkillCount);

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

    public void Start(IntPtr hWnd)
    {
        if (IsRunning) return;

        Volatile.Write(ref _battleSkillCount, 0);
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
        try
        {
            _battleSkillTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }

        _cts.Dispose();
        _cts = null;
        _battleSkillTask = null;

        Log?.Invoke("自动战技线程已停止");
    }

    private async Task BattleSkillLoop(IntPtr hWnd, CancellationToken token)
    {
        long lastSeenId = 0;
        long lastSkillTimestamp = 0;

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
                    if (now - lastSkillTimestamp >= SkillCooldownMs)
                    {
                        var teamCount = _teamCountProvider();
                        if (teamCount > 0)
                        {
                            _currentSlotIndex = _currentSlotIndex % teamCount;
                            var vk = Win32Helper.VK_1 + _currentSlotIndex;
                            await _inputService.SendKeyPressAsync(hWnd, vk);
                            lastSkillTimestamp = Environment.TickCount64;
                            Interlocked.Increment(ref _battleSkillCount);
                            Log?.Invoke($"技力充能完成，发送数字键 {_currentSlotIndex + 1}");
                            _currentSlotIndex = (_currentSlotIndex + 1) % teamCount;
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
                Log?.Invoke($"自动战技线程异常: {ex.Message}");
                await Task.Delay(100, token);
            }
        }
    }

    public void Dispose()
    {
        Stop();
    }
}

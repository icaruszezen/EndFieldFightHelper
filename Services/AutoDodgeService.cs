using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using EndFieldFightHelper.Helpers;
using EndFieldFightHelper.Models;

namespace EndFieldFightHelper.Services;

public sealed class AutoDodgeService : IDisposable, IPipelineStatusProvider
{
    private readonly SharedDetectionState _sharedDetection;
    private readonly IInputService _inputService;
    private readonly Func<int> _dodgeDelayProvider;
    private readonly Func<bool> _suppressDuringSkillProvider;
    private readonly Func<long> _lastSkillTimestampProvider;

    private CancellationTokenSource? _cts;
    private Task? _dodgeTask;

    private long _dodgeCount;
    private volatile string? _lastErrorMessage;

    private const string DodgePromptName = YoloLabels.DodgePrompt;
    private const int DodgeCooldownMs = 300;
    private const int SkillSuppressMs = 500;

    public bool IsRunning { get { var cts = _cts; return cts != null && !cts.IsCancellationRequested; } }
    public long DodgeCount => Volatile.Read(ref _dodgeCount);

    string IPipelineStatusProvider.PipelineName => "自动闪避";
    string? IPipelineStatusProvider.LastErrorMessage => _lastErrorMessage;

    IReadOnlyList<PipelineMetric> IPipelineStatusProvider.GetMetrics()
    {
        return [new("闪避次数", DodgeCount.ToString())];
    }

    public event Action<string>? Log;
    public event Action? DodgeTriggered;

    public AutoDodgeService(SharedDetectionState sharedDetection, IInputService inputService,
        Func<int> dodgeDelayProvider, Func<bool> suppressDuringSkillProvider,
        Func<long> lastSkillTimestampProvider)
    {
        _sharedDetection = sharedDetection;
        _inputService = inputService;
        _dodgeDelayProvider = dodgeDelayProvider;
        _suppressDuringSkillProvider = suppressDuringSkillProvider;
        _lastSkillTimestampProvider = lastSkillTimestampProvider;
    }

    public void Start(IntPtr hWnd)
    {
        if (IsRunning) return;

        Volatile.Write(ref _dodgeCount, 0);
        _lastErrorMessage = null;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        _dodgeTask = Task.Run(() => DodgeLoop(hWnd, token), token);
        Log?.Invoke("自动闪避线程已启动");
    }

    public void Stop()
    {
        if (_cts == null) return;

        _cts.Cancel();
        bool finished;
        try
        {
            finished = _dodgeTask?.Wait(TimeSpan.FromSeconds(2)) != false;
            if (!finished)
                Log?.Invoke("警告：自动闪避线程未能在超时内结束");
        }
        catch (AggregateException ex)
        {
            finished = true;
            foreach (var inner in ex.Flatten().InnerExceptions)
            {
                if (inner is not OperationCanceledException)
                    Log?.Invoke($"自动闪避线程异常: {inner.Message}");
            }
        }

        if (finished)
        {
            _cts.Dispose();
        }
        else
        {
            var leakedCts = _cts;
            var leakedTask = _dodgeTask;
            _ = (leakedTask ?? Task.CompletedTask).ContinueWith(_ => leakedCts.Dispose(), TaskScheduler.Default);
        }
        _cts = null;
        _dodgeTask = null;

        Log?.Invoke("自动闪避线程已停止");
    }

    private const int MaxConsecutiveErrors = 20;

    private async Task DodgeLoop(IntPtr hWnd, CancellationToken token)
    {
        long lastSeenId = 0;
        long lastDodgeTimestamp = 0;
        int consecutiveErrors = 0;

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

                var hasDodgePrompt = false;
                foreach (var result in latest.Value.results)
                {
                    if (result.Name.Contains(DodgePromptName, StringComparison.OrdinalIgnoreCase))
                    {
                        hasDodgePrompt = true;
                        break;
                    }
                }

                if (hasDodgePrompt)
                {
                    var now = Environment.TickCount64;
                    if (now - lastDodgeTimestamp < DodgeCooldownMs)
                        continue;

                    if (_suppressDuringSkillProvider())
                    {
                        var skillTs = _lastSkillTimestampProvider();
                        if (skillTs > 0 && now - skillTs < SkillSuppressMs)
                            continue;
                    }

                    var delay = _dodgeDelayProvider();
                    if (delay > 0)
                        await Task.Delay(delay, token);

                    await _inputService.SendKeyPressAsync(hWnd, Win32Helper.VK_LSHIFT);
                    lastDodgeTimestamp = Environment.TickCount64;
                    Interlocked.Increment(ref _dodgeCount);
                    DodgeTriggered?.Invoke();
                    Log?.Invoke("检测到闪避提示，已发送闪避按键");
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
                    Log?.Invoke($"自动闪避线程连续 {MaxConsecutiveErrors} 次失败，已停止: {ex.Message}");
                    break;
                }
                var backoffMs = Math.Min(100 * (1 << Math.Min(consecutiveErrors - 1, 5)), 5000);
                Log?.Invoke($"自动闪避线程异常 ({consecutiveErrors}/{MaxConsecutiveErrors}): {ex.Message}");
                await Task.Delay(backoffMs, token);
            }
        }
    }

    public void Dispose()
    {
        Stop();
    }
}

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EndFieldFightHelper.Helpers;
using EndFieldFightHelper.Models;

namespace EndFieldFightHelper.Services;

public sealed class AutoChainSkillService : IDisposable, IPipelineStatusProvider
{
    private readonly SharedDetectionState _sharedDetection;
    private readonly IInputService _inputService;
    private readonly Func<bool> _isPausedProvider;

    private CancellationTokenSource? _cts;
    private Task? _chainSkillTask;

    private long _chainSkillCount;
    private volatile string? _lastErrorMessage;

    private const string ChainSkillPromptName = YoloLabels.ChainTrigger;

    public int ChainSkillCooldownMs { get; set; } = 300;

    public bool IsRunning { get { var cts = _cts; return cts != null && !cts.IsCancellationRequested; } }
    public long ChainSkillCount => Volatile.Read(ref _chainSkillCount);

    string IPipelineStatusProvider.PipelineName => "自动连携技";
    string? IPipelineStatusProvider.LastErrorMessage => _lastErrorMessage;

    IReadOnlyList<PipelineMetric> IPipelineStatusProvider.GetMetrics()
    {
        var paused = IsRunning && _isPausedProvider();
        return [new("连携次数", ChainSkillCount.ToString()), new("状态", paused ? "已暂停" : IsRunning ? "运行中" : "-")];
    }

    public event Action<string>? Log;

    public AutoChainSkillService(SharedDetectionState sharedDetection, IInputService inputService,
        Func<bool> isPausedProvider)
    {
        _sharedDetection = sharedDetection;
        _inputService = inputService;
        _isPausedProvider = isPausedProvider;
    }

    public void Start(IntPtr hWnd)
    {
        if (IsRunning) return;

        Volatile.Write(ref _chainSkillCount, 0);
        _lastErrorMessage = null;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        _chainSkillTask = Task.Run(() => ChainSkillLoop(hWnd, token), token);
        Log?.Invoke("自动连携技线程已启动");
    }

    public void Stop()
    {
        if (_cts == null) return;

        _cts.Cancel();
        bool finished;
        try
        {
            finished = _chainSkillTask?.Wait(TimeSpan.FromSeconds(2)) != false;
            if (!finished)
                Log?.Invoke("警告：自动连携技线程未能在超时内结束");
        }
        catch (AggregateException ex)
        {
            finished = true;
            foreach (var inner in ex.Flatten().InnerExceptions)
            {
                if (inner is not OperationCanceledException)
                    Log?.Invoke($"自动连携技线程异常: {inner.Message}");
            }
        }

        if (finished)
        {
            _cts.Dispose();
        }
        else
        {
            var leakedCts = _cts;
            var leakedTask = _chainSkillTask;
            _ = (leakedTask ?? Task.CompletedTask).ContinueWith(_ => leakedCts.Dispose(), TaskScheduler.Default);
        }
        _cts = null;
        _chainSkillTask = null;

        Log?.Invoke("自动连携技线程已停止");
    }

    private const int MaxConsecutiveErrors = 20;

    private async Task ChainSkillLoop(IntPtr hWnd, CancellationToken token)
    {
        long lastSeenId = 0;
        long lastChainSkillTimestamp = 0;
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

                var hasChainSkillPrompt = false;
                foreach (var result in latest.Value.results)
                {
                    if (result.Name.Contains(ChainSkillPromptName, StringComparison.OrdinalIgnoreCase))
                    {
                        hasChainSkillPrompt = true;
                        break;
                    }
                }

                if (hasChainSkillPrompt)
                {
                    var now = Environment.TickCount64;
                    if (now - lastChainSkillTimestamp >= ChainSkillCooldownMs)
                    {
                        await _inputService.SendKeyPressAsync(hWnd, Win32Helper.VK_E);
                        lastChainSkillTimestamp = Environment.TickCount64;
                        Interlocked.Increment(ref _chainSkillCount);
                        Log?.Invoke("检测到连携触发，已发送连携按键");
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
                    Log?.Invoke($"自动连携技线程连续 {MaxConsecutiveErrors} 次失败，已停止: {ex.Message}");
                    break;
                }
                var backoffMs = Math.Min(100 * (1 << Math.Min(consecutiveErrors - 1, 5)), 5000);
                Log?.Invoke($"自动连携技线程异常 ({consecutiveErrors}/{MaxConsecutiveErrors}): {ex.Message}");
                await Task.Delay(backoffMs, token);
            }
        }
    }

    public void Dispose()
    {
        Stop();
    }
}

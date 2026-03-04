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

    private const string DodgePromptName = "闪避提示";
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
        try
        {
            _dodgeTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }

        _cts.Dispose();
        _cts = null;
        _dodgeTask = null;

        Log?.Invoke("自动闪避线程已停止");
    }

    private async Task DodgeLoop(IntPtr hWnd, CancellationToken token)
    {
        long lastSeenId = 0;
        long lastDodgeTimestamp = 0;

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
                    Log?.Invoke("检测到闪避提示，已发送闪避按键");
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _lastErrorMessage = ex.Message;
                Log?.Invoke($"自动闪避线程异常: {ex.Message}");
                await Task.Delay(100, token);
            }
        }
    }

    public void Dispose()
    {
        Stop();
    }
}

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

    private CancellationTokenSource? _cts;
    private Task? _chainSkillTask;

    private long _chainSkillCount;
    private volatile string? _lastErrorMessage;

    private const string ChainSkillPromptName = "连携触发";
    private const int ChainSkillCooldownMs = 500;

    public bool IsRunning { get { var cts = _cts; return cts != null && !cts.IsCancellationRequested; } }
    public long ChainSkillCount => Volatile.Read(ref _chainSkillCount);

    string IPipelineStatusProvider.PipelineName => "自动连携技";
    string? IPipelineStatusProvider.LastErrorMessage => _lastErrorMessage;

    IReadOnlyList<PipelineMetric> IPipelineStatusProvider.GetMetrics()
    {
        return [new("连携次数", ChainSkillCount.ToString())];
    }

    public event Action<string>? Log;

    public AutoChainSkillService(SharedDetectionState sharedDetection, IInputService inputService)
    {
        _sharedDetection = sharedDetection;
        _inputService = inputService;
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
        try
        {
            _chainSkillTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }

        _cts.Dispose();
        _cts = null;
        _chainSkillTask = null;

        Log?.Invoke("自动连携技线程已停止");
    }

    private async Task ChainSkillLoop(IntPtr hWnd, CancellationToken token)
    {
        long lastSeenId = 0;
        long lastChainSkillTimestamp = 0;

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
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _lastErrorMessage = ex.Message;
                Log?.Invoke($"自动连携技线程异常: {ex.Message}");
                await Task.Delay(100, token);
            }
        }
    }

    public void Dispose()
    {
        Stop();
    }
}

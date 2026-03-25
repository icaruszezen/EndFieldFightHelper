using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using EndFieldFightHelper.Helpers;
using EndFieldFightHelper.Models;

namespace EndFieldFightHelper.Services;

public sealed class AutoAttackService : IDisposable, IPipelineStatusProvider
{
    private readonly IInputService _inputService;

    private CancellationTokenSource? _cts;
    private Task? _attackTask;

    private long _attackCount;
    private volatile string? _lastErrorMessage;

    private const int AttackIntervalMs = 400;
    private const int MiddleClickIntervalMs = 5000;

    public bool IsRunning { get { var cts = _cts; return cts != null && !cts.IsCancellationRequested; } }
    public long AttackCount => Volatile.Read(ref _attackCount);

    string IPipelineStatusProvider.PipelineName => "自动攻击";
    string? IPipelineStatusProvider.LastErrorMessage => _lastErrorMessage;

    IReadOnlyList<PipelineMetric> IPipelineStatusProvider.GetMetrics()
    {
        return [new("攻击次数", AttackCount.ToString())];
    }

    public event Action<string>? Log;

    public AutoAttackService(IInputService inputService)
    {
        _inputService = inputService;
    }

    public void Start(IntPtr hWnd)
    {
        if (IsRunning) return;

        Volatile.Write(ref _attackCount, 0);
        _lastErrorMessage = null;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        _attackTask = Task.Run(() => AttackLoop(hWnd, token), token);
        Log?.Invoke("自动攻击线程已启动");
    }

    public void Stop()
    {
        if (_cts == null) return;

        _cts.Cancel();
        try
        {
            if (_attackTask?.Wait(TimeSpan.FromSeconds(2)) == false)
                Log?.Invoke("警告：自动攻击线程未能在超时内结束");
        }
        catch (AggregateException ex)
        {
            foreach (var inner in ex.Flatten().InnerExceptions)
            {
                if (inner is not OperationCanceledException)
                    Log?.Invoke($"自动攻击线程异常: {inner.Message}");
            }
        }

        _cts.Dispose();
        _cts = null;
        _attackTask = null;

        Log?.Invoke("自动攻击线程已停止");
    }

    private async Task AttackLoop(IntPtr hWnd, CancellationToken token)
    {
        var middleClickTimer = Stopwatch.StartNew();

        while (!token.IsCancellationRequested)
        {
            try
            {
                Win32Helper.GetClientRect(hWnd, out var rect);
                var centerX = rect.Right / 2;
                var centerY = rect.Bottom / 2;

                if (middleClickTimer.ElapsedMilliseconds >= MiddleClickIntervalMs)
                {
                    await _inputService.SendMouseClickAsync(hWnd, MouseButton.Middle, centerX, centerY);
                    middleClickTimer.Restart();
                }

                await _inputService.SendMouseClickAsync(hWnd, MouseButton.Left, centerX, centerY);
                Interlocked.Increment(ref _attackCount);

                await Task.Delay(AttackIntervalMs, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _lastErrorMessage = ex.Message;
                Log?.Invoke($"自动攻击线程异常: {ex.Message}");
                await Task.Delay(100, token);
            }
        }
    }

    public void Dispose()
    {
        Stop();
    }
}

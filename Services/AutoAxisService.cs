using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using EndFieldFightHelper.Helpers;
using EndFieldFightHelper.Models;

namespace EndFieldFightHelper.Services;

public enum AxisEventType
{
    Switch,
    Attack,
    Skill,
    Link,
    Ultimate,
}

public readonly struct AxisTimelineEvent(double time, AxisEventType type, string characterId, double duration = 0)
{
    public double Time { get; } = time;
    public AxisEventType Type { get; } = type;
    public string CharacterId { get; } = characterId;
    public double Duration { get; } = duration;
}

public sealed class AutoAxisService : IDisposable, IPipelineStatusProvider
{
    private readonly IInputService _inputService;

    private CancellationTokenSource? _cts;
    private Task? _axisTask;
    private Stopwatch? _loopStopwatch;

    private long _eventCount;
    private long _lastSkillTimestamp;
    private volatile int _pauseRequestMs;
    private volatile string? _lastErrorMessage;
    private volatile string _currentStatus = "";
    private string? _activeCharacterId;

    private const int LoopPollMs = 10;

    public int NormalAttackIntervalMs { get; set; } = 350;
    public int NormalAttackCount { get; set; } = 4;
    public int UltimateHoldMs { get; set; } = 1500;
    public int UltimatePauseMs { get; set; } = 2000;

    public bool IsRunning
    {
        get
        {
            var cts = _cts;
            return cts != null && !cts.IsCancellationRequested;
        }
    }

    public long EventCount => Volatile.Read(ref _eventCount);
    public long LastSkillTimestamp => Volatile.Read(ref _lastSkillTimestamp);
    public double ElapsedSeconds => _loopStopwatch?.Elapsed.TotalSeconds ?? 0;

    string IPipelineStatusProvider.PipelineName => "自动打轴";
    string? IPipelineStatusProvider.LastErrorMessage => _lastErrorMessage;

    IReadOnlyList<PipelineMetric> IPipelineStatusProvider.GetMetrics()
    {
        return
        [
            new("已执行事件", EventCount.ToString()),
            new("状态", IsRunning ? _currentStatus : "-"),
        ];
    }

    public event Action<string>? Log;

    public AutoAxisService(IInputService inputService)
    {
        _inputService = inputService;
    }

    public void Start(IntPtr hWnd, List<AxisTimelineEvent> events, Func<string, int?> slotMapper)
    {
        if (IsRunning) return;

        Volatile.Write(ref _eventCount, 0);
        Volatile.Write(ref _lastSkillTimestamp, 0);
        _pauseRequestMs = 0;
        _lastErrorMessage = null;
        _currentStatus = "运行中";
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        _axisTask = Task.Run(() => AxisLoop(hWnd, events, slotMapper, token), token);
        Log?.Invoke("自动打轴线程已启动");
    }

    public void Stop()
    {
        if (_cts == null) return;

        _cts.Cancel();
        try
        {
            if (_axisTask?.Wait(TimeSpan.FromSeconds(5)) == false)
                Log?.Invoke("警告：自动打轴线程未能在超时内结束");
        }
        catch (AggregateException ex)
        {
            foreach (var inner in ex.Flatten().InnerExceptions)
            {
                if (inner is not OperationCanceledException)
                    Log?.Invoke($"自动打轴线程异常: {inner.Message}");
            }
        }

        _cts.Dispose();
        _cts = null;
        _axisTask = null;
        _loopStopwatch?.Stop();
        _loopStopwatch = null;
        _currentStatus = "";

        Log?.Invoke("自动打轴线程已停止");
    }

    public void RequestPause(int ms)
    {
        Interlocked.Exchange(ref _pauseRequestMs, ms);
    }

    private async Task AxisLoop(IntPtr hWnd, List<AxisTimelineEvent> events,
        Func<string, int?> slotMapper, CancellationToken token)
    {
        _loopStopwatch = new Stopwatch();
        _loopStopwatch.Start();
        var eventIndex = 0;
        _activeCharacterId = null;

        if (events.Count > 0 && events[0].Type == AxisEventType.Switch)
            _activeCharacterId = events[0].CharacterId;

        while (!token.IsCancellationRequested && eventIndex < events.Count)
        {
            try
            {
                var pauseMs = Interlocked.Exchange(ref _pauseRequestMs, 0);
                if (pauseMs > 0)
                {
                    _currentStatus = "暂停中";
                    _loopStopwatch.Stop();
                    await Task.Delay(pauseMs, token);
                    _loopStopwatch.Start();
                    _currentStatus = "运行中";
                }

                var elapsed = _loopStopwatch.Elapsed.TotalSeconds;
                var nextEvent = events[eventIndex];

                if (elapsed < nextEvent.Time)
                {
                    await Task.Delay(LoopPollMs, token);
                    continue;
                }

                await ExecuteEventAsync(hWnd, nextEvent, slotMapper, _loopStopwatch, token);
                Interlocked.Increment(ref _eventCount);
                eventIndex++;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _lastErrorMessage = ex.Message;
                Log?.Invoke($"自动打轴线程异常: {ex.Message}");
                await Task.Delay(100, token);
            }
        }

        if (!token.IsCancellationRequested)
        {
            _currentStatus = "已完成";
            Log?.Invoke("战斗轴已执行完毕");
        }
    }

    private async Task ExecuteEventAsync(IntPtr hWnd, AxisTimelineEvent evt,
        Func<string, int?> slotMapper, Stopwatch stopwatch, CancellationToken token)
    {
        switch (evt.Type)
        {
            case AxisEventType.Switch:
                await ExecuteSwitchAsync(hWnd, evt.CharacterId, slotMapper, token);
                _activeCharacterId = evt.CharacterId;
                break;

            case AxisEventType.Attack:
                await ExecuteAttackAsync(hWnd, evt, stopwatch, token);
                break;

            case AxisEventType.Skill:
                await ExecuteSkillAsync(hWnd, evt.CharacterId, slotMapper, token);
                break;

            case AxisEventType.Link:
                await ExecuteLinkAsync(hWnd, token);
                break;

            case AxisEventType.Ultimate:
                await ExecuteUltimateAsync(hWnd, evt.CharacterId, slotMapper, token);
                break;
        }
    }

    private async Task ExecuteSwitchAsync(IntPtr hWnd, string characterId,
        Func<string, int?> slotMapper, CancellationToken token)
    {
        var slot = slotMapper(characterId);
        if (slot == null)
        {
            Log?.Invoke($"切换角色失败：未找到角色 {characterId} 的槽位");
            return;
        }

        var vk = Win32Helper.VK_F1 + (slot.Value - 1);
        await _inputService.SendKeyPressAsync(hWnd, vk);
        Log?.Invoke($"切换角色：F{slot.Value}");
    }

    private async Task ExecuteAttackAsync(IntPtr hWnd, AxisTimelineEvent evt,
        Stopwatch stopwatch, CancellationToken token)
    {
        Win32Helper.GetClientRect(hWnd, out var rect);
        var centerX = rect.Right / 2;
        var centerY = rect.Bottom / 2;

        var attackCount = Math.Max(1, NormalAttackCount);
        var intervalMs = NormalAttackIntervalMs;

        if (evt.Duration > 0)
        {
            intervalMs = Math.Max(50, (int)(evt.Duration * 1000 / attackCount));
        }

        for (var i = 0; i < attackCount; i++)
        {
            token.ThrowIfCancellationRequested();

            var pauseMs = Interlocked.Exchange(ref _pauseRequestMs, 0);
            if (pauseMs > 0)
            {
                _currentStatus = "暂停中";
                stopwatch.Stop();
                await Task.Delay(pauseMs, token);
                stopwatch.Start();
                _currentStatus = "运行中";
            }

            await _inputService.SendMouseClickAsync(hWnd, MouseButton.Left, centerX, centerY);

            if (i < attackCount - 1)
                await Task.Delay(intervalMs, token);
        }

        Log?.Invoke($"执行重击（{attackCount}次普攻，间隔{intervalMs}ms）");
    }

    private async Task ExecuteSkillAsync(IntPtr hWnd, string characterId,
        Func<string, int?> slotMapper, CancellationToken token)
    {
        var slot = slotMapper(characterId);
        if (slot == null)
        {
            Log?.Invoke($"释放战技失败：未找到角色 {characterId} 的槽位");
            return;
        }

        var vk = Win32Helper.VK_1 + (slot.Value - 1);
        await _inputService.SendKeyPressAsync(hWnd, vk);

        if (string.Equals(characterId, _activeCharacterId, StringComparison.Ordinal))
            Volatile.Write(ref _lastSkillTimestamp, Environment.TickCount64);

        Log?.Invoke($"释放战技：数字键 {slot.Value}");
    }

    private async Task ExecuteLinkAsync(IntPtr hWnd, CancellationToken token)
    {
        await _inputService.SendKeyPressAsync(hWnd, Win32Helper.VK_E);
        Log?.Invoke("释放连携技：E");
    }

    private async Task ExecuteUltimateAsync(IntPtr hWnd, string characterId,
        Func<string, int?> slotMapper, CancellationToken token)
    {
        var slot = slotMapper(characterId);
        if (slot == null)
        {
            Log?.Invoke($"释放终结技失败：未找到角色 {characterId} 的槽位");
            return;
        }

        var vk = Win32Helper.VK_1 + (slot.Value - 1);
        await _inputService.SendKeyPressAsync(hWnd, vk, UltimateHoldMs);
        Log?.Invoke($"释放终结技：长按数字键 {slot.Value}");

        RequestPause(UltimatePauseMs);
    }

    public void Dispose()
    {
        Stop();
    }
}

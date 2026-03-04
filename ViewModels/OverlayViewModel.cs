using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EndFieldFightHelper.Models;

namespace EndFieldFightHelper.ViewModels;

public partial class OverlayViewModel : ViewModelBase, IDisposable
{
    private const int MaxLogLines = 50;

    private IReadOnlyList<IPipelineStatusProvider>? _providers;
    private Timer? _pipelineRefreshTimer;

    private List<OverlayBattleAction>? _battleActions;
    private Stopwatch? _axisStopwatch;
    private Timer? _axisTimer;

    [ObservableProperty]
    private bool _isEditMode;

    [ObservableProperty]
    private double _backgroundOpacity = OverlayDefaults.Opacity;

    [ObservableProperty]
    private bool _showLog = true;

    [ObservableProperty]
    private bool _showPipelineStatus;

    [ObservableProperty]
    private bool _showBattleAxis;

    [ObservableProperty]
    private double _battleAxisFontSize = 11;

    [ObservableProperty]
    private bool _isAxisPlaying;

    [ObservableProperty]
    private double _battleAxisElapsedSeconds;

    [ObservableProperty]
    private string _currentActionText = "";

    [ObservableProperty]
    private string _nextActionText = "";

    public ObservableCollection<string> LogMessages { get; } = new();
    public ObservableCollection<OverlayPipelineItem> PipelineItems { get; } = [];

    public event Action<double, double, double, double>? PositionSizeChanged;

    public OverlayViewModel()
    {
    }

    public void SetPipelineProviders(IReadOnlyList<IPipelineStatusProvider> providers, List<string>? visibleNames = null)
    {
        _providers = providers;
        _pipelineRefreshTimer?.Dispose();
        _pipelineRefreshTimer = null;

        PipelineItems.Clear();
        foreach (var p in providers)
        {
            PipelineItems.Add(new OverlayPipelineItem
            {
                Name = p.PipelineName,
                IsVisible = visibleNames == null || visibleNames.Count == 0 || visibleNames.Contains(p.PipelineName),
            });
        }

        if (ShowPipelineStatus)
            _pipelineRefreshTimer = new Timer(RefreshPipelines, null, 0, 500);
    }

    partial void OnShowPipelineStatusChanged(bool value)
    {
        if (value && _providers != null && _pipelineRefreshTimer == null)
            _pipelineRefreshTimer = new Timer(RefreshPipelines, null, 0, 500);
        else if (!value)
        {
            _pipelineRefreshTimer?.Dispose();
            _pipelineRefreshTimer = null;
        }
    }

    public void UpdateVisiblePipelines(List<string> visibleNames)
    {
        foreach (var item in PipelineItems)
        {
            item.IsVisible = visibleNames.Count == 0 || visibleNames.Contains(item.Name);
        }
    }

    private void RefreshPipelines(object? state)
    {
        var providers = _providers;
        if (providers == null) return;

        for (var i = 0; i < providers.Count && i < PipelineItems.Count; i++)
        {
            var provider = providers[i];
            var isRunning = provider.IsRunning;
            var metrics = provider.GetMetrics();
            var summary = metrics.Count > 0 ? metrics[0].Value : "";

            var index = i;
            Dispatcher.UIThread.Post(() =>
            {
                if (index >= PipelineItems.Count) return;
                var item = PipelineItems[index];
                item.IsRunning = isRunning;
                item.MetricSummary = summary;
            });
        }
    }

    public void StartAxisPlayback(List<OverlayBattleAction> actions)
    {
        _axisTimer?.Dispose();

        _battleActions = actions.OrderBy(a => a.StartTime).ToList();
        _axisStopwatch = Stopwatch.StartNew();
        IsAxisPlaying = true;
        BattleAxisElapsedSeconds = 0;
        CurrentActionText = "";
        NextActionText = "";

        _axisTimer = new Timer(TickAxis, null, 0, 200);
    }

    public void StopAxisPlayback()
    {
        _axisTimer?.Dispose();
        _axisTimer = null;
        _axisStopwatch?.Stop();
        _axisStopwatch = null;
        IsAxisPlaying = false;
        BattleAxisElapsedSeconds = 0;
        CurrentActionText = "";
        NextActionText = "";
    }

    private void TickAxis(object? state)
    {
        var stopwatch = _axisStopwatch;
        var actions = _battleActions;
        if (stopwatch == null || actions == null) return;

        var elapsed = stopwatch.Elapsed.TotalSeconds;

        var current = actions
            .Where(a => a.StartTime <= elapsed && a.StartTime + a.Duration > elapsed)
            .Select(a => $"[{a.CharacterName}] {a.TypeLabel}")
            .FirstOrDefault();

        var next = actions
            .Where(a => a.StartTime > elapsed)
            .Take(2)
            .Select(a =>
            {
                var delta = a.StartTime - elapsed;
                return $"{a.TypeLabel}({delta:F1}s后)";
            })
            .ToList();

        var currentText = current ?? "";
        var nextText = next.Count > 0 ? string.Join(" → ", next) : "";

        Dispatcher.UIThread.Post(() =>
        {
            BattleAxisElapsedSeconds = elapsed;
            CurrentActionText = currentText;
            NextActionText = nextText;
        });
    }

    public void AddLog(string message)
    {
        if (Dispatcher.UIThread.CheckAccess())
            AddLogCore(message);
        else
            Dispatcher.UIThread.Post(() => AddLogCore(message));
    }

    private void AddLogCore(string message)
    {
        LogMessages.Add(message);
        while (LogMessages.Count > MaxLogLines)
            LogMessages.RemoveAt(0);
    }

    [RelayCommand]
    private void ToggleEditMode()
    {
        IsEditMode = !IsEditMode;
    }

    public void NotifyPositionSizeChanged(double x, double y, double width, double height)
    {
        PositionSizeChanged?.Invoke(x, y, width, height);
    }

    public void Dispose()
    {
        _pipelineRefreshTimer?.Dispose();
        _axisTimer?.Dispose();
        _axisStopwatch?.Stop();
    }
}

public partial class OverlayPipelineItem : ObservableObject
{
    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string _metricSummary = "";

    [ObservableProperty]
    private bool _isVisible = true;
}

public class OverlayBattleAction
{
    public string CharacterName { get; init; } = "";
    public string TypeLabel { get; init; } = "";
    public double StartTime { get; init; }
    public double Duration { get; init; }
}

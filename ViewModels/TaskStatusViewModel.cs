using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using EndFieldFightHelper.Models;

namespace EndFieldFightHelper.ViewModels;

public partial class PipelineStatusItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private ObservableCollection<PipelineMetric> _metrics = [];
}

public partial class TaskStatusViewModel : ViewModelBase, IDisposable
{
    private readonly IReadOnlyList<IPipelineStatusProvider> _providers;
    private Timer? _refreshTimer;

    public ObservableCollection<PipelineStatusItemViewModel> PipelineItems { get; } = [];

    public TaskStatusViewModel(IReadOnlyList<IPipelineStatusProvider> providers)
    {
        _providers = providers;

        foreach (var p in providers)
        {
            PipelineItems.Add(new PipelineStatusItemViewModel { Name = p.PipelineName });
        }

        _refreshTimer = new Timer(Refresh, null, 0, 500);
    }

    private void Refresh(object? state)
    {
        for (var i = 0; i < _providers.Count; i++)
        {
            var provider = _providers[i];
            var isRunning = provider.IsRunning;
            var error = provider.LastErrorMessage;
            var metrics = provider.GetMetrics();

            var index = i;
            Dispatcher.UIThread.Post(() =>
            {
                var item = PipelineItems[index];
                item.IsRunning = isRunning;
                item.HasError = error != null;
                item.ErrorMessage = error;

                item.Metrics.Clear();
                foreach (var m in metrics)
                    item.Metrics.Add(m);
            });
        }
    }

    public void Dispose()
    {
        _refreshTimer?.Dispose();
        _refreshTimer = null;
    }
}

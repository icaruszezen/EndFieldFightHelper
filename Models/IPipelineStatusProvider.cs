using System.Collections.Generic;

namespace EndFieldFightHelper.Models;

public interface IPipelineStatusProvider
{
    string PipelineName { get; }
    bool IsRunning { get; }
    string? LastErrorMessage { get; }
    IReadOnlyList<PipelineMetric> GetMetrics();
}

public readonly record struct PipelineMetric(string Label, string Value);

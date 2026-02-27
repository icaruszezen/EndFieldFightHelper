using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using EndFieldFightHelper.Models;

namespace EndFieldFightHelper.Services;

public sealed class RecognitionPipelineService : IDisposable, IPipelineStatusProvider
{
    private readonly IScreenshotService _screenshotService;
    private readonly YoloDetectionService _detectionService;

    private CancellationTokenSource? _cts;
    private Task? _captureTask;
    private Task? _detectionTask;

    public SharedCaptureFrame SharedCapture { get; } = new();
    public SharedDetectionState SharedDetection { get; } = new();
    public bool IsRunning { get { var cts = _cts; return cts != null && !cts.IsCancellationRequested; } }

    private long _lastCaptureDurationMs;
    private long _lastDetectionDurationMs;
    private long _captureFrameCount;
    private long _detectionFrameCount;
    private long _lastDetectionResultCount;
    private volatile bool _isDebugOutputEnabled;
    private volatile int _captureFrameIntervalMs;
    private byte[]? _lastPlottedImageBytes;
    private volatile string? _lastErrorMessage;

    public long LastCaptureDurationMs => Volatile.Read(ref _lastCaptureDurationMs);
    public long LastDetectionDurationMs => Volatile.Read(ref _lastDetectionDurationMs);
    public long CaptureFrameCount => Volatile.Read(ref _captureFrameCount);
    public long DetectionFrameCount => Volatile.Read(ref _detectionFrameCount);
    public long LastDetectionResultCount => Volatile.Read(ref _lastDetectionResultCount);

    public bool IsDebugOutputEnabled
    {
        get => _isDebugOutputEnabled;
        set => _isDebugOutputEnabled = value;
    }

    public int CaptureFrameIntervalMs
    {
        get => _captureFrameIntervalMs;
        set => _captureFrameIntervalMs = value;
    }

    public byte[]? LastPlottedImageBytes => Volatile.Read(ref _lastPlottedImageBytes);

    string IPipelineStatusProvider.PipelineName => "识别管道";
    string? IPipelineStatusProvider.LastErrorMessage => _lastErrorMessage;

    IReadOnlyList<PipelineMetric> IPipelineStatusProvider.GetMetrics()
    {
        var capMs = LastCaptureDurationMs;
        var detMs = LastDetectionDurationMs;
        var capCount = CaptureFrameCount;
        var detCount = DetectionFrameCount;
        var resultCount = LastDetectionResultCount;
        var skipped = capCount - detCount;

        return
        [
            new("截图耗时", $"{capMs} ms"),
            new("推理耗时", $"{detMs} ms"),
            new("截图帧数", capCount.ToString()),
            new("识别帧数", $"{detCount} (跳过 {skipped})"),
            new("目标数", resultCount.ToString()),
        ];
    }

    public event Action<string>? Log;

    public RecognitionPipelineService(IScreenshotService screenshotService, YoloDetectionService detectionService)
    {
        _screenshotService = screenshotService;
        _detectionService = detectionService;
    }

    public void Start(IntPtr hWnd, CaptureMethod method)
    {
        if (IsRunning) return;

        if (!_detectionService.IsModelLoaded)
        {
            Log?.Invoke("无法启动识别管道：YOLO 模型未加载");
            return;
        }

        Volatile.Write(ref _captureFrameCount, 0);
        Volatile.Write(ref _detectionFrameCount, 0);
        Volatile.Write(ref _lastCaptureDurationMs, 0);
        Volatile.Write(ref _lastDetectionDurationMs, 0);
        Volatile.Write(ref _lastDetectionResultCount, 0);
        _lastErrorMessage = null;

        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        _captureTask = Task.Run(() => CaptureLoop(hWnd, method, token), token);
        _detectionTask = Task.Run(() => DetectionLoop(token), token);

        Log?.Invoke("识别管道已启动");
    }

    public void Stop()
    {
        if (_cts == null) return;

        _cts.Cancel();

        try
        {
            Task.WhenAll(
                _captureTask ?? Task.CompletedTask,
                _detectionTask ?? Task.CompletedTask
            ).Wait(TimeSpan.FromSeconds(3));
        }
        catch (AggregateException)
        {
        }

        _cts.Dispose();
        _cts = null;
        _captureTask = null;
        _detectionTask = null;

        SharedCapture.Clear();
        SharedDetection.Clear();
        Volatile.Write(ref _lastPlottedImageBytes, null);

        Log?.Invoke("识别管道已停止");
    }

    private async Task CaptureLoop(IntPtr hWnd, CaptureMethod method, CancellationToken token)
    {
        var sw = new Stopwatch();
        while (!token.IsCancellationRequested)
        {
            try
            {
                sw.Restart();
                var bitmap = _screenshotService.CaptureWindow(hWnd, method);
                sw.Stop();

                if (bitmap != null)
                {
                    Volatile.Write(ref _lastCaptureDurationMs, sw.ElapsedMilliseconds);
                    Interlocked.Increment(ref _captureFrameCount);
                    SharedCapture.Update(bitmap);

                    var interval = _captureFrameIntervalMs;
                    if (interval > 0)
                    {
                        var sleepMs = interval - (int)sw.ElapsedMilliseconds;
                        if (sleepMs > 0)
                            await Task.Delay(sleepMs, token);
                    }
                }
                else
                {
                    await Task.Delay(10, token);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _lastErrorMessage = $"截图线程: {ex.Message}";
                Log?.Invoke($"截图线程异常: {ex.Message}");
                await Task.Delay(100, token);
            }
        }
    }

    private async Task DetectionLoop(CancellationToken token)
    {
        long lastFrameId = 0;
        var sw = new Stopwatch();

        while (!token.IsCancellationRequested)
        {
            try
            {
                var frame = SharedCapture.CloneLatest(lastFrameId);
                if (frame == null)
                {
                    await Task.Delay(5, token);
                    continue;
                }

                lastFrameId = frame.Value.frameId;
                using (frame.Value.image)
                {
                    var localCount = Volatile.Read(ref _detectionFrameCount);
                    bool skipPlot = !_isDebugOutputEnabled || (localCount % 5 != 0);

                    sw.Restart();
                    var (results, plotBytes) = await _detectionService.DetectAsync(frame.Value.image, skipPlot);
                    sw.Stop();

                    if (plotBytes != null)
                        Volatile.Write(ref _lastPlottedImageBytes, plotBytes);

                    Volatile.Write(ref _lastDetectionDurationMs, sw.ElapsedMilliseconds);
                    Volatile.Write(ref _lastDetectionResultCount, results.Count);
                    Interlocked.Increment(ref _detectionFrameCount);
                    SharedDetection.Update(lastFrameId, results);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _lastErrorMessage = $"识别线程: {ex.Message}";
                Log?.Invoke($"识别线程异常: {ex.Message}");
                await Task.Delay(100, token);
            }
        }
    }

    public void Dispose()
    {
        Stop();
        SharedCapture.Dispose();
    }
}

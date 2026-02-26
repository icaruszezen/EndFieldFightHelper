using System;
using System.Collections.ObjectModel;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using EndFieldFightHelper.Models;
using EndFieldFightHelper.Services;

namespace EndFieldFightHelper.ViewModels;

public partial class DebugViewModel : ViewModelBase, IDisposable
{
    private readonly RecognitionPipelineService _pipelineService;
    private Timer? _refreshTimer;
    private long _lastCaptureFrameId;
    private long _lastDetectionFrameId;
    private byte[]? _lastPlotBytesRef;

    [ObservableProperty]
    private bool _isDebugEnabled;

    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private string _captureStatsText = "管道未运行";

    [ObservableProperty]
    private string _detectionStatsText = "管道未运行";

    [ObservableProperty]
    private string _frameIdText = "";

    [ObservableProperty]
    private Avalonia.Media.Imaging.Bitmap? _capturePreviewImage;

    [ObservableProperty]
    private Avalonia.Media.Imaging.Bitmap? _detectionPreviewImage;

    [ObservableProperty]
    private ObservableCollection<DetectionResult> _detectionResults = new();

    public DebugViewModel(RecognitionPipelineService pipelineService)
    {
        _pipelineService = pipelineService;
    }

    partial void OnIsDebugEnabledChanged(bool value)
    {
        if (value)
        {
            _pipelineService.IsDebugOutputEnabled = true;
            _lastCaptureFrameId = 0;
            _lastDetectionFrameId = 0;
            _lastPlotBytesRef = null;
            _refreshTimer = new Timer(RefreshDebugData, null, 0, 200);
        }
        else
        {
            _pipelineService.IsDebugOutputEnabled = false;
            _refreshTimer?.Dispose();
            _refreshTimer = null;

            Dispatcher.UIThread.Post(() =>
            {
                CaptureStatsText = "管道未运行";
                DetectionStatsText = "管道未运行";
                FrameIdText = "";
                DetectionResults.Clear();

                var oldCap = CapturePreviewImage;
                CapturePreviewImage = null;
                oldCap?.Dispose();

                var oldDet = DetectionPreviewImage;
                DetectionPreviewImage = null;
                oldDet?.Dispose();
            });
        }
    }

    private void RefreshDebugData(object? state)
    {
        var ps = _pipelineService;

        if (!ps.IsRunning)
        {
            Dispatcher.UIThread.Post(() =>
            {
                CaptureStatsText = "管道未运行";
                DetectionStatsText = "管道未运行";
                FrameIdText = "";
            });
            return;
        }

        var capMs = ps.LastCaptureDurationMs;
        var capCount = ps.CaptureFrameCount;
        var detMs = ps.LastDetectionDurationMs;
        var detCount = ps.DetectionFrameCount;
        var detResults = ps.LastDetectionResultCount;
        var capFrameId = ps.SharedCapture.FrameId;
        var detFrameId = ps.SharedDetection.FrameId;
        var skipped = capCount - detCount;

        var capText = $"截图耗时: {capMs}ms | 总帧数: {capCount}";
        var detText = $"推理耗时: {detMs}ms | 已识别: {detCount} 帧 | 跳过: {skipped} 帧 | 目标数: {detResults}";
        var idText = $"截图 FrameId: {capFrameId} | 识别 FrameId: {detFrameId}";

        var tabIndex = SelectedTabIndex;

        Avalonia.Media.Imaging.Bitmap? newCapBitmap = null;
        if (tabIndex == 0)
        {
            var frame = ps.SharedCapture.CloneLatest(_lastCaptureFrameId);
            if (frame != null)
            {
                _lastCaptureFrameId = frame.Value.frameId;
                try
                {
                    using var stream = new MemoryStream();
                    frame.Value.image.Save(stream, ImageFormat.Bmp);
                    stream.Position = 0;
                    newCapBitmap = new Avalonia.Media.Imaging.Bitmap(stream);
                }
                finally
                {
                    frame.Value.image.Dispose();
                }
            }
        }

        Avalonia.Media.Imaging.Bitmap? newDetBitmap = null;
        DetectionResult[]? newResults = null;
        if (tabIndex == 1)
        {
            var plotBytes = ps.LastPlottedImageBytes;
            if (plotBytes != null && !ReferenceEquals(plotBytes, _lastPlotBytesRef))
            {
                _lastPlotBytesRef = plotBytes;
                using var ms = new MemoryStream(plotBytes);
                newDetBitmap = new Avalonia.Media.Imaging.Bitmap(ms);
            }

            var det = ps.SharedDetection.GetLatest(_lastDetectionFrameId);
            if (det != null)
            {
                _lastDetectionFrameId = det.Value.frameId;
                newResults = [.. det.Value.results];
            }
        }

        Dispatcher.UIThread.Post(() =>
        {
            CaptureStatsText = capText;
            DetectionStatsText = detText;
            FrameIdText = idText;

            if (newCapBitmap != null)
            {
                var old = CapturePreviewImage;
                CapturePreviewImage = newCapBitmap;
                old?.Dispose();
            }

            if (newDetBitmap != null)
            {
                var old = DetectionPreviewImage;
                DetectionPreviewImage = newDetBitmap;
                old?.Dispose();
            }

            if (newResults != null)
            {
                DetectionResults.Clear();
                foreach (var r in newResults)
                    DetectionResults.Add(r);
            }
        });
    }

    public void Dispose()
    {
        IsDebugEnabled = false;
        _refreshTimer?.Dispose();
        _refreshTimer = null;
        CapturePreviewImage?.Dispose();
        DetectionPreviewImage?.Dispose();
    }
}

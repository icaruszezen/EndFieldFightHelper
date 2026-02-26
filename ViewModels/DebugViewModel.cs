using System;
using System.Collections.ObjectModel;
using System.Drawing;
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
    private long _lastDetCaptureFrameId;
    private long _lastDetectionFrameId;
    private DetectionResult[]? _cachedDetectionResults;

    [ObservableProperty]
    private bool _isDebugEnabled;

    [ObservableProperty]
    private int _selectedPageIndex;

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

    public ScreenshotViewModel ScreenshotViewModel { get; }
    public YoloDetectionViewModel YoloDetectionViewModel { get; }
    public InputTestViewModel InputTestViewModel { get; }
    public SettingsViewModel OverlaySettingsViewModel { get; }

    public DebugViewModel(
        RecognitionPipelineService pipelineService,
        ScreenshotViewModel screenshotViewModel,
        YoloDetectionViewModel yoloDetectionViewModel,
        InputTestViewModel inputTestViewModel,
        SettingsViewModel overlaySettingsViewModel)
    {
        _pipelineService = pipelineService;
        ScreenshotViewModel = screenshotViewModel;
        YoloDetectionViewModel = yoloDetectionViewModel;
        InputTestViewModel = inputTestViewModel;
        OverlaySettingsViewModel = overlaySettingsViewModel;
    }

    partial void OnIsDebugEnabledChanged(bool value)
    {
        if (value)
        {
            _pipelineService.IsDebugOutputEnabled = true;
            _lastCaptureFrameId = 0;
            _lastDetCaptureFrameId = 0;
            _lastDetectionFrameId = 0;
            _cachedDetectionResults = null;
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
            var det = ps.SharedDetection.GetLatest(_lastDetectionFrameId);
            if (det != null)
            {
                _lastDetectionFrameId = det.Value.frameId;
                _cachedDetectionResults = [.. det.Value.results];
                newResults = _cachedDetectionResults;
            }

            var frame = ps.SharedCapture.CloneLatest(_lastDetCaptureFrameId);
            if (frame != null)
            {
                _lastDetCaptureFrameId = frame.Value.frameId;
                try
                {
                    var bitmap = frame.Value.image;
                    if (_cachedDetectionResults is { Length: > 0 })
                        DrawDetectionBoxes(bitmap, _cachedDetectionResults);

                    using var stream = new MemoryStream();
                    bitmap.Save(stream, ImageFormat.Bmp);
                    stream.Position = 0;
                    newDetBitmap = new Avalonia.Media.Imaging.Bitmap(stream);
                }
                finally
                {
                    frame.Value.image.Dispose();
                }
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

    private static void DrawDetectionBoxes(Bitmap bitmap, DetectionResult[] results)
    {
        using var g = Graphics.FromImage(bitmap);
        var fontSize = Math.Max(12f, bitmap.Height / 60f);
        var penWidth = Math.Max(2f, bitmap.Height / 400f);
        using var pen = new Pen(Color.FromArgb(0, 255, 128), penWidth);
        using var font = new Font("Consolas", fontSize, FontStyle.Bold);
        using var textBrush = new SolidBrush(Color.FromArgb(0, 255, 128));
        using var bgBrush = new SolidBrush(Color.FromArgb(180, 0, 0, 0));

        foreach (var r in results)
        {
            g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);

            var label = $"{r.Name} {r.Confidence:P0}";
            var size = g.MeasureString(label, font);
            var labelY = Math.Max(r.Y - size.Height - 2, 0);
            g.FillRectangle(bgBrush, r.X, labelY, size.Width + 4, size.Height + 2);
            g.DrawString(label, font, textBrush, r.X + 2, labelY);
        }
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

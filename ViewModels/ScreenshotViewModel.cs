using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EndFieldFightHelper.Helpers;
using EndFieldFightHelper.Models;
using EndFieldFightHelper.Services;

namespace EndFieldFightHelper.ViewModels;

public partial class ScreenshotViewModel : ViewModelBase, IDisposable
{
    private readonly IScreenshotService _screenshotService;
    private System.Drawing.Bitmap? _currentBitmap;

    [ObservableProperty]
    private ObservableCollection<WindowInfo> _windows = new();

    [ObservableProperty]
    private WindowInfo? _selectedWindow;

    [ObservableProperty]
    private Avalonia.Media.Imaging.Bitmap? _previewImage;

    [ObservableProperty]
    private bool _hasImage;

    [ObservableProperty]
    private string _captureDurationText = "";

    [ObservableProperty]
    private string _statusMessage = "选择一个窗口并点击截图";

    [ObservableProperty]
    private CaptureMethod _currentMethod = CaptureMethod.PrintWindow;

    [ObservableProperty]
    private ObservableCollection<long> _benchmarkTimes = new();

    [ObservableProperty]
    private string _benchmarkResultText = "";

    [ObservableProperty]
    private bool _isBenchmarking;

    [ObservableProperty]
    private double _averageCaptureTime;

    public ScreenshotViewModel(IScreenshotService screenshotService)
    {
        _screenshotService = screenshotService;
        RefreshWindows();
    }

    [RelayCommand]
    private void RefreshWindows()
    {
        Windows.Clear();
        var windows = Win32Helper.GetVisibleWindows();
        foreach (var window in windows)
        {
            Windows.Add(window);
        }
        StatusMessage = $"找到 {Windows.Count} 个窗口";
    }

    [RelayCommand]
    private void Capture()
    {
        if (SelectedWindow == null)
        {
            StatusMessage = "请先选择一个窗口";
            return;
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            _currentBitmap?.Dispose();
            _currentBitmap = _screenshotService.CaptureWindow(SelectedWindow, CurrentMethod);

            stopwatch.Stop();

            if (_currentBitmap != null)
            {
                PreviewImage = ConvertToAvaloniaBitmap(_currentBitmap);
                HasImage = true;
                StatusMessage = $"截图成功 ({_currentBitmap.Width}x{_currentBitmap.Height})";
                CaptureDurationText = $"耗时: {stopwatch.ElapsedMilliseconds}ms";
            }
            else
            {
                StatusMessage = "截图失败";
                HasImage = false;
                CaptureDurationText = "";
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            StatusMessage = $"截图错误: {ex.Message}";
            HasImage = false;
            CaptureDurationText = "";
        }
    }

    [RelayCommand]
    private async Task SaveAsync(IStorageProvider? storageProvider)
    {
        if (_currentBitmap == null || storageProvider == null)
        {
            StatusMessage = "没有可保存的截图";
            return;
        }

        try
        {
            var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "保存截图",
                SuggestedFileName = $"screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("PNG Image") { Patterns = new[] { "*.png" } },
                    new FilePickerFileType("JPEG Image") { Patterns = new[] { "*.jpg", "*.jpeg" } }
                }
            });

            if (file != null)
            {
                var filePath = file.Path.LocalPath;
                var format = filePath.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                            filePath.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
                    ? ImageFormat.Jpeg
                    : ImageFormat.Png;

                _currentBitmap.Save(filePath, format);
                StatusMessage = $"已保存: {filePath}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"保存失败: {ex.Message}";
        }
    }

    public void SetCaptureMethod(CaptureMethod method)
    {
        CurrentMethod = method;
    }

    private static Avalonia.Media.Imaging.Bitmap ConvertToAvaloniaBitmap(System.Drawing.Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        stream.Position = 0;
        return new Avalonia.Media.Imaging.Bitmap(stream);
    }

    public void TriggerCapture()
    {
        Capture();
    }

    [RelayCommand]
    private async Task BenchmarkCaptureAsync()
    {
        if (SelectedWindow == null)
        {
            StatusMessage = "请先选择一个窗口";
            return;
        }

        IsBenchmarking = true;
        BenchmarkTimes.Clear();
        StatusMessage = "正在进行5秒截图性能测试...";
        BenchmarkResultText = "";

        var testDuration = TimeSpan.FromSeconds(5);
        var window = SelectedWindow;
        var method = CurrentMethod;

        var times = await Task.Run(() =>
        {
            var results = new System.Collections.Generic.List<long>();
            var startTime = Stopwatch.StartNew();

            while (startTime.Elapsed < testDuration)
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    var bitmap = _screenshotService.CaptureWindow(window, method);
                    sw.Stop();
                    if (bitmap != null)
                    {
                        results.Add(sw.ElapsedMilliseconds);
                        bitmap.Dispose();
                    }
                }
                catch
                {
                    sw.Stop();
                }
            }

            return results;
        });

        foreach (var t in times)
        {
            BenchmarkTimes.Add(t);
        }

        if (BenchmarkTimes.Count > 0)
        {
            AverageCaptureTime = BenchmarkTimes.Average();
            var min = BenchmarkTimes.Min();
            var max = BenchmarkTimes.Max();
            BenchmarkResultText = $"测试完成: {BenchmarkTimes.Count}次截图 | 平均: {AverageCaptureTime:F1}ms | 最小: {min}ms | 最大: {max}ms";
            StatusMessage = "性能测试完成";
        }
        else
        {
            StatusMessage = "性能测试失败，未能完成任何截图";
        }

        IsBenchmarking = false;
    }

    public void Dispose()
    {
        _currentBitmap?.Dispose();
        _currentBitmap = null;
    }
}

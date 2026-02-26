using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EndFieldFightHelper.Helpers;
using EndFieldFightHelper.Models;
using EndFieldFightHelper.Services;

namespace EndFieldFightHelper.ViewModels;

public partial class YoloDetectionViewModel : ViewModelBase, IDisposable
{
    private readonly IScreenshotService _screenshotService;
    private readonly YoloDetectionService _detectionService;
    private System.Drawing.Bitmap? _currentBitmap;
    private string? _currentImagePath;
    private CancellationTokenSource? _continuousCts;

    [ObservableProperty]
    private ObservableCollection<WindowInfo> _windows = new();

    [ObservableProperty]
    private WindowInfo? _selectedWindow;

    [ObservableProperty]
    private Bitmap? _previewImage;

    [ObservableProperty]
    private bool _hasImage;

    [ObservableProperty]
    private bool _hasResults;

    [ObservableProperty]
    private string _statusMessage = "请先加载 YOLO 模型";

    [ObservableProperty]
    private string _modelStatusText = "未加载模型";

    [ObservableProperty]
    private bool _isModelLoaded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    private bool _isDetecting;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    private bool _isContinuousDetecting;

    public bool IsBusy => IsDetecting || IsContinuousDetecting;

    [ObservableProperty]
    private string _captureDurationText = "";

    [ObservableProperty]
    private string _detectionDurationText = "";

    [ObservableProperty]
    private int _continuousCount;

    [ObservableProperty]
    private ObservableCollection<DetectionResult> _detectionResults = new();

    [ObservableProperty]
    private CaptureMethod _currentMethod = CaptureMethod.PrintWindow;

    private bool _useGpu;
    private float _confidence = 0.3f;
    private float _iou = 0.45f;

    public event Action<string>? ModelPathChanged;

    public YoloDetectionViewModel(IScreenshotService screenshotService, YoloDetectionService detectionService)
    {
        _screenshotService = screenshotService;
        _detectionService = detectionService;
        RefreshWindows();
    }

    public void SetCaptureMethod(CaptureMethod method)
    {
        CurrentMethod = method;
    }

    public void SetYoloSettings(bool useGpu, float confidence, float iou)
    {
        _useGpu = useGpu;
        _confidence = confidence;
        _iou = iou;
    }

    public void TryLoadSavedModel(string? modelPath, bool useGpu = false,
        float confidence = 0.3f, float iou = 0.45f)
    {
        SetYoloSettings(useGpu, confidence, iou);
        if (!string.IsNullOrEmpty(modelPath) && File.Exists(modelPath))
        {
            try
            {
                _detectionService.LoadModel(modelPath, useGpu, confidence, iou);
                IsModelLoaded = true;
                UpdateModelStatusText();

                if (useGpu && !_detectionService.IsUsingGpu)
                    StatusMessage = $"GPU 不可用，已回退到 CPU（{_detectionService.GpuFallbackReason}）";
                else
                    StatusMessage = "模型已加载，可以开始识别";
            }
            catch
            {
                ModelStatusText = "未加载模型";
            }
        }
    }

    public void UpdateModelStatus()
    {
        IsModelLoaded = _detectionService.IsModelLoaded;
        if (IsModelLoaded)
            UpdateModelStatusText();
    }

    private void UpdateModelStatusText()
    {
        var fileName = Path.GetFileName(_detectionService.ModelPath);
        var mode = _detectionService.IsUsingGpu ? "GPU" : "CPU";
        ModelStatusText = $"已加载: {fileName} ({mode})";
    }

    [RelayCommand]
    private void RefreshWindows()
    {
        Windows.Clear();
        foreach (var window in Win32Helper.GetVisibleWindows())
            Windows.Add(window);

        var endfield = Windows.FirstOrDefault(w =>
            w.Title.Equals("Endfield", StringComparison.OrdinalIgnoreCase) ||
            w.ProcessName.Equals("Endfield", StringComparison.OrdinalIgnoreCase));
        if (endfield != null)
            SelectedWindow = endfield;

        StatusMessage = SelectedWindow != null
            ? $"找到 {Windows.Count} 个窗口，已自动选中: {SelectedWindow.Title}"
            : $"找到 {Windows.Count} 个窗口";
    }

    [RelayCommand]
    private async Task LoadModelAsync(IStorageProvider? storageProvider)
    {
        if (storageProvider == null) return;

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择 YOLO 模型文件",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("ONNX 模型") { Patterns = new[] { "*.onnx" } },
                new FilePickerFileType("所有文件") { Patterns = new[] { "*.*" } }
            }
        });

        if (files.Count == 0) return;

        var filePath = files[0].Path.LocalPath;
        try
        {
            StatusMessage = "正在加载模型...";
            await Task.Run(() => _detectionService.LoadModel(filePath, _useGpu, _confidence, _iou));
            IsModelLoaded = true;
            UpdateModelStatusText();

            if (_useGpu && !_detectionService.IsUsingGpu)
                StatusMessage = $"GPU 不可用，已回退到 CPU（{_detectionService.GpuFallbackReason}）";
            else
                StatusMessage = "模型加载成功";

            ModelPathChanged?.Invoke(filePath);
        }
        catch (Exception ex)
        {
            IsModelLoaded = false;
            ModelStatusText = "加载失败";
            StatusMessage = $"模型加载失败: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task CaptureAndDetectAsync()
    {
        if (SelectedWindow == null)
        {
            StatusMessage = "请先选择一个窗口";
            return;
        }

        if (!IsModelLoaded)
        {
            StatusMessage = "请先加载 YOLO 模型";
            return;
        }

        try
        {
            _currentImagePath = null;
            _currentBitmap?.Dispose();

            var captureSw = Stopwatch.StartNew();
            _currentBitmap = _screenshotService.CaptureWindow(SelectedWindow, CurrentMethod);
            captureSw.Stop();
            CaptureDurationText = $"截图耗时: {captureSw.ElapsedMilliseconds}ms";

            if (_currentBitmap == null)
            {
                StatusMessage = "截图失败";
                return;
            }

            await RunDetectionAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"截图识别错误: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task LoadImageAndDetectAsync(IStorageProvider? storageProvider)
    {
        if (storageProvider == null) return;

        if (!IsModelLoaded)
        {
            StatusMessage = "请先加载 YOLO 模型";
            return;
        }

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择图片文件",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("图片文件") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif" } },
                new FilePickerFileType("所有文件") { Patterns = new[] { "*.*" } }
            }
        });

        if (files.Count == 0) return;

        try
        {
            _currentBitmap?.Dispose();
            _currentBitmap = null;
            _currentImagePath = files[0].Path.LocalPath;
            await RunDetectionAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"加载图片失败: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task StartContinuousDetectionAsync()
    {
        if (SelectedWindow == null)
        {
            StatusMessage = "请先选择一个窗口";
            return;
        }

        if (!IsModelLoaded)
        {
            StatusMessage = "请先加载 YOLO 模型";
            return;
        }

        _continuousCts = new CancellationTokenSource();
        IsContinuousDetecting = true;
        ContinuousCount = 0;

        try
        {
            while (!_continuousCts.Token.IsCancellationRequested)
            {
                _currentImagePath = null;

                var captureSw = Stopwatch.StartNew();
                var newBitmap = _screenshotService.CaptureWindow(SelectedWindow, CurrentMethod);
                captureSw.Stop();

                if (newBitmap != null)
                {
                    _currentBitmap?.Dispose();
                    _currentBitmap = newBitmap;
                    CaptureDurationText = $"截图耗时: {captureSw.ElapsedMilliseconds}ms";
                }
                else if (_currentBitmap == null)
                {
                    StatusMessage = "截图失败，持续识别已停止";
                    break;
                }
                else
                {
                    await Task.Delay(1, _continuousCts.Token);
                    continue;
                }

                await RunDetectionAsync();
                ContinuousCount++;
                StatusMessage = $"持续识别中... 第 {ContinuousCount} 帧，检测到 {DetectionResults.Count} 个目标";
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            StatusMessage = $"持续识别出错: {ex.Message}";
        }
        finally
        {
            IsContinuousDetecting = false;
            StatusMessage = $"持续识别已停止，共处理 {ContinuousCount} 帧";
        }
    }

    [RelayCommand]
    private void StopContinuousDetection()
    {
        _continuousCts?.Cancel();
    }

    private const int PlotEveryNFrames = 5;

    private async Task RunDetectionAsync()
    {
        if (_currentBitmap == null && _currentImagePath == null) return;

        if (!IsContinuousDetecting)
        {
            IsDetecting = true;
            DetectionResults.Clear();
            HasResults = false;
        }

        bool skipPlot = IsContinuousDetecting && (ContinuousCount % PlotEveryNFrames != 0);

        var sw = Stopwatch.StartNew();
        try
        {
            var (results, plottedBytes) = _currentImagePath != null
                ? await Task.Run(() => _detectionService.DetectAsync(_currentImagePath, skipPlot))
                : await Task.Run(() => _detectionService.DetectAsync(_currentBitmap!, skipPlot));
            sw.Stop();

            DetectionResults.Clear();
            foreach (var r in results)
                DetectionResults.Add(r);
            HasResults = DetectionResults.Count > 0;

            if (plottedBytes != null)
            {
                using var ms = new MemoryStream(plottedBytes);
                PreviewImage = new Bitmap(ms);
                HasImage = true;
            }

            DetectionDurationText = $"推理耗时: {sw.ElapsedMilliseconds}ms";
            StatusMessage = $"识别完成，检测到 {results.Count} 个目标";
        }
        catch (Exception ex)
        {
            sw.Stop();
            StatusMessage = $"识别失败: {ex.Message}";
            HasImage = false;
        }
        finally
        {
            if (!IsContinuousDetecting)
                IsDetecting = false;
        }
    }

    public void Dispose()
    {
        _continuousCts?.Cancel();
        _continuousCts?.Dispose();
        _currentBitmap?.Dispose();
        _currentBitmap = null;
    }
}

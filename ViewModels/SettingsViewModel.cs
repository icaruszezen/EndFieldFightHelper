using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EndFieldFightHelper.Models;
using EndFieldFightHelper.Services;
using SukiUI;
using SukiUI.Enums;
using SukiUI.Models;
using SukiUI.Toasts;

namespace EndFieldFightHelper.ViewModels;

public partial class SettingsViewModel : ViewModelBase, IDisposable
{
    private readonly string _settingsPath;
    private bool _isLoading;
    private readonly OverlayService _overlayService;
    private readonly ISukiToastManager _toastManager;
    private OverlayViewModel? _overlayViewModel;
    private CancellationTokenSource? _saveCts;
    
    [ObservableProperty]
    private CaptureMethod _selectedMethod = CaptureMethod.PrintWindow;

    [ObservableProperty]
    private bool _isDarkTheme = false;

    [ObservableProperty]
    private SukiColorTheme? _selectedColorTheme;

    [ObservableProperty]
    private bool _overlayEnabled;

    [ObservableProperty]
    private bool _overlayClickThrough = true;

    [ObservableProperty]
    private double _overlayX = 50;

    [ObservableProperty]
    private double _overlayY = 50;

    [ObservableProperty]
    private double _overlayWidth = 300;

    [ObservableProperty]
    private double _overlayHeight = 120;

    [ObservableProperty]
    private double _overlayOpacity = 0.85;

    [ObservableProperty]
    private string _overlayText = "自定义内容示例";

    [ObservableProperty]
    private CloseAction _closeAction = CloseAction.Ask;

    [ObservableProperty]
    private string _yoloModelPath = "";

    [ObservableProperty]
    private bool _useGpu;

    [ObservableProperty]
    private double _yoloConfidence = 0.3;

    [ObservableProperty]
    private double _yoloIoU = 0.45;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CudaRuntimeSummary))]
    [NotifyPropertyChangedFor(nameof(GpuToggleHint))]
    private bool _isGpuAvailable = YoloDetectionService.IsGpuAvailable();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CudaRuntimeSummary))]
    [NotifyPropertyChangedFor(nameof(GpuToggleHint))]
    private bool _isCudaInstalled = CudaDependencyService.CheckInstalled();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowIdleCudaStatus))]
    private bool _isDownloadingCuda;

    [ObservableProperty]
    private double _cudaDownloadProgress;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCudaDownloadStatus))]
    [NotifyPropertyChangedFor(nameof(ShowIdleCudaStatus))]
    private string _cudaDownloadStatus = "";

    private CancellationTokenSource? _cudaDownloadCts;
    private readonly CudaDependencyService _cudaDependencyService = new();

    public ObservableCollection<SukiColorTheme> AvailableColorThemes { get; } = new();

    public event Action<CaptureMethod>? CaptureMethodChanged;
    public event Action? YoloSettingsChanged;

    public bool HasCudaDownloadStatus => !string.IsNullOrWhiteSpace(CudaDownloadStatus);
    public bool ShowIdleCudaStatus => !IsDownloadingCuda && HasCudaDownloadStatus;
    public string CudaRuntimeSummary => !IsCudaInstalled
        ? "CUDA 运行时未安装"
        : IsGpuAvailable
            ? "CUDA 运行时已安装，可直接启用 GPU"
            : "CUDA 运行时已安装，重启应用后可启用 GPU";
    public string GpuToggleHint => !IsCudaInstalled
        ? "需要先安装 CUDA 运行时（见下方）"
        : IsGpuAvailable
            ? "使用 CUDA 进行 GPU 加速推理"
            : "CUDA 已安装，如无法启用 GPU 请先重启应用";

    public string PrintWindowDescription => 
        "PrintWindow 是 Windows API，可以截取被其他窗口遮挡的窗口内容。" +
        "支持 DWM 合成窗口，适合大多数现代应用程序。某些使用硬件加速渲染的应用可能返回黑屏。";

    public string GdiPlusDescription => 
        "GDI+ CopyFromScreen 使用 .NET 内置的 Graphics 类进行截图。" +
        "简单易用，但只能截取屏幕上当前可见的内容，被遮挡的部分无法截取。";

    public string BitBltDescription => 
        "BitBlt 是经典的 GDI 位块传输方法。" +
        "但无法截取使用 DirectX/硬件加速的窗口内容，这些窗口可能显示为黑色。";

    public string DxgiDesktopDuplicationDescription => 
        "DXGI Desktop Duplication 使用 Windows 桌面复制 API，" +
        "可以高效捕获包括 DirectX/硬件加速在内的所有窗口内容。" +
        "性能优异且支持 HDR，但仅支持 Windows 8 及以上系统，且只能截取屏幕上可见的内容。";

    public string WindowsGraphicsCaptureDescription => 
        "Windows Graphics Capture 使用现代 WinRT 捕获 API，" +
        "可以截取被遮挡的窗口内容，同时支持 DirectX/硬件加速渲染。" +
        "通过禁用黄色边框实现无感截图（需要 Windows 11），最低支持 Windows 10 1903。";

    public SettingsViewModel(ISukiToastManager toastManager)
    {
        _toastManager = toastManager;
        _settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EndFieldFightHelper",
            "settings.json");

        _overlayService = new OverlayService();
        
        InitializeColorThemes();
        LoadSettings();
    }

    private void InitializeColorThemes()
    {
        var sukiTheme = SukiTheme.GetInstance();
        foreach (var theme in sukiTheme.ColorThemes)
        {
            AvailableColorThemes.Add(theme);
        }
    }

    partial void OnSelectedMethodChanged(CaptureMethod value)
    {
        CaptureMethodChanged?.Invoke(value);
        if (!_isLoading) ScheduleSave();
    }

    partial void OnIsDarkThemeChanged(bool value)
    {
        var sukiTheme = SukiTheme.GetInstance();
        sukiTheme.ChangeBaseTheme(value ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light);
        if (!_isLoading) ScheduleSave();
    }

    partial void OnSelectedColorThemeChanged(SukiColorTheme? value)
    {
        if (value != null)
        {
            var sukiTheme = SukiTheme.GetInstance();
            sukiTheme.ChangeColorTheme(value);
            if (!_isLoading) ScheduleSave();
        }
    }

    partial void OnOverlayEnabledChanged(bool value)
    {
        ApplyOverlaySettingsIfReady();
        if (!_isLoading) ScheduleSave();
    }

    partial void OnOverlayClickThroughChanged(bool value)
    {
        ApplyOverlaySettingsIfReady();
        if (!_isLoading) ScheduleSave();
    }

    partial void OnOverlayXChanged(double value)
    {
        ApplyOverlaySettingsIfReady();
        if (!_isLoading) ScheduleSave();
    }

    partial void OnOverlayYChanged(double value)
    {
        ApplyOverlaySettingsIfReady();
        if (!_isLoading) ScheduleSave();
    }

    partial void OnOverlayWidthChanged(double value)
    {
        ApplyOverlaySettingsIfReady();
        if (!_isLoading) ScheduleSave();
    }

    partial void OnOverlayHeightChanged(double value)
    {
        ApplyOverlaySettingsIfReady();
        if (!_isLoading) ScheduleSave();
    }

    partial void OnOverlayOpacityChanged(double value)
    {
        ApplyOverlaySettingsIfReady();
        if (!_isLoading) ScheduleSave();
    }

    partial void OnOverlayTextChanged(string value)
    {
        _overlayViewModel?.UpdateText(value);
        if (!_isLoading) ScheduleSave();
    }

    partial void OnCloseActionChanged(CloseAction value)
    {
        if (!_isLoading) ScheduleSave();
    }

    partial void OnYoloModelPathChanged(string value)
    {
        if (!_isLoading) ScheduleSave();
    }

    partial void OnUseGpuChanged(bool value)
    {
        if (!_isLoading)
        {
            ScheduleSave();
            YoloSettingsChanged?.Invoke();
        }
    }

    partial void OnYoloConfidenceChanged(double value)
    {
        if (!_isLoading)
        {
            ScheduleSave();
            YoloSettingsChanged?.Invoke();
        }
    }

    partial void OnYoloIoUChanged(double value)
    {
        if (!_isLoading)
        {
            ScheduleSave();
            YoloSettingsChanged?.Invoke();
        }
    }

    public void UpdateYoloModelPath(string path)
    {
        YoloModelPath = path;
    }

    [RelayCommand]
    private void SetCloseAction(string action)
    {
        CloseAction = action switch
        {
            "Ask" => CloseAction.Ask,
            "MinimizeToTray" => CloseAction.MinimizeToTray,
            "Exit" => CloseAction.Exit,
            _ => CloseAction
        };
    }

    [RelayCommand]
    private void SetMethod(string method)
    {
        SelectedMethod = method switch
        {
            "PrintWindow" => CaptureMethod.PrintWindow,
            "GdiPlus" => CaptureMethod.GdiPlusCopyFromScreen,
            "BitBlt" => CaptureMethod.BitBlt,
            "DxgiDesktopDuplication" => CaptureMethod.DxgiDesktopDuplication,
            "WindowsGraphicsCapture" => CaptureMethod.WindowsGraphicsCapture,
            _ => SelectedMethod
        };
    }

    [RelayCommand]
    private void SetColorTheme(SukiColorTheme theme)
    {
        SelectedColorTheme = theme;
    }

    [RelayCommand]
    private async Task DownloadCudaAsync()
    {
        if (IsDownloadingCuda) return;
        IsDownloadingCuda = true;
        CudaDownloadProgress = 0;
        CudaDownloadStatus = "准备下载 CUDA 运行时...";
        _cudaDownloadCts = new CancellationTokenSource();

        try
        {
            var progress = new Progress<CudaDownloadProgress>(p =>
            {
                var extractingSuffix = " (解压中)";
                var isExtracting = p.FileName.EndsWith(extractingSuffix, StringComparison.Ordinal);
                var packageName = isExtracting
                    ? p.FileName[..^extractingSuffix.Length]
                    : p.FileName;

                CudaDownloadProgress = ((p.FileIndex - 1) * 100.0 + p.FileProgress) / p.TotalFiles;
                CudaDownloadStatus = isExtracting
                    ? $"正在安装 {packageName} ({p.FileIndex}/{p.TotalFiles})..."
                    : $"正在下载 {packageName} ({p.FileIndex}/{p.TotalFiles})... {p.FileProgress:F0}%";
            });

            await _cudaDependencyService.DownloadAndInstallAsync(progress, _cudaDownloadCts.Token);

            RefreshCudaState();
            CudaDownloadStatus = IsCudaInstalled
                ? CudaRuntimeSummary
                : "CUDA 安装未完成，请重试";
        }
        catch (OperationCanceledException)
        {
            CudaDownloadStatus = "下载已取消";
        }
        catch (Exception ex)
        {
            var message = ex.Message.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault() ?? ex.Message;
            CudaDownloadStatus = $"下载失败：{message}";
        }
        finally
        {
            IsDownloadingCuda = false;
            _cudaDownloadCts?.Dispose();
            _cudaDownloadCts = null;
        }
    }

    [RelayCommand]
    private void CancelCudaDownload()
    {
        _cudaDownloadCts?.Cancel();
    }

    [RelayCommand]
    private void UninstallCuda()
    {
        CudaDependencyService.DeleteCudaDlls();
        YoloDetectionService.ResetGpuCache();
        IsCudaInstalled = false;
        IsGpuAvailable = false;
        UseGpu = false;
        CudaDownloadStatus = "CUDA 运行时已卸载";
    }

    [RelayCommand]
    private void OpenCudaGuide()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://developer.nvidia.com/cuda-downloads",
                UseShellExecute = true
            });
        }
        catch { }
    }

    private void LoadSettings()
    {
        _isLoading = true;
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings != null)
                {
                    SelectedMethod = settings.DefaultCaptureMethod;
                    IsDarkTheme = settings.IsDarkTheme;
                    OverlayEnabled = settings.OverlayEnabled;
                    OverlayClickThrough = settings.OverlayClickThrough;
                    OverlayX = settings.OverlayX;
                    OverlayY = settings.OverlayY;
                    OverlayWidth = settings.OverlayWidth;
                    OverlayHeight = settings.OverlayHeight;
                    OverlayOpacity = settings.OverlayOpacity;
                    OverlayText = settings.OverlayText;
                    CloseAction = settings.CloseAction;
                    YoloModelPath = settings.YoloModelPath;
                    UseGpu = settings.UseGpu;
                    YoloConfidence = settings.YoloConfidence;
                    YoloIoU = settings.YoloIoU;
                    
                    var savedTheme = AvailableColorThemes
                        .FirstOrDefault(t => t.DisplayName == settings.ThemeColorName);
                    SelectedColorTheme = savedTheme ?? (AvailableColorThemes.Count > 0 ? AvailableColorThemes[0] : null);
                }
            }
            else if (AvailableColorThemes.Count > 0)
            {
                SelectedColorTheme = AvailableColorThemes.FirstOrDefault(t => t.DisplayName == "Orange") 
                    ?? AvailableColorThemes[0];
            }
        }
        catch
        {
            if (AvailableColorThemes.Count > 0)
            {
                SelectedColorTheme = AvailableColorThemes[0];
            }
        }
        finally
        {
            _isLoading = false;
            var sukiTheme = SukiTheme.GetInstance();
            sukiTheme.ChangeBaseTheme(IsDarkTheme ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light);
            ApplyOverlaySettings();
        }
    }

    private void ScheduleSave()
    {
        _saveCts?.Cancel();
        _saveCts = new CancellationTokenSource();
        var token = _saveCts.Token;
        _ = SaveSettingsDebouncedAsync(token);
    }

    private async Task SaveSettingsDebouncedAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(300, token);
            if (token.IsCancellationRequested) return;

            var dir = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var settings = new AppSettings
            {
                DefaultCaptureMethod = SelectedMethod,
                IsDarkTheme = IsDarkTheme,
                ThemeColorName = SelectedColorTheme?.DisplayName ?? "Orange",
                OverlayEnabled = OverlayEnabled,
                OverlayClickThrough = OverlayClickThrough,
                OverlayX = OverlayX,
                OverlayY = OverlayY,
                OverlayWidth = OverlayWidth,
                OverlayHeight = OverlayHeight,
                OverlayOpacity = OverlayOpacity,
                OverlayText = OverlayText,
                CloseAction = CloseAction,
                YoloModelPath = YoloModelPath,
                UseGpu = UseGpu,
                YoloConfidence = (float)YoloConfidence,
                YoloIoU = (float)YoloIoU
            };

            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_settingsPath, json, token);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }
    }

    private void ApplyOverlaySettingsIfReady()
    {
        if (_isLoading)
        {
            return;
        }

        ApplyOverlaySettings();
    }

    private void ApplyOverlaySettings()
    {
        _overlayService.ApplySettings(
            OverlayEnabled,
            OverlayClickThrough,
            OverlayX,
            OverlayY,
            OverlayWidth,
            OverlayHeight,
            OverlayOpacity);
    }

    public void AttachOverlay(OverlayViewModel overlayViewModel)
    {
        _overlayViewModel = overlayViewModel;
        _overlayViewModel.UpdateText(OverlayText);
        _overlayService.SetOverlayDataContext(overlayViewModel);
    }

    public void Dispose()
    {
        _saveCts?.Cancel();
        _saveCts?.Dispose();
        _overlayService.Dispose();
    }

    private void RefreshCudaState()
    {
        IsCudaInstalled = CudaDependencyService.CheckInstalled();
        YoloDetectionService.ResetGpuCache();
        IsGpuAvailable = YoloDetectionService.IsGpuAvailable();
    }
}

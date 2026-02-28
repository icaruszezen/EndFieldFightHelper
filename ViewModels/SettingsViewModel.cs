using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EndFieldFightHelper.Models;
using EndFieldFightHelper.Services;
using SukiUI;
using SukiUI.Enums;
using SukiUI.Models;
using SukiUI.Toasts;

namespace EndFieldFightHelper.ViewModels;

public record FrameRateOption(int Value, string Label)
{
    public override string ToString() => Label;
}

public partial class SettingsViewModel : ViewModelBase, IDisposable
{
    private readonly string _settingsPath;
    private bool _isLoading;
    private bool _isUpdatingFromDrag;
    private readonly OverlayService _overlayService;
    private readonly ISukiToastManager _toastManager;
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
    private double _overlayX = OverlayDefaults.X;

    [ObservableProperty]
    private double _overlayY = OverlayDefaults.Y;

    [ObservableProperty]
    private double _overlayWidth = OverlayDefaults.Width;

    [ObservableProperty]
    private double _overlayHeight = OverlayDefaults.Height;

    [ObservableProperty]
    private double _overlayOpacity = OverlayDefaults.Opacity;

    [ObservableProperty]
    private CloseAction _closeAction = CloseAction.Ask;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(YoloModelDisplayText))]
    private string _yoloModelPath = "";

    public string YoloModelDisplayText => string.IsNullOrEmpty(YoloModelPath)
        ? "未选择模型"
        : Path.GetFileName(YoloModelPath);

    [ObservableProperty]
    private GpuDeviceInfo _selectedInferenceDevice = GpuDeviceInfo.CpuDevice;

    [ObservableProperty]
    private double _yoloConfidence = 0.5;

    [ObservableProperty]
    private double _yoloIoU = 0.45;

    [ObservableProperty]
    private FrameRateOption _selectedFrameRateLimit;

    public static IReadOnlyList<FrameRateOption> FrameRateLimitOptions { get; } =
    [
        new(30, "30 FPS"),
        new(60, "60 FPS"),
        new(0, "无限制"),
    ];

    public int CaptureFrameRateLimit => SelectedFrameRateLimit.Value;

    public ObservableCollection<SukiColorTheme> AvailableColorThemes { get; } = new();
    public ObservableCollection<GpuDeviceInfo> InferenceDevices { get; } = new();

    public event Action<CaptureMethod>? CaptureMethodChanged;
    public event Action? YoloSettingsChanged;
    public event Action<int>? CaptureFrameRateLimitChanged;

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

    public SettingsViewModel(ISukiToastManager toastManager, OverlayService overlayService)
    {
        _toastManager = toastManager;
        _overlayService = overlayService;
        _selectedFrameRateLimit = FrameRateLimitOptions[1]; // 60 FPS
        _settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EndFieldFightHelper",
            "settings.json");

        _overlayService.OverlayPositionSizeChanged += OnOverlayPositionSizeFromWindow;

        RefreshInferenceDevices();
        InitializeColorThemes();
        LoadSettings();
    }

    private void OnOverlayPositionSizeFromWindow(double x, double y, double width, double height)
    {
        _isUpdatingFromDrag = true;
        try
        {
            OverlayX = x;
            OverlayY = y;
            OverlayWidth = width;
            OverlayHeight = height;
        }
        finally
        {
            _isUpdatingFromDrag = false;
        }

        ScheduleSave();
    }

    private void RefreshInferenceDevices()
    {
        InferenceDevices.Clear();
        foreach (var dev in GpuDeviceService.EnumerateAllDevices())
            InferenceDevices.Add(dev);
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

    partial void OnOverlayXChanged(double value)
    {
        UpdateOverlayPropertiesIfReady();
        if (!_isLoading && !_isUpdatingFromDrag) ScheduleSave();
    }

    partial void OnOverlayYChanged(double value)
    {
        UpdateOverlayPropertiesIfReady();
        if (!_isLoading && !_isUpdatingFromDrag) ScheduleSave();
    }

    partial void OnOverlayWidthChanged(double value)
    {
        UpdateOverlayPropertiesIfReady();
        if (!_isLoading && !_isUpdatingFromDrag) ScheduleSave();
    }

    partial void OnOverlayHeightChanged(double value)
    {
        UpdateOverlayPropertiesIfReady();
        if (!_isLoading && !_isUpdatingFromDrag) ScheduleSave();
    }

    partial void OnOverlayOpacityChanged(double value)
    {
        UpdateOverlayPropertiesIfReady();
        if (!_isLoading && !_isUpdatingFromDrag) ScheduleSave();
    }

    partial void OnCloseActionChanged(CloseAction value)
    {
        if (!_isLoading) ScheduleSave();
    }

    partial void OnYoloModelPathChanged(string value)
    {
        if (!_isLoading)
        {
            ScheduleSave();
            YoloSettingsChanged?.Invoke();
        }
    }

    partial void OnSelectedInferenceDeviceChanged(GpuDeviceInfo value)
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

    partial void OnSelectedFrameRateLimitChanged(FrameRateOption value)
    {
        CaptureFrameRateLimitChanged?.Invoke(value.Value);
        if (!_isLoading) ScheduleSave();
    }

    public void UpdateYoloModelPath(string path)
    {
        YoloModelPath = path;
    }

    [RelayCommand]
    private async Task SelectYoloModelAsync(IStorageProvider? storageProvider)
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
        YoloModelPath = files[0].Path.LocalPath;
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
    private void ResetOverlayPosition()
    {
        OverlayX = OverlayDefaults.X;
        OverlayY = OverlayDefaults.Y;
        OverlayWidth = OverlayDefaults.Width;
        OverlayHeight = OverlayDefaults.Height;
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
                    OverlayX = settings.OverlayX;
                    OverlayY = settings.OverlayY;
                    OverlayWidth = settings.OverlayWidth;
                    OverlayHeight = settings.OverlayHeight;
                    OverlayOpacity = settings.OverlayOpacity;
                    CloseAction = settings.CloseAction;
                    YoloModelPath = settings.YoloModelPath;
                    YoloConfidence = settings.YoloConfidence;
                    YoloIoU = settings.YoloIoU;
                    SelectedFrameRateLimit = FrameRateLimitOptions.FirstOrDefault(o => o.Value == settings.CaptureFrameRateLimit)
                                             ?? FrameRateLimitOptions[1];

                    var savedDeviceId = string.Equals(settings.GpuMode, "directml", StringComparison.OrdinalIgnoreCase)
                        ? settings.GpuDeviceId
                        : -1;
                    SelectedInferenceDevice = InferenceDevices.FirstOrDefault(d => d.DeviceId == savedDeviceId)
                                              ?? GpuDeviceInfo.CpuDevice;

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

            var device = SelectedInferenceDevice;
            var settings = new AppSettings
            {
                DefaultCaptureMethod = SelectedMethod,
                IsDarkTheme = IsDarkTheme,
                ThemeColorName = SelectedColorTheme?.DisplayName ?? "Orange",
                OverlayEnabled = OverlayEnabled,
                OverlayX = OverlayX,
                OverlayY = OverlayY,
                OverlayWidth = OverlayWidth,
                OverlayHeight = OverlayHeight,
                OverlayOpacity = OverlayOpacity,
                CloseAction = CloseAction,
                YoloModelPath = YoloModelPath,
                GpuMode = device.IsCpu ? "cpu" : "directml",
                GpuDeviceId = device.IsCpu ? 0 : device.DeviceId,
                YoloConfidence = (float)YoloConfidence,
                YoloIoU = (float)YoloIoU,
                CaptureFrameRateLimit = SelectedFrameRateLimit.Value,
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
        if (_isLoading || _isUpdatingFromDrag)
        {
            return;
        }

        ApplyOverlaySettings();
    }

    private void UpdateOverlayPropertiesIfReady()
    {
        if (_isLoading || _isUpdatingFromDrag)
        {
            return;
        }

        _overlayService.UpdateProperties(
            OverlayX, OverlayY,
            OverlayWidth, OverlayHeight,
            OverlayOpacity);
    }

    private void ApplyOverlaySettings()
    {
        _overlayService.ApplySettings(
            OverlayEnabled,
            OverlayX,
            OverlayY,
            OverlayWidth,
            OverlayHeight,
            OverlayOpacity);
    }

    public void AttachOverlay(OverlayViewModel overlayViewModel)
    {
        _overlayService.SetOverlayDataContext(overlayViewModel);
    }

    public void Dispose()
    {
        _overlayService.OverlayPositionSizeChanged -= OnOverlayPositionSizeFromWindow;
        _saveCts?.Cancel();
        _saveCts?.Dispose();
    }
}

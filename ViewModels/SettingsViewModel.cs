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
    private readonly ResourceService _resourceService;
    private readonly AppUpdateService _appUpdateService;
    private CancellationTokenSource? _saveCts;
    private CancellationTokenSource? _downloadCts;
    private CancellationTokenSource? _appUpdateCts;
    private AppUpdateInfo? _latestUpdateInfo;

    private volatile int _dodgeDelayMs = 150;
    private volatile bool _dodgeSuppressDuringSkill;

    private bool _homeAutoDodge;
    private bool _homeAutoSkill;
    private bool _homeAutoAttack;
    private bool _homeAutoUltimate;
    private bool _homeAutoChainSkill;
    private bool _homeBattleOverlay;
    private string _homeAutoSkillOrder = "";
    private OverlayContentSettings _overlayContentSettings = new();

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

    [ObservableProperty]
    private bool _useGitHubMirror;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomMirror))]
    private GitHubMirrorOption? _selectedMirrorPreset;

    [ObservableProperty]
    private string _gitHubMirrorUrl = "https://ghgo.xyz/";

    public bool IsCustomMirror => SelectedMirrorPreset?.IsCustom == true;

    [ObservableProperty]
    private string _resourceStatus = "检测中...";

    [ObservableProperty]
    private bool _isResourceDownloading;

    [ObservableProperty]
    private double _downloadProgress;

    [ObservableProperty]
    private string _downloadProgressText = "";

    [ObservableProperty]
    private bool _hasResourceUpdate;

    [ObservableProperty]
    private bool _resourcesExist;

    public string CurrentVersion { get; } = AppUpdateService.GetCurrentVersion();

    [ObservableProperty]
    private string _appUpdateStatus = "";

    [ObservableProperty]
    private bool _hasAppUpdate;

    [ObservableProperty]
    private bool _isAppUpdateDownloading;

    [ObservableProperty]
    private double _appUpdateProgress;

    [ObservableProperty]
    private string _appUpdateProgressText = "";

    [ObservableProperty]
    private string _appUpdateReleaseNotes = "";

    public static IReadOnlyList<FrameRateOption> FrameRateLimitOptions { get; } =
    [
        new(30, "30 FPS"),
        new(60, "60 FPS"),
        new(0, "无限制"),
    ];

    public static IReadOnlyList<GitHubMirrorOption> MirrorPresets { get; } =
    [
        new("ghgo.xyz", "https://ghgo.xyz/"),
        new("gh-proxy.com", "https://gh-proxy.com/"),
        new("自定义", "", IsCustom: true),
    ];

    public int CaptureFrameRateLimit => SelectedFrameRateLimit.Value;

    public ObservableCollection<SukiColorTheme> AvailableColorThemes { get; } = new();
    public ObservableCollection<GpuDeviceInfo> InferenceDevices { get; } = new();

    public event Action<CaptureMethod>? CaptureMethodChanged;
    public event Action? YoloSettingsChanged;
    public event Action<int>? CaptureFrameRateLimitChanged;
    public event Action? ResourcesDownloaded;

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

    public SettingsViewModel(ISukiToastManager toastManager, OverlayService overlayService,
        ResourceService resourceService, AppUpdateService appUpdateService)
    {
        _toastManager = toastManager;
        _overlayService = overlayService;
        _resourceService = resourceService;
        _appUpdateService = appUpdateService;
        _selectedFrameRateLimit = FrameRateLimitOptions[1]; // 60 FPS
        _selectedMirrorPreset = MirrorPresets[0];
        _settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EndFieldFightHelper",
            "settings.json");

        _overlayService.OverlayPositionSizeChanged += OnOverlayPositionSizeFromWindow;

        RefreshInferenceDevices();
        InitializeColorThemes();
        LoadSettings();
        RefreshResourceStatus();
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

    partial void OnUseGitHubMirrorChanged(bool value)
    {
        ApplyMirrorToServices();
        if (!_isLoading) ScheduleSave();
    }

    partial void OnSelectedMirrorPresetChanged(GitHubMirrorOption? value)
    {
        if (value != null && !value.IsCustom)
            GitHubMirrorUrl = value.Url;

        ApplyMirrorToServices();
        if (!_isLoading) ScheduleSave();
    }

    partial void OnGitHubMirrorUrlChanged(string value)
    {
        ApplyMirrorToServices();
        if (!_isLoading) ScheduleSave();
    }

    private void ApplyMirrorToServices()
    {
        var prefix = UseGitHubMirror && !string.IsNullOrWhiteSpace(GitHubMirrorUrl)
            ? GitHubMirrorUrl
            : null;
        _appUpdateService.GitHubMirrorPrefix = prefix;
        _resourceService.GitHubMirrorPrefix = prefix;
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

                    UseGitHubMirror = settings.UseGitHubMirror;
                    GitHubMirrorUrl = settings.GitHubMirrorUrl;
                    SelectedMirrorPreset = MirrorPresets.FirstOrDefault(p => !p.IsCustom && p.Url == settings.GitHubMirrorUrl)
                                           ?? MirrorPresets[^1];

                    _dodgeDelayMs = settings.DodgeDelayMs;
                    _dodgeSuppressDuringSkill = settings.DodgeSuppressDuringSkill;
                    _homeAutoDodge = settings.IsAutoDodgeEnabled;
                    _homeAutoSkill = settings.IsAutoSkillEnabled;
                    _homeAutoSkillOrder = settings.AutoSkillOrder ?? "";
                    _homeAutoAttack = settings.IsAutoAttackEnabled;
                    _homeAutoUltimate = settings.IsAutoUltimateEnabled;
                    _homeAutoChainSkill = settings.IsAutoChainSkillEnabled;
                    _homeBattleOverlay = settings.IsBattleOverlayEnabled;
                    _overlayContentSettings = settings.OverlayContent ?? new OverlayContentSettings();
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
            ApplyMirrorToServices();
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
                UseGitHubMirror = UseGitHubMirror,
                GitHubMirrorUrl = GitHubMirrorUrl,
                DodgeDelayMs = _dodgeDelayMs,
                DodgeSuppressDuringSkill = _dodgeSuppressDuringSkill,
                IsAutoDodgeEnabled = _homeAutoDodge,
                IsAutoSkillEnabled = _homeAutoSkill,
                AutoSkillOrder = _homeAutoSkillOrder,
                IsAutoAttackEnabled = _homeAutoAttack,
                IsAutoUltimateEnabled = _homeAutoUltimate,
                IsAutoChainSkillEnabled = _homeAutoChainSkill,
                IsBattleOverlayEnabled = _homeBattleOverlay,
                OverlayContent = _overlayContentSettings,
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

    public int DodgeDelayMs => _dodgeDelayMs;
    public bool DodgeSuppressDuringSkill => _dodgeSuppressDuringSkill;

    public (int DelayMs, bool SuppressDuringSkill) GetDodgeSettings()
        => (_dodgeDelayMs, _dodgeSuppressDuringSkill);

    public void UpdateDodgeSettings(int delayMs, bool suppressDuringSkill)
    {
        _dodgeDelayMs = delayMs;
        _dodgeSuppressDuringSkill = suppressDuringSkill;
        ScheduleSave();
    }

    public (bool AutoDodge, bool AutoSkill, bool AutoAttack, bool AutoUltimate, bool AutoChainSkill, bool BattleOverlay) GetHomeToggles()
        => (_homeAutoDodge, _homeAutoSkill, _homeAutoAttack, _homeAutoUltimate, _homeAutoChainSkill, _homeBattleOverlay);

    public void UpdateHomeToggle(string name, bool value)
    {
        switch (name)
        {
            case nameof(AppSettings.IsAutoDodgeEnabled): _homeAutoDodge = value; break;
            case nameof(AppSettings.IsAutoSkillEnabled): _homeAutoSkill = value; break;
            case nameof(AppSettings.IsAutoAttackEnabled): _homeAutoAttack = value; break;
            case nameof(AppSettings.IsAutoUltimateEnabled): _homeAutoUltimate = value; break;
            case nameof(AppSettings.IsAutoChainSkillEnabled): _homeAutoChainSkill = value; break;
            case nameof(AppSettings.IsBattleOverlayEnabled): _homeBattleOverlay = value; break;
            default: return;
        }
        ScheduleSave();
    }

    public string GetAutoSkillOrder() => _homeAutoSkillOrder;

    public void UpdateAutoSkillOrder(string order)
    {
        _homeAutoSkillOrder = order;
        ScheduleSave();
    }

    public OverlayContentSettings GetOverlayContentSettings()
        => _overlayContentSettings;

    public void UpdateOverlayContentSettings(OverlayContentSettings settings)
    {
        _overlayContentSettings = settings;
        ScheduleSave();
    }

    public void AttachOverlay(OverlayViewModel overlayViewModel)
    {
        _overlayService.SetOverlayDataContext(overlayViewModel);
    }

    public void RefreshResourceStatus()
    {
        ResourcesExist = _resourceService.CheckResourcesExist();
        var meta = _resourceService.LoadMetadata();
        if (!ResourcesExist)
        {
            ResourceStatus = "未下载 — 部分功能需要游戏资源才能使用";
        }
        else if (meta != null && meta.LastUpdated != default)
        {
            ResourceStatus = $"已安装 — 更新于 {meta.LastUpdated.ToLocalTime():yyyy-MM-dd HH:mm}";
        }
        else
        {
            ResourceStatus = "已安装";
        }
    }

    [RelayCommand]
    private async Task CheckResourceUpdateAsync()
    {
        if (IsResourceDownloading) return;

        ResourceStatus = "正在检查更新...";
        var (hasUpdate, _, message) = await _resourceService.CheckForUpdateAsync();
        HasResourceUpdate = hasUpdate;

        if (hasUpdate)
        {
            ResourceStatus = $"有新版本可用 — {message}";
        }
        else
        {
            RefreshResourceStatus();
            if (!message.StartsWith("检查更新失败"))
                ResourceStatus += "（已是最新）";
            else
                ResourceStatus = message;
        }
    }

    [RelayCommand]
    private async Task DownloadResourcesAsync()
    {
        if (IsResourceDownloading) return;

        _downloadCts?.Cancel();
        _downloadCts = new CancellationTokenSource();
        var ct = _downloadCts.Token;

        IsResourceDownloading = true;
        DownloadProgress = 0;
        DownloadProgressText = "准备下载...";

        try
        {
            var progress = new Progress<(string Status, double Percent)>(p =>
            {
                DownloadProgressText = p.Status;
                DownloadProgress = p.Percent;
            });

            await _resourceService.DownloadResourcesAsync(progress, ct);

            HasResourceUpdate = false;
            RefreshResourceStatus();
            ResourcesDownloaded?.Invoke();
            _toastManager.CreateToast()
                .WithTitle("资源下载完成")
                .WithContent("游戏资源已成功下载并安装")
                .Dismiss().After(TimeSpan.FromSeconds(4))
                .Queue();
        }
        catch (OperationCanceledException)
        {
            DownloadProgressText = "下载已取消";
        }
        catch (Exception ex)
        {
            DownloadProgressText = $"下载失败: {ex.Message}";
            _toastManager.CreateToast()
                .WithTitle("资源下载失败")
                .WithContent(ex.Message)
                .Dismiss().After(TimeSpan.FromSeconds(6))
                .Queue();
        }
        finally
        {
            IsResourceDownloading = false;
        }
    }

    [RelayCommand]
    private void CancelDownload()
    {
        _downloadCts?.Cancel();
    }

    [RelayCommand]
    private async Task CheckAppUpdateAsync()
    {
        if (IsAppUpdateDownloading) return;

        AppUpdateStatus = "正在检查更新...";
        var (hasUpdate, info, message) = await _appUpdateService.CheckForUpdateAsync();
        HasAppUpdate = hasUpdate;
        _latestUpdateInfo = info;

        if (hasUpdate && info != null)
        {
            AppUpdateStatus = $"发现新版本 v{info.Version}";
            AppUpdateReleaseNotes = info.ReleaseNotes;
        }
        else
        {
            AppUpdateStatus = message;
            AppUpdateReleaseNotes = "";
        }
    }

    [RelayCommand]
    private async Task DownloadAppUpdateAsync()
    {
        if (IsAppUpdateDownloading || _latestUpdateInfo == null) return;

        _appUpdateCts?.Cancel();
        _appUpdateCts?.Dispose();
        _appUpdateCts = new CancellationTokenSource();
        var ct = _appUpdateCts.Token;

        IsAppUpdateDownloading = true;
        AppUpdateProgress = 0;
        AppUpdateProgressText = "准备下载...";

        try
        {
            var progress = new Progress<(string Status, double Percent)>(p =>
            {
                AppUpdateProgressText = p.Status;
                AppUpdateProgress = p.Percent;
            });

            await _appUpdateService.DownloadUpdateAsync(_latestUpdateInfo, progress, ct);
            _appUpdateService.ApplyUpdateAndRestart();
        }
        catch (OperationCanceledException)
        {
            AppUpdateProgressText = "下载已取消";
            _appUpdateService.CleanupPendingUpdate();
        }
        catch (Exception ex)
        {
            AppUpdateProgressText = $"下载失败: {ex.Message}";
            _appUpdateService.CleanupPendingUpdate();
            _toastManager.CreateToast()
                .WithTitle("更新下载失败")
                .WithContent(ex.Message)
                .Dismiss().After(TimeSpan.FromSeconds(6))
                .Queue();
        }
        finally
        {
            IsAppUpdateDownloading = false;
        }
    }

    [RelayCommand]
    private void CancelAppUpdate()
    {
        _appUpdateCts?.Cancel();
    }

    public async Task CheckAppUpdateOnStartupAsync()
    {
        try
        {
            var (hasUpdate, info, _) = await _appUpdateService.CheckForUpdateAsync();
            if (hasUpdate && info != null)
            {
                HasAppUpdate = true;
                _latestUpdateInfo = info;
                AppUpdateStatus = $"发现新版本 v{info.Version}";
                AppUpdateReleaseNotes = info.ReleaseNotes;
                _toastManager.CreateToast()
                    .WithTitle("软件有更新")
                    .WithContent($"发现新版本 v{info.Version}，请在设置页面中更新。")
                    .Dismiss().After(TimeSpan.FromSeconds(6))
                    .Queue();
            }
        }
        catch
        {
            // Silently ignore startup check failures
        }
    }

    public async Task CheckResourcesOnStartupAsync()
    {
        if (!_resourceService.CheckResourcesExist())
        {
            _toastManager.CreateToast()
                .WithTitle("游戏资源未安装")
                .WithContent("排轴和队伍配置功能需要游戏资源。请在设置页面中下载。")
                .Dismiss().After(TimeSpan.FromSeconds(8))
                .Queue();
            return;
        }

        var (hasUpdate, _, message) = await _resourceService.CheckForUpdateAsync();
        if (hasUpdate)
        {
            HasResourceUpdate = true;
            ResourceStatus = $"有新版本可用 — {message}";
            _toastManager.CreateToast()
                .WithTitle("资源有更新")
                .WithContent("检测到新版本的游戏资源，请在设置页面中更新。")
                .Dismiss().After(TimeSpan.FromSeconds(6))
                .Queue();
        }
    }

    public void Dispose()
    {
        _overlayService.OverlayPositionSizeChanged -= OnOverlayPositionSizeFromWindow;
        _saveCts?.Cancel();
        _saveCts?.Dispose();
        _downloadCts?.Cancel();
        _downloadCts?.Dispose();
        _appUpdateCts?.Cancel();
        _appUpdateCts?.Dispose();
    }
}

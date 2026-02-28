using System;
using System.Collections.ObjectModel;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EndFieldFightHelper.Helpers;
using EndFieldFightHelper.Models;
using EndFieldFightHelper.Services;

namespace EndFieldFightHelper.ViewModels;

public partial class HomeViewModel : ViewModelBase, IDisposable
{
    private readonly IScreenshotService _screenshotService;
    private readonly OverlayService _overlayService;
    private readonly RecognitionPipelineService _pipelineService;
    private readonly AutoDodgeService _autoDodgeService;
    private SettingsViewModel? _settingsViewModel;
    private OverlayViewModel? _overlayViewModel;
    private DebugViewModel? _debugViewModel;
    private Timer? _previewTimer;
    private CaptureMethod _captureMethod = CaptureMethod.PrintWindow;

    [ObservableProperty]
    private WindowInfo? _endfieldWindow;

    [ObservableProperty]
    private bool _isWindowBound;

    [ObservableProperty]
    private string _windowStatusText = "未检测到 Endfield 窗口，请先启动游戏";

    [ObservableProperty]
    private Avalonia.Media.Imaging.Bitmap? _previewImage;

    [ObservableProperty]
    private bool _isPreviewRunning;

    [ObservableProperty]
    private bool _isBattleAssistEnabled;

    [ObservableProperty]
    private bool _isAutoDodgeEnabled;

    [ObservableProperty]
    private bool _isAutoSkillEnabled;

    [ObservableProperty]
    private bool _isAutoAttackEnabled;

    [ObservableProperty]
    private bool _isAutoUltimateEnabled;

    [ObservableProperty]
    private bool _isBattleOverlayEnabled;

    public ObservableCollection<string> LogMessages { get; } = new();

    public RecognitionPipelineService PipelineService => _pipelineService;

    public HomeViewModel(IScreenshotService screenshotService, OverlayService overlayService,
        RecognitionPipelineService pipelineService, AutoDodgeService autoDodgeService)
    {
        _screenshotService = screenshotService;
        _overlayService = overlayService;
        _pipelineService = pipelineService;
        _autoDodgeService = autoDodgeService;
        _pipelineService.Log += msg => AddLog(msg);
        _autoDodgeService.Log += msg => AddLog(msg);
        FindEndfieldWindow();
    }

    public void SetSettingsViewModel(SettingsViewModel settingsViewModel)
    {
        _settingsViewModel = settingsViewModel;
    }

    public void SetOverlayViewModel(OverlayViewModel overlayViewModel)
    {
        _overlayViewModel = overlayViewModel;
    }

    public void SetDebugViewModel(DebugViewModel debugViewModel)
    {
        _debugViewModel = debugViewModel;
    }

    private IntPtr? GetEffectiveWindowHandle()
    {
        if (_debugViewModel is { IsDebugEnabled: true, SelectedDebugWindow: { } debugWindow })
            return debugWindow.Handle;
        return EndfieldWindow?.Handle;
    }

    public void SetCaptureMethod(CaptureMethod method)
    {
        _captureMethod = method;
        AddLog($"截图方式已切换: {method}");
    }

    [RelayCommand]
    private void RefreshWindow()
    {
        FindEndfieldWindow();
    }

    private const string EndfieldProcessName = "Endfield";

    private void FindEndfieldWindow()
    {
        var windows = Win32Helper.GetVisibleWindows();
        var endfield = windows.FirstOrDefault(w =>
            w.ProcessName.Equals(EndfieldProcessName, StringComparison.OrdinalIgnoreCase));

        if (endfield != null)
        {
            EndfieldWindow = endfield;
            IsWindowBound = true;
            WindowStatusText = $"已绑定: {endfield.Title} (PID: {endfield.ProcessId})";
            AddLog($"已绑定 Endfield 窗口: {endfield.Title}");
        }
        else
        {
            EndfieldWindow = null;
            IsWindowBound = false;
            WindowStatusText = "未检测到 Endfield 窗口，请先启动游戏";
            AddLog("未检测到 Endfield 进程，请确认游戏已启动后点击刷新");
        }
    }

    [RelayCommand]
    private void TogglePreview()
    {
        if (GetEffectiveWindowHandle() == null)
        {
            AddLog("无法开始预览：未绑定窗口");
            return;
        }

        if (IsPreviewRunning)
        {
            StopPreview();
        }
        else
        {
            StartPreview();
        }
    }

    private void StartPreview()
    {
        if (GetEffectiveWindowHandle() == null) return;

        IsPreviewRunning = true;
        AddLog("开始截图预览");

        _previewTimer = new Timer(CapturePreviewFrame, null, 0, 500);
    }

    private void StopPreview()
    {
        _previewTimer?.Dispose();
        _previewTimer = null;
        IsPreviewRunning = false;
        AddLog("停止截图预览");
    }

    private void CapturePreviewFrame(object? state)
    {
        var hWnd = GetEffectiveWindowHandle();
        if (hWnd == null || !IsPreviewRunning) return;

        try
        {
            var bitmap = _screenshotService.CaptureWindow(hWnd.Value, _captureMethod);
            if (bitmap != null)
            {
                using var stream = new MemoryStream();
                bitmap.Save(stream, ImageFormat.Png);
                stream.Position = 0;
                var avaloniaBitmap = new Avalonia.Media.Imaging.Bitmap(stream);
                bitmap.Dispose();

                Dispatcher.UIThread.Post(() =>
                {
                    var old = PreviewImage;
                    PreviewImage = avaloniaBitmap;
                    old?.Dispose();
                });
            }
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() => AddLog($"截图失败: {ex.Message}"));
        }
    }

    [RelayCommand]
    private void ToggleBattleAssist()
    {
        if (!IsBattleAssistEnabled)
        {
            if (!IsWindowBound && GetEffectiveWindowHandle() == null)
                FindEndfieldWindow();

            var targetHandle = GetEffectiveWindowHandle();
            if (targetHandle == null)
            {
                AddLog("启动失败：未绑定窗口");
                return;
            }

            _pipelineService.Start(targetHandle.Value, _captureMethod);

            if (!_pipelineService.IsRunning)
            {
                AddLog("启动失败：识别管道未能启动");
                return;
            }

            IsBattleAssistEnabled = true;
            AddLog("战斗辅助已开启");

            if (IsAutoDodgeEnabled)
                _autoDodgeService.Start(targetHandle.Value);

            if (IsBattleOverlayEnabled)
                ApplyBattleOverlay();
        }
        else
        {
            _autoDodgeService.Stop();
            _pipelineService.Stop();
            AddLog("战斗辅助已关闭");
            if (IsPreviewRunning) StopPreview();
            if (IsBattleOverlayEnabled)
            {
                _overlayService.ApplySettings(false, 0, 0, OverlayDefaults.Width, OverlayDefaults.Height, OverlayDefaults.Opacity);
            }
            IsBattleAssistEnabled = false;
        }
    }

    partial void OnIsAutoDodgeEnabledChanged(bool value)
    {
        if (value)
        {
            AddLog("自动闪避已开启");
            var handle = _pipelineService.TargetWindowHandle;
            if (handle != IntPtr.Zero && _pipelineService.IsRunning)
                _autoDodgeService.Start(handle);
        }
        else
        {
            _autoDodgeService.Stop();
            AddLog("自动闪避已关闭");
        }
    }

    partial void OnIsAutoSkillEnabledChanged(bool value)
    {
        AddLog(value ? "自动技能已开启" : "自动技能已关闭");
    }

    partial void OnIsAutoAttackEnabledChanged(bool value)
    {
        AddLog(value ? "自动攻击已开启" : "自动攻击已关闭");
    }

    partial void OnIsAutoUltimateEnabledChanged(bool value)
    {
        AddLog(value ? "自动终结技已开启" : "自动终结技已关闭");
    }

    partial void OnIsBattleOverlayEnabledChanged(bool value)
    {
        if (value)
        {
            AddLog("战斗叠加层已开启");
            ApplyBattleOverlay();
        }
        else
        {
            AddLog("战斗叠加层已关闭");
            _overlayService.ApplySettings(false, 0, 0, OverlayDefaults.Width, OverlayDefaults.Height, OverlayDefaults.Opacity);
        }
    }

    private void ApplyBattleOverlay()
    {
        var hWnd = GetEffectiveWindowHandle();
        if (hWnd == null)
        {
            AddLog("无法显示叠加层：未绑定窗口");
            IsBattleOverlayEnabled = false;
            return;
        }

        if (_settingsViewModel != null)
        {
            _overlayService.ApplySettings(true,
                _settingsViewModel.OverlayX,
                _settingsViewModel.OverlayY,
                _settingsViewModel.OverlayWidth,
                _settingsViewModel.OverlayHeight,
                _settingsViewModel.OverlayOpacity);
        }
        else
        {
            var rect = Win32Helper.GetWindowRectDwm(hWnd.Value);
            _overlayService.ApplySettings(true, rect.Left + 10, rect.Top + 50, OverlayDefaults.Width, OverlayDefaults.Height, OverlayDefaults.Opacity);
        }
    }

    [RelayCommand]
    private void ClearLog()
    {
        LogMessages.Clear();
    }

    public void AddLog(string message)
    {
        var entry = $"[{DateTime.Now:HH:mm:ss}] {message}";
        if (Dispatcher.UIThread.CheckAccess())
        {
            LogMessages.Add(entry);
        }
        else
        {
            Dispatcher.UIThread.Post(() => LogMessages.Add(entry));
        }

        _overlayViewModel?.AddLog(entry);
    }

    public void Dispose()
    {
        _autoDodgeService.Dispose();
        _pipelineService.Dispose();
        _previewTimer?.Dispose();
        _previewTimer = null;
        PreviewImage?.Dispose();
        PreviewImage = null;
    }
}

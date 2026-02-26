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
        if (!IsWindowBound)
        {
            AddLog("无法开始预览：未绑定 Endfield 窗口");
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
        if (EndfieldWindow == null) return;

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
        if (EndfieldWindow == null || !IsPreviewRunning) return;

        try
        {
            var bitmap = _screenshotService.CaptureWindow(EndfieldWindow, _captureMethod);
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

    partial void OnIsBattleAssistEnabledChanged(bool value)
    {
        if (value)
        {
            AddLog("战斗辅助已开启");
            if (!IsWindowBound)
            {
                FindEndfieldWindow();
            }

            if (EndfieldWindow != null)
            {
                _pipelineService.Start(EndfieldWindow.Handle, _captureMethod);
                if (IsAutoDodgeEnabled)
                    _autoDodgeService.Start(EndfieldWindow.Handle);
            }
            else
            {
                AddLog("无法启动识别管道：未绑定 Endfield 窗口");
                IsBattleAssistEnabled = false;
            }
        }
        else
        {
            _autoDodgeService.Stop();
            _pipelineService.Stop();
            AddLog("战斗辅助已关闭");
            if (IsPreviewRunning) StopPreview();
            if (IsBattleOverlayEnabled)
            {
                IsBattleOverlayEnabled = false;
            }
            IsAutoDodgeEnabled = false;
            IsAutoSkillEnabled = false;
            IsAutoAttackEnabled = false;
            IsAutoUltimateEnabled = false;
        }
    }

    partial void OnIsAutoDodgeEnabledChanged(bool value)
    {
        if (value)
        {
            AddLog("自动闪避已开启");
            if (EndfieldWindow != null && _pipelineService.IsRunning)
                _autoDodgeService.Start(EndfieldWindow.Handle);
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
            _overlayService.ApplySettings(false, true, 0, 0, 300, 200, 0.85);
        }
    }

    private void ApplyBattleOverlay()
    {
        if (EndfieldWindow == null)
        {
            AddLog("无法显示叠加层：未绑定 Endfield 窗口");
            IsBattleOverlayEnabled = false;
            return;
        }

        var rect = Win32Helper.GetWindowRectDwm(EndfieldWindow.Handle);
        const double overlayWidth = 260;
        const double overlayHeight = 200;
        double x = rect.Left + 10;
        double y = rect.Top + (rect.Height - overlayHeight) / 2.0;

        _overlayService.ApplySettings(true, true, x, y, overlayWidth, overlayHeight, 0.85);
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
    }

    public void Dispose()
    {
        _autoDodgeService.Dispose();
        _pipelineService.Stop();
        _previewTimer?.Dispose();
        _previewTimer = null;
        PreviewImage?.Dispose();
        PreviewImage = null;
    }
}

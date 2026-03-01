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
    private readonly AutoAttackService _autoAttackService;
    private readonly AutoUltimateService _autoUltimateService;
    private readonly AutoChainSkillService _autoChainSkillService;
    private readonly AutoBattleSkillService _autoBattleSkillService;
    private readonly ActiveCharacterService _activeCharacterService;
    private readonly BattleStateService _battleStateService;
    private SettingsViewModel? _settingsViewModel;
    private OverlayViewModel? _overlayViewModel;
    private DebugViewModel? _debugViewModel;
    private Timer? _previewTimer;
    private CaptureMethod _captureMethod = CaptureMethod.PrintWindow;
    private bool _isLoadingToggles;

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
    private bool _isAutoChainSkillEnabled;

    [ObservableProperty]
    private bool _isBattleOverlayEnabled;

    public ObservableCollection<string> LogMessages { get; } = new();

    public RecognitionPipelineService PipelineService => _pipelineService;

    public HomeViewModel(IScreenshotService screenshotService, OverlayService overlayService,
        RecognitionPipelineService pipelineService, AutoDodgeService autoDodgeService,
        AutoAttackService autoAttackService, AutoUltimateService autoUltimateService,
        AutoChainSkillService autoChainSkillService, AutoBattleSkillService autoBattleSkillService,
        ActiveCharacterService activeCharacterService, BattleStateService battleStateService)
    {
        _screenshotService = screenshotService;
        _overlayService = overlayService;
        _pipelineService = pipelineService;
        _autoDodgeService = autoDodgeService;
        _autoAttackService = autoAttackService;
        _autoUltimateService = autoUltimateService;
        _autoChainSkillService = autoChainSkillService;
        _autoBattleSkillService = autoBattleSkillService;
        _activeCharacterService = activeCharacterService;
        _battleStateService = battleStateService;
        _pipelineService.Log += msg => AddLog(msg);
        _autoDodgeService.Log += msg => AddLog(msg);
        _autoAttackService.Log += msg => AddLog(msg);
        _autoUltimateService.Log += msg => AddLog(msg);
        _autoChainSkillService.Log += msg => AddLog(msg);
        _autoBattleSkillService.Log += msg => AddLog(msg);
        _activeCharacterService.Log += msg => AddLog(msg);
        _battleStateService.Log += msg => AddLog(msg);
        _battleStateService.BattleEntered += OnBattleEntered;
        _battleStateService.BattleExited += OnBattleExited;
        FindEndfieldWindow();
    }

    public void SetSettingsViewModel(SettingsViewModel settingsViewModel)
    {
        _settingsViewModel = settingsViewModel;

        _isLoadingToggles = true;
        try
        {
            var t = settingsViewModel.GetHomeToggles();
            IsAutoDodgeEnabled = t.AutoDodge;
            IsAutoSkillEnabled = t.AutoSkill;
            IsAutoAttackEnabled = t.AutoAttack;
            IsAutoUltimateEnabled = t.AutoUltimate;
            IsAutoChainSkillEnabled = t.AutoChainSkill;
            IsBattleOverlayEnabled = t.BattleOverlay;
        }
        finally
        {
            _isLoadingToggles = false;
        }
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
            AddLog("战斗辅助已开启，等待进入战斗...");

            _battleStateService.Start();
        }
        else
        {
            _battleStateService.Stop();
            // OnBattleExited is dispatched via Post, so explicit stops ensure synchronous shutdown
            _activeCharacterService.Stop();
            _autoDodgeService.Stop();
            _autoAttackService.Stop();
            _autoUltimateService.Stop();
            _autoChainSkillService.Stop();
            _autoBattleSkillService.Stop();
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

    private void OnBattleEntered()
    {
        Dispatcher.UIThread.Post(() =>
        {
            _activeCharacterService.Start();

            var handle = _pipelineService.TargetWindowHandle;
            if (IsAutoDodgeEnabled && handle != IntPtr.Zero)
                _autoDodgeService.Start(handle);

            if (IsAutoAttackEnabled && handle != IntPtr.Zero)
                _autoAttackService.Start(handle);

            if (IsAutoUltimateEnabled && handle != IntPtr.Zero)
                _autoUltimateService.Start(handle);

            if (IsAutoChainSkillEnabled && handle != IntPtr.Zero)
                _autoChainSkillService.Start(handle);

            if (IsAutoSkillEnabled && handle != IntPtr.Zero)
                _autoBattleSkillService.Start(handle);

            if (IsBattleOverlayEnabled)
                ApplyBattleOverlay();
        });
    }

    private void OnBattleExited()
    {
        Dispatcher.UIThread.Post(() =>
        {
            _autoDodgeService.Stop();
            _autoAttackService.Stop();
            _autoUltimateService.Stop();
            _autoChainSkillService.Stop();
            _autoBattleSkillService.Stop();
            _activeCharacterService.Stop();

            if (IsBattleOverlayEnabled)
                _overlayService.ApplySettings(false, 0, 0, OverlayDefaults.Width, OverlayDefaults.Height, OverlayDefaults.Opacity);
        });
    }

    partial void OnIsAutoDodgeEnabledChanged(bool value)
    {
        if (_isLoadingToggles) return;

        if (value)
        {
            AddLog("自动闪避已开启");
            var handle = _pipelineService.TargetWindowHandle;
            if (handle != IntPtr.Zero && _pipelineService.IsRunning && _battleStateService.IsBattleActive)
                _autoDodgeService.Start(handle);
        }
        else
        {
            _autoDodgeService.Stop();
            AddLog("自动闪避已关闭");
        }

        _settingsViewModel?.UpdateHomeToggle(nameof(AppSettings.IsAutoDodgeEnabled), value);
    }

    partial void OnIsAutoSkillEnabledChanged(bool value)
    {
        if (_isLoadingToggles) return;

        if (value)
        {
            AddLog("自动战技已开启");
            var handle = _pipelineService.TargetWindowHandle;
            if (handle != IntPtr.Zero && _pipelineService.IsRunning && _battleStateService.IsBattleActive)
                _autoBattleSkillService.Start(handle);
        }
        else
        {
            _autoBattleSkillService.Stop();
            AddLog("自动战技已关闭");
        }

        _settingsViewModel?.UpdateHomeToggle(nameof(AppSettings.IsAutoSkillEnabled), value);
    }

    partial void OnIsAutoAttackEnabledChanged(bool value)
    {
        if (_isLoadingToggles) return;

        if (value)
        {
            AddLog("自动攻击已开启");
            var handle = _pipelineService.TargetWindowHandle;
            if (handle != IntPtr.Zero && _pipelineService.IsRunning && _battleStateService.IsBattleActive)
                _autoAttackService.Start(handle);
        }
        else
        {
            _autoAttackService.Stop();
            AddLog("自动攻击已关闭");
        }

        _settingsViewModel?.UpdateHomeToggle(nameof(AppSettings.IsAutoAttackEnabled), value);
    }

    partial void OnIsAutoUltimateEnabledChanged(bool value)
    {
        if (_isLoadingToggles) return;

        if (value)
        {
            AddLog("自动终结技已开启");
            var handle = _pipelineService.TargetWindowHandle;
            if (handle != IntPtr.Zero && _pipelineService.IsRunning && _battleStateService.IsBattleActive)
                _autoUltimateService.Start(handle);
        }
        else
        {
            _autoUltimateService.Stop();
            AddLog("自动终结技已关闭");
        }

        _settingsViewModel?.UpdateHomeToggle(nameof(AppSettings.IsAutoUltimateEnabled), value);
    }

    partial void OnIsAutoChainSkillEnabledChanged(bool value)
    {
        if (_isLoadingToggles) return;

        if (value)
        {
            AddLog("自动连携技已开启");
            var handle = _pipelineService.TargetWindowHandle;
            if (handle != IntPtr.Zero && _pipelineService.IsRunning && _battleStateService.IsBattleActive)
                _autoChainSkillService.Start(handle);
        }
        else
        {
            _autoChainSkillService.Stop();
            AddLog("自动连携技已关闭");
        }

        _settingsViewModel?.UpdateHomeToggle(nameof(AppSettings.IsAutoChainSkillEnabled), value);
    }

    partial void OnIsBattleOverlayEnabledChanged(bool value)
    {
        if (_isLoadingToggles) return;

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

        _settingsViewModel?.UpdateHomeToggle(nameof(AppSettings.IsBattleOverlayEnabled), value);
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
        _battleStateService.Dispose();
        _activeCharacterService.Dispose();
        _autoUltimateService.Dispose();
        _autoChainSkillService.Dispose();
        _autoBattleSkillService.Dispose();
        _autoAttackService.Dispose();
        _autoDodgeService.Dispose();
        _pipelineService.Dispose();
        _previewTimer?.Dispose();
        _previewTimer = null;
        PreviewImage?.Dispose();
        PreviewImage = null;
    }
}

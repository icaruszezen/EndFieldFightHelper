using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
    private readonly AutoAxisService _autoAxisService;
    private readonly IDialogService _dialogService;
    private readonly SettingsViewModel _settingsViewModel;
    private readonly OverlayViewModel _overlayViewModel;
    private readonly BattleAxisViewModel _battleAxisViewModel;
    private readonly TeamSetupViewModel _teamSetupViewModel;
    private DebugViewModel? _debugViewModel;
    private IReadOnlyList<IPipelineStatusProvider>? _pipelineProviders;
    private Timer? _previewTimer;
    private CaptureMethod _captureMethod = CaptureMethod.PrintWindow;
    private bool _isLoadingToggles;
    private string _autoSkillOrder = "";

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
    private bool _isAutoAxisEnabled;

    [ObservableProperty]
    private bool _isBattleOverlayEnabled;

    public ObservableCollection<string> LogMessages { get; } = new();

    public RecognitionPipelineService PipelineService => _pipelineService;

    public HomeViewModel(IScreenshotService screenshotService, OverlayService overlayService,
        RecognitionPipelineService pipelineService, AutoDodgeService autoDodgeService,
        AutoAttackService autoAttackService, AutoUltimateService autoUltimateService,
        AutoChainSkillService autoChainSkillService, AutoBattleSkillService autoBattleSkillService,
        ActiveCharacterService activeCharacterService, BattleStateService battleStateService,
        AutoAxisService autoAxisService, IDialogService dialogService,
        SettingsViewModel settingsViewModel, OverlayViewModel overlayViewModel,
        BattleAxisViewModel battleAxisViewModel, TeamSetupViewModel teamSetupViewModel)
    {
        _screenshotService = screenshotService;
        _overlayService = overlayService;
        _pipelineService = pipelineService;
        _dialogService = dialogService;
        _settingsViewModel = settingsViewModel;
        _overlayViewModel = overlayViewModel;
        _battleAxisViewModel = battleAxisViewModel;
        _teamSetupViewModel = teamSetupViewModel;
        _autoDodgeService = autoDodgeService;
        _autoAttackService = autoAttackService;
        _autoUltimateService = autoUltimateService;
        _autoChainSkillService = autoChainSkillService;
        _autoBattleSkillService = autoBattleSkillService;
        _activeCharacterService = activeCharacterService;
        _battleStateService = battleStateService;
        _autoAxisService = autoAxisService;
        _pipelineService.Log += AddLog;
        _autoDodgeService.Log += AddLog;
        _autoAttackService.Log += AddLog;
        _autoUltimateService.Log += AddLog;
        _autoChainSkillService.Log += AddLog;
        _autoBattleSkillService.Log += AddLog;
        _activeCharacterService.Log += AddLog;
        _battleStateService.Log += AddLog;
        _autoAxisService.Log += AddLog;
        _battleStateService.BattleEntered += OnBattleEntered;
        _battleStateService.BattleExited += OnBattleExited;
        _battleStateService.CameraScanStarting += OnCameraScanStarting;
        _autoDodgeService.DodgeTriggered += OnDodgeTriggered;

        _isLoadingToggles = true;
        try
        {
            var t = settingsViewModel.GetHomeToggles();
            IsAutoDodgeEnabled = t.AutoDodge;
            IsAutoSkillEnabled = t.AutoSkill;
            IsAutoAttackEnabled = t.AutoAttack;
            IsAutoUltimateEnabled = t.AutoUltimate;
            IsAutoChainSkillEnabled = t.AutoChainSkill;
            IsAutoAxisEnabled = t.AutoAxis;
            IsBattleOverlayEnabled = t.BattleOverlay;

            _autoSkillOrder = settingsViewModel.GetAutoSkillOrder();
            _autoBattleSkillService.SetSkillOrder(ParseSkillOrder(_autoSkillOrder));
        }
        finally
        {
            _isLoadingToggles = false;
        }

        FindEndfieldWindow();
    }

    public void SetDebugViewModel(DebugViewModel debugViewModel)
    {
        _debugViewModel = debugViewModel;
    }

    public void SetPipelineProviders(IReadOnlyList<IPipelineStatusProvider> providers)
    {
        _pipelineProviders = providers;
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

            _battleStateService.Start(targetHandle.Value);
        }
        else
        {
            _battleStateService.Stop();
            _activeCharacterService.Stop();
            _autoDodgeService.Stop();
            _autoAttackService.Stop();
            _autoUltimateService.Stop();
            _autoChainSkillService.Stop();
            _autoBattleSkillService.Stop();
            _autoAxisService.Stop();
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
            TryRunService(() => _activeCharacterService.Start());

            var handle = _pipelineService.TargetWindowHandle;
            if (IsAutoDodgeEnabled && handle != IntPtr.Zero)
                TryRunService(() => _autoDodgeService.Start(handle));

            var axisStarted = IsAutoAxisEnabled && handle != IntPtr.Zero && TryStartAutoAxis(handle);

            if (!axisStarted)
            {
                if (IsAutoAttackEnabled && handle != IntPtr.Zero)
                    TryRunService(() => _autoAttackService.Start(handle));

                if (IsAutoUltimateEnabled && handle != IntPtr.Zero)
                    TryRunService(() => _autoUltimateService.Start(handle));

                if (IsAutoChainSkillEnabled && handle != IntPtr.Zero)
                    TryRunService(() => _autoChainSkillService.Start(handle));

                if (IsAutoSkillEnabled && handle != IntPtr.Zero)
                    TryRunService(() => _autoBattleSkillService.Start(handle));
            }

            if (IsBattleOverlayEnabled)
                ApplyBattleOverlay();

            TryStartAxisPlayback();
        });
    }

    private void OnBattleExited()
    {
        Dispatcher.UIThread.Post(() =>
        {
            TryRunService(() => _autoDodgeService.Stop());
            TryRunService(() => _autoAttackService.Stop());
            TryRunService(() => _autoUltimateService.Stop());
            TryRunService(() => _autoChainSkillService.Stop());
            TryRunService(() => _autoBattleSkillService.Stop());
            TryRunService(() => _autoAxisService.Stop());
            TryRunService(() => _activeCharacterService.Stop());

            _overlayViewModel.StopAxisPlayback();
        });
    }

    private void TryRunService(Action action)
    {
        try { action(); }
        catch (Exception ex) { AddLog($"服务操作失败: {ex.Message}"); }
    }

    private void TryStartAxisPlayback()
    {
        if (!_overlayViewModel.ShowBattleAxis) return;
        if (!_battleAxisViewModel.HasData) return;

        var actions = BuildOverlayBattleActions();
        if (actions.Count > 0)
        {
            Func<double>? timeSource = IsAutoAxisEnabled
                ? () => _autoAxisService.ElapsedSeconds
                : null;
            _overlayViewModel.StartAxisPlayback(actions, timeSource);
        }
    }

    private List<OverlayBattleAction> BuildOverlayBattleActions()
    {
        if (_battleAxisViewModel.Tracks == null) return [];

        return _battleAxisViewModel.Tracks
            .SelectMany(track => track.Actions.Select(a => new OverlayBattleAction
            {
                CharacterName = track.CharacterName,
                TypeLabel = BattleAxisViewModel.ActionTypeLabels.GetValueOrDefault(a.Type, a.TypeLabel),
                StartTime = a.StartTime,
                Duration = a.Duration,
            }))
            .OrderBy(a => a.StartTime)
            .ToList();
    }

    private bool TryStartAutoAxis(IntPtr handle)
    {
        var events = BuildAxisTimelineEvents();
        if (events.Count == 0)
        {
            AddLog("自动打轴启动失败：未导入战斗轴数据或数据为空");
            return false;
        }

        var unmappedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var evt in events)
        {
            if (!string.IsNullOrEmpty(evt.CharacterId) && FindSlotForCharacter(evt.CharacterId) == null)
                unmappedIds.Add(evt.CharacterId);
        }

        if (unmappedIds.Count > 0)
        {
            AddLog($"自动打轴启动失败：以下角色未在队伍中配置槽位：{string.Join(", ", unmappedIds)}");
            return false;
        }

        _autoAxisService.Start(handle, events, FindSlotForCharacter);
        return true;
    }

    private List<AxisTimelineEvent> BuildAxisTimelineEvents()
    {
        if (!_battleAxisViewModel.HasData || _battleAxisViewModel.SelectedScenario is not { } scenario)
            return [];

        var data = scenario.Scenario.Data;
        var prepOffset = data.PrepDuration;
        var events = new List<AxisTimelineEvent>();

        if (data.SwitchEvents != null)
        {
            foreach (var sw in data.SwitchEvents)
            {
                var time = sw.Time - prepOffset;
                if (time >= 0)
                    events.Add(new AxisTimelineEvent(time, AxisEventType.Switch, sw.CharacterId));
            }
        }

        foreach (var track in data.Tracks)
        {
            foreach (var action in track.Actions)
            {
                if (action.IsDisabled) continue;
                var type = action.Type switch
                {
                    "attack" => (AxisEventType?)AxisEventType.Attack,
                    "skill" => AxisEventType.Skill,
                    "link" => AxisEventType.Link,
                    "ultimate" => AxisEventType.Ultimate,
                    _ => null,
                };
                if (type == null) continue;
                var time = action.StartTime - prepOffset;
                if (time < 0) continue;
                events.Add(new AxisTimelineEvent(time, type.Value, track.Id, action.Duration));
            }
        }

        events.Sort((a, b) =>
        {
            var cmp = a.Time.CompareTo(b.Time);
            return cmp != 0 ? cmp : a.Type.CompareTo(b.Type);
        });
        return events;
    }

    private int? FindSlotForCharacter(string characterId)
    {
        for (var i = 1; i <= 4; i++)
        {
            var slot = _teamSetupViewModel.GetSlot(i);
            if (slot != null && string.Equals(slot.Id, characterId, StringComparison.Ordinal))
                return i;
        }
        return null;
    }

    private void OnDodgeTriggered()
    {
        if (_autoAxisService.IsRunning)
            _autoAxisService.RequestPause(200);
    }

    private async Task OnCameraScanStarting()
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _autoDodgeService.Stop();
            _autoAttackService.Stop();
            _autoUltimateService.Stop();
            _autoChainSkillService.Stop();
            _autoBattleSkillService.Stop();
            _autoAxisService.Stop();
            _activeCharacterService.Stop();
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

        _settingsViewModel.UpdateHomeToggle(nameof(AppSettings.IsAutoDodgeEnabled), value);
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

        _settingsViewModel.UpdateHomeToggle(nameof(AppSettings.IsAutoSkillEnabled), value);
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

        _settingsViewModel.UpdateHomeToggle(nameof(AppSettings.IsAutoAttackEnabled), value);
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

        _settingsViewModel.UpdateHomeToggle(nameof(AppSettings.IsAutoUltimateEnabled), value);
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

        _settingsViewModel.UpdateHomeToggle(nameof(AppSettings.IsAutoChainSkillEnabled), value);
    }

    partial void OnIsAutoAxisEnabledChanged(bool value)
    {
        if (_isLoadingToggles) return;

        var handle = _pipelineService.TargetWindowHandle;
        var inBattle = handle != IntPtr.Zero && _pipelineService.IsRunning && _battleStateService.IsBattleActive;

        if (value)
        {
            AddLog("自动打轴已开启");
            if (inBattle)
            {
                var started = TryStartAutoAxis(handle);
                if (started)
                {
                    _autoAttackService.Stop();
                    _autoUltimateService.Stop();
                    _autoChainSkillService.Stop();
                    _autoBattleSkillService.Stop();
                }
            }
        }
        else
        {
            _autoAxisService.Stop();
            AddLog("自动打轴已关闭");

            if (inBattle)
            {
                if (IsAutoAttackEnabled)
                    _autoAttackService.Start(handle);
                if (IsAutoUltimateEnabled)
                    _autoUltimateService.Start(handle);
                if (IsAutoChainSkillEnabled)
                    _autoChainSkillService.Start(handle);
                if (IsAutoSkillEnabled)
                    _autoBattleSkillService.Start(handle);
            }
        }

        _settingsViewModel.UpdateHomeToggle(nameof(AppSettings.IsAutoAxisEnabled), value);
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

        _settingsViewModel.UpdateHomeToggle(nameof(AppSettings.IsBattleOverlayEnabled), value);
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

        _overlayService.ApplySettings(true,
            _settingsViewModel.OverlayX,
            _settingsViewModel.OverlayY,
            _settingsViewModel.OverlayWidth,
            _settingsViewModel.OverlayHeight,
            _settingsViewModel.OverlayOpacity);
    }

    [RelayCommand]
    private async Task OpenDodgeSettings()
    {
        var (delay, suppress) = _settingsViewModel.GetDodgeSettings();
        var result = await _dialogService.ShowDodgeSettingsAsync(delay, suppress);

        if (result is { } r)
        {
            _settingsViewModel.UpdateDodgeSettings(r.DelayMs, r.SuppressDuringSkill);
            AddLog($"闪避设置已更新：延迟 {r.DelayMs}ms，战技暂停 {(r.SuppressDuringSkill ? "开启" : "关闭")}");
        }
    }

    [RelayCommand]
    private async Task OpenAutoSkillOrderSettings()
    {
        var result = await _dialogService.ShowAutoSkillOrderAsync(_autoSkillOrder);

        if (result != null)
        {
            _autoSkillOrder = result;
            _autoBattleSkillService.SetSkillOrder(ParseSkillOrder(_autoSkillOrder));
            _settingsViewModel.UpdateAutoSkillOrder(_autoSkillOrder);

            if (string.IsNullOrEmpty(_autoSkillOrder))
                AddLog("战技循环顺序已恢复默认（按配队顺序）");
            else
                AddLog($"战技循环顺序已更新: {_autoSkillOrder}");
        }
    }

    private static int[]? ParseSkillOrder(string order)
    {
        if (string.IsNullOrEmpty(order)) return null;
        var result = new int[order.Length];
        for (var i = 0; i < order.Length; i++)
        {
            var ch = order[i];
            if (ch < '1' || ch > '4') return null;
            result[i] = ch - '1';
        }
        return result;
    }

    [RelayCommand]
    private async Task OpenOverlayContentSettings()
    {
        var currentSettings = _settingsViewModel.GetOverlayContentSettings() ?? new OverlayContentSettings();
        var pipelineNames = _pipelineProviders?.Select(p => p.PipelineName).ToList() ?? [];

        var result = await _dialogService.ShowOverlayContentSettingsAsync(currentSettings, pipelineNames);

        if (result != null)
        {
            _settingsViewModel.UpdateOverlayContentSettings(result);
            ApplyOverlayContentSettings(result);
            AddLog("叠加层显示设置已更新");
        }
    }

    public void ApplyOverlayContentSettings(OverlayContentSettings settings)
    {
        _overlayViewModel.ShowLog = settings.ShowLog;
        _overlayViewModel.ShowPipelineStatus = settings.ShowPipelineStatus;
        _overlayViewModel.ShowBattleAxis = settings.ShowBattleAxis;
        _overlayViewModel.BattleAxisFontSize = settings.BattleAxisFontSize;

        if (settings.ShowPipelineStatus && _pipelineProviders != null)
        {
            _overlayViewModel.SetPipelineProviders(_pipelineProviders, settings.VisiblePipelineNames);
        }
        else
        {
            _overlayViewModel.UpdateVisiblePipelines(settings.VisiblePipelineNames);
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

        _overlayViewModel.AddLog(entry);
    }

    public void Dispose()
    {
        _battleStateService.BattleEntered -= OnBattleEntered;
        _battleStateService.BattleExited -= OnBattleExited;
        _battleStateService.CameraScanStarting -= OnCameraScanStarting;
        _autoDodgeService.DodgeTriggered -= OnDodgeTriggered;

        _pipelineService.Log -= AddLog;
        _autoDodgeService.Log -= AddLog;
        _autoAttackService.Log -= AddLog;
        _autoUltimateService.Log -= AddLog;
        _autoChainSkillService.Log -= AddLog;
        _autoBattleSkillService.Log -= AddLog;
        _activeCharacterService.Log -= AddLog;
        _battleStateService.Log -= AddLog;
        _autoAxisService.Log -= AddLog;

        _battleStateService.Dispose();
        _activeCharacterService.Dispose();
        _autoUltimateService.Dispose();
        _autoChainSkillService.Dispose();
        _autoBattleSkillService.Dispose();
        _autoAxisService.Dispose();
        _autoAttackService.Dispose();
        _autoDodgeService.Dispose();
        _pipelineService.Dispose();
        _overlayViewModel.Dispose();
        _previewTimer?.Dispose();
        _previewTimer = null;
        PreviewImage?.Dispose();
        PreviewImage = null;
    }
}

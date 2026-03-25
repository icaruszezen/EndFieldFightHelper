using System;
using System.Diagnostics;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using EndFieldFightHelper.Models;
using EndFieldFightHelper.Services;
using EndFieldFightHelper.Views;
using Microsoft.Extensions.DependencyInjection;
using SukiUI.Toasts;

namespace EndFieldFightHelper.ViewModels;

public partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    [ObservableProperty]
    private ViewModelBase? _currentPage;

    private readonly IScreenshotService _screenshotService;
    private readonly YoloDetectionService _detectionService;
    private readonly OverlayService _overlayService;
    private readonly ActiveCharacterService _activeCharacterService;
    private readonly ResourceService _resourceService;
    private readonly AppUpdateService _appUpdateService;

    public HomeViewModel HomeViewModel { get; }
    public ScreenshotViewModel ScreenshotViewModel { get; }
    public SettingsViewModel SettingsViewModel { get; }
    public OverlayViewModel OverlayViewModel { get; }
    public YoloDetectionViewModel YoloDetectionViewModel { get; }
    public InputTestViewModel InputTestViewModel { get; }
    public DebugViewModel DebugViewModel { get; }
    public TaskStatusViewModel TaskStatusViewModel { get; }
    public TeamSetupViewModel TeamSetupViewModel { get; }
    public BattleAxisViewModel BattleAxisViewModel { get; }

    public MainWindowViewModel(ISukiToastManager toastManager)
    {
        var sp = App.Services;
        var screenshotService = sp.GetRequiredService<IScreenshotService>();
        var detectionService = sp.GetRequiredService<YoloDetectionService>();
        var inputService = sp.GetRequiredService<IInputService>();
        var overlayService = sp.GetRequiredService<OverlayService>();
        var resourceService = sp.GetRequiredService<ResourceService>();
        var appUpdateService = sp.GetRequiredService<AppUpdateService>();

        _screenshotService = screenshotService;
        _detectionService = detectionService;
        _overlayService = overlayService;
        _resourceService = resourceService;
        _appUpdateService = appUpdateService;

        var pipelineService = new RecognitionPipelineService(screenshotService, detectionService);
        var autoAttackService = new AutoAttackService(inputService);
        var battleStateService = new BattleStateService(pipelineService.SharedDetection, inputService);
        TeamSetupViewModel = new TeamSetupViewModel();
        _activeCharacterService = new ActiveCharacterService(
            pipelineService.SharedDetection,
            pipelineService.SharedCapture,
            index => TeamSetupViewModel.GetSlot(index),
            () => TeamSetupViewModel.TeamCount);
        Func<bool> isPausedProvider = () => !battleStateService.AreBothMarkersVisible;
        var autoUltimateService = new AutoUltimateService(
            _activeCharacterService.SharedUltimateCharge, inputService, isPausedProvider);
        var autoChainSkillService = new AutoChainSkillService(
            pipelineService.SharedDetection, inputService, isPausedProvider);
        var autoBattleSkillService = new AutoBattleSkillService(
            pipelineService.SharedDetection, inputService, () => TeamSetupViewModel.TeamCount, isPausedProvider);
        var autoAxisService = new AutoAxisService(inputService);
        var autoDodgeService = new AutoDodgeService(
            pipelineService.SharedDetection, inputService,
            () => SettingsViewModel.DodgeDelayMs,
            () => SettingsViewModel.DodgeSuppressDuringSkill,
            () => Math.Max(autoBattleSkillService.LastSkillTimestamp, autoAxisService.LastSkillTimestamp));

        BattleAxisViewModel = new BattleAxisViewModel(TeamSetupViewModel.ApplyCharacterOrder);
        SettingsViewModel = new SettingsViewModel(toastManager, overlayService, resourceService, appUpdateService);
        OverlayViewModel = new OverlayViewModel();

        HomeViewModel = new HomeViewModel(screenshotService, overlayService, pipelineService,
            autoDodgeService, autoAttackService, autoUltimateService, autoChainSkillService,
            autoBattleSkillService, _activeCharacterService, battleStateService, autoAxisService,
            new DialogService(), SettingsViewModel, OverlayViewModel, BattleAxisViewModel, TeamSetupViewModel);

        ScreenshotViewModel = new ScreenshotViewModel(screenshotService);
        YoloDetectionViewModel = new YoloDetectionViewModel(screenshotService, detectionService);
        InputTestViewModel = new InputTestViewModel(inputService);
        DebugViewModel = new DebugViewModel(
            pipelineService, _activeCharacterService, ScreenshotViewModel,
            YoloDetectionViewModel, InputTestViewModel, SettingsViewModel);
        IPipelineStatusProvider[] allProviders = [pipelineService, battleStateService, autoDodgeService, autoAttackService, autoUltimateService, autoChainSkillService, autoBattleSkillService, autoAxisService, _activeCharacterService];
        TaskStatusViewModel = new TaskStatusViewModel(allProviders);

        WireViewModels(allProviders);
        WireSettingsEvents(pipelineService, inputService);

        CurrentPage = HomeViewModel;
    }

    private void WireViewModels(IPipelineStatusProvider[] allProviders)
    {
        HomeViewModel.SetDebugViewModel(DebugViewModel);
        HomeViewModel.SetPipelineProviders(allProviders);
        SettingsViewModel.AttachOverlay(OverlayViewModel);

        var overlayContentSettings = SettingsViewModel.GetOverlayContentSettings();
        HomeViewModel.ApplyOverlayContentSettings(overlayContentSettings);
    }

    private void WireSettingsEvents(RecognitionPipelineService pipelineService, IInputService inputService)
    {
        SettingsViewModel.ResourcesDownloaded += OnResourcesDownloaded;

        inputService.Method = SettingsViewModel.SelectedInputMethod;
        SettingsViewModel.InputMethodChanged += method => inputService.Method = method;

        SettingsViewModel.CaptureMethodChanged += method =>
        {
            ScreenshotViewModel.SetCaptureMethod(method);
            YoloDetectionViewModel.SetCaptureMethod(method);
            HomeViewModel.SetCaptureMethod(method);
        };
        ScreenshotViewModel.SetCaptureMethod(SettingsViewModel.SelectedMethod);
        YoloDetectionViewModel.SetCaptureMethod(SettingsViewModel.SelectedMethod);
        HomeViewModel.SetCaptureMethod(SettingsViewModel.SelectedMethod);

        static int FpsToIntervalMs(int fps) => fps > 0 ? 1000 / fps : 0;
        pipelineService.CaptureFrameIntervalMs = FpsToIntervalMs(SettingsViewModel.CaptureFrameRateLimit);
        SettingsViewModel.CaptureFrameRateLimitChanged += fps =>
            pipelineService.CaptureFrameIntervalMs = FpsToIntervalMs(fps);

        YoloDetectionViewModel.SetYoloSettings(
            SettingsViewModel.SelectedInferenceDevice,
            (float)SettingsViewModel.YoloConfidence,
            (float)SettingsViewModel.YoloIoU);
        YoloDetectionViewModel.ModelPathChanged += path => SettingsViewModel.UpdateYoloModelPath(path);

        var isReloading = false;
        SettingsViewModel.YoloSettingsChanged += async () =>
        {
            YoloDetectionViewModel.SetYoloSettings(
                SettingsViewModel.SelectedInferenceDevice,
                (float)SettingsViewModel.YoloConfidence,
                (float)SettingsViewModel.YoloIoU);

            if (isReloading) return;

            var modelPath = !string.IsNullOrEmpty(SettingsViewModel.YoloModelPath)
                ? SettingsViewModel.YoloModelPath
                : _detectionService.ModelPath;

            if (string.IsNullOrEmpty(modelPath) || !System.IO.File.Exists(modelPath))
                return;

            isReloading = true;
            var device = SettingsViewModel.SelectedInferenceDevice;
            var conf = (float)SettingsViewModel.YoloConfidence;
            var iou = (float)SettingsViewModel.YoloIoU;

            try
            {
                YoloDetectionViewModel.StatusMessage = "正在加载模型...";
                await System.Threading.Tasks.Task.Run(() =>
                    _detectionService.LoadModel(modelPath, device, conf, iou));
                YoloDetectionViewModel.UpdateModelStatus();

                if (!device.IsCpu && _detectionService.ActiveDevice.IsCpu)
                {
                    YoloDetectionViewModel.StatusMessage =
                        $"GPU 不可用，已回退到 CPU 模式（{_detectionService.GpuFallbackReason}）";
                }
                else
                {
                    YoloDetectionViewModel.StatusMessage =
                        $"模型已加载 ({_detectionService.ActiveDevice.Name})";
                }
            }
            catch (System.Exception ex)
            {
                YoloDetectionViewModel.StatusMessage = $"模型加载失败: {ex.Message}";
            }
            finally
            {
                isReloading = false;
            }
        };
    }

    public async Task InitializeAsync()
    {
        await YoloDetectionViewModel.TryLoadSavedModelAsync(
            SettingsViewModel.YoloModelPath,
            SettingsViewModel.SelectedInferenceDevice,
            (float)SettingsViewModel.YoloConfidence,
            (float)SettingsViewModel.YoloIoU);
    }

    private async void OnResourcesDownloaded()
    {
        try
        {
            await TeamSetupViewModel.ReloadCharactersAsync();
            await BattleAxisViewModel.ReloadCharacterMapAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"资源重载失败: {ex.Message}");
        }
    }

    public void Dispose()
    {
        SettingsViewModel.ResourcesDownloaded -= OnResourcesDownloaded;
        SafeDispose(OverlayViewModel);
        SafeDispose(HomeViewModel);
        SafeDispose(BattleAxisViewModel);
        SafeDispose(DebugViewModel);
        SafeDispose(TaskStatusViewModel);
        SafeDispose(TeamSetupViewModel);
        SafeDispose(ScreenshotViewModel);
        SafeDispose(SettingsViewModel);
        SafeDispose(_detectionService);
        SafeDispose(_overlayService);
        SafeDispose(_screenshotService as IDisposable);
        SafeDispose(_resourceService);
        SafeDispose(_appUpdateService);
    }

    private static void SafeDispose(IDisposable? disposable)
    {
        if (disposable == null) return;
        try { disposable.Dispose(); }
        catch (Exception ex) { Debug.WriteLine($"Dispose failed for {disposable.GetType().Name}: {ex.Message}"); }
    }
}

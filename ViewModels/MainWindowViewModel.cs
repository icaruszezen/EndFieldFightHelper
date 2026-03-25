using System;
using System.Diagnostics;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using EndFieldFightHelper.Models;
using EndFieldFightHelper.Services;
using SukiUI.Toasts;

namespace EndFieldFightHelper.ViewModels;

public partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    [ObservableProperty]
    private ViewModelBase? _currentPage;

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
        var screenshotService = new ScreenshotService();
        var detectionService = new YoloDetectionService();
        var inputService = new InputService();
        var overlayService = new OverlayService();
        var resourceService = new ResourceService();
        var appUpdateService = new AppUpdateService();

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
            () => SettingsViewModel!.DodgeDelayMs,
            () => SettingsViewModel!.DodgeSuppressDuringSkill,
            () => Math.Max(autoBattleSkillService.LastSkillTimestamp, autoAxisService.LastSkillTimestamp));

        BattleAxisViewModel = new BattleAxisViewModel(TeamSetupViewModel.ApplyCharacterOrder);

        HomeViewModel = new HomeViewModel(screenshotService, overlayService, pipelineService,
            autoDodgeService, autoAttackService, autoUltimateService, autoChainSkillService,
            autoBattleSkillService, _activeCharacterService, battleStateService, autoAxisService);
        SettingsViewModel = new SettingsViewModel(toastManager, overlayService, resourceService, appUpdateService);
        ScreenshotViewModel = new ScreenshotViewModel(screenshotService);
        OverlayViewModel = new OverlayViewModel();
        YoloDetectionViewModel = new YoloDetectionViewModel(screenshotService, detectionService);
        InputTestViewModel = new InputTestViewModel(inputService);
        DebugViewModel = new DebugViewModel(
            pipelineService, _activeCharacterService, ScreenshotViewModel,
            YoloDetectionViewModel, InputTestViewModel, SettingsViewModel);
        IPipelineStatusProvider[] allProviders = [pipelineService, battleStateService, autoDodgeService, autoAttackService, autoUltimateService, autoChainSkillService, autoBattleSkillService, autoAxisService, _activeCharacterService];
        TaskStatusViewModel = new TaskStatusViewModel(allProviders);

        HomeViewModel.SetSettingsViewModel(SettingsViewModel);
        HomeViewModel.SetOverlayViewModel(OverlayViewModel);
        HomeViewModel.SetDebugViewModel(DebugViewModel);
        HomeViewModel.SetBattleAxisViewModel(BattleAxisViewModel);
        HomeViewModel.SetTeamSetupViewModel(TeamSetupViewModel);
        HomeViewModel.SetPipelineProviders(allProviders);
        SettingsViewModel.AttachOverlay(OverlayViewModel);

        var overlayContentSettings = SettingsViewModel.GetOverlayContentSettings();
        HomeViewModel.ApplyOverlayContentSettings(overlayContentSettings);

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
                : detectionService.ModelPath;

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
                    detectionService.LoadModel(modelPath, device, conf, iou));
                YoloDetectionViewModel.UpdateModelStatus();

                if (!device.IsCpu && detectionService.ActiveDevice.IsCpu)
                {
                    YoloDetectionViewModel.StatusMessage =
                        $"GPU 不可用，已回退到 CPU 模式（{detectionService.GpuFallbackReason}）";
                }
                else
                {
                    YoloDetectionViewModel.StatusMessage =
                        $"模型已加载 ({detectionService.ActiveDevice.Name})";
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

        CurrentPage = HomeViewModel;
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

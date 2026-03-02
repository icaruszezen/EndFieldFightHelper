using System;
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

        _detectionService = detectionService;
        _overlayService = overlayService;

        var pipelineService = new RecognitionPipelineService(screenshotService, detectionService);
        var autoDodgeService = new AutoDodgeService(pipelineService.SharedDetection, inputService);
        var autoAttackService = new AutoAttackService(inputService);
        var battleStateService = new BattleStateService(pipelineService.SharedDetection);
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

        BattleAxisViewModel = new BattleAxisViewModel();

        HomeViewModel = new HomeViewModel(screenshotService, overlayService, pipelineService,
            autoDodgeService, autoAttackService, autoUltimateService, autoChainSkillService,
            autoBattleSkillService, _activeCharacterService, battleStateService);
        SettingsViewModel = new SettingsViewModel(toastManager, overlayService);
        ScreenshotViewModel = new ScreenshotViewModel(screenshotService);
        OverlayViewModel = new OverlayViewModel();
        YoloDetectionViewModel = new YoloDetectionViewModel(screenshotService, detectionService);
        InputTestViewModel = new InputTestViewModel(inputService);
        DebugViewModel = new DebugViewModel(
            pipelineService, _activeCharacterService, ScreenshotViewModel,
            YoloDetectionViewModel, InputTestViewModel, SettingsViewModel);
        TaskStatusViewModel = new TaskStatusViewModel([pipelineService, battleStateService, autoDodgeService, autoAttackService, autoUltimateService, autoChainSkillService, autoBattleSkillService, _activeCharacterService]);

        HomeViewModel.SetSettingsViewModel(SettingsViewModel);
        HomeViewModel.SetOverlayViewModel(OverlayViewModel);
        HomeViewModel.SetDebugViewModel(DebugViewModel);
        SettingsViewModel.AttachOverlay(OverlayViewModel);

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

        YoloDetectionViewModel.TryLoadSavedModel(
            SettingsViewModel.YoloModelPath,
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

    public void Dispose()
    {
        HomeViewModel.Dispose();
        BattleAxisViewModel.Dispose();
        DebugViewModel.Dispose();
        TaskStatusViewModel.Dispose();
        TeamSetupViewModel.Dispose();
        ScreenshotViewModel.Dispose();
        SettingsViewModel.Dispose();
        _detectionService.Dispose();
        _overlayService.Dispose();
    }
}

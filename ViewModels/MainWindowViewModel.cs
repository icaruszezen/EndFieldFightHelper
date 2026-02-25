using CommunityToolkit.Mvvm.ComponentModel;
using EndFieldFightHelper.Services;
using SukiUI.Toasts;

namespace EndFieldFightHelper.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    private ViewModelBase? _currentPage;

    public HomeViewModel HomeViewModel { get; }
    public ScreenshotViewModel ScreenshotViewModel { get; }
    public SettingsViewModel SettingsViewModel { get; }
    public OverlayViewModel OverlayViewModel { get; }
    public YoloDetectionViewModel YoloDetectionViewModel { get; }

    public MainWindowViewModel(ISukiToastManager toastManager)
    {
        var screenshotService = new ScreenshotService();
        var detectionService = new YoloDetectionService();

        HomeViewModel = new HomeViewModel();
        SettingsViewModel = new SettingsViewModel(toastManager);
        ScreenshotViewModel = new ScreenshotViewModel(screenshotService);
        OverlayViewModel = new OverlayViewModel();
        YoloDetectionViewModel = new YoloDetectionViewModel(screenshotService, detectionService);

        SettingsViewModel.AttachOverlay(OverlayViewModel);

        SettingsViewModel.CaptureMethodChanged += method =>
        {
            ScreenshotViewModel.SetCaptureMethod(method);
            YoloDetectionViewModel.SetCaptureMethod(method);
        };
        ScreenshotViewModel.SetCaptureMethod(SettingsViewModel.SelectedMethod);
        YoloDetectionViewModel.SetCaptureMethod(SettingsViewModel.SelectedMethod);

        YoloDetectionViewModel.TryLoadSavedModel(
            SettingsViewModel.YoloModelPath,
            SettingsViewModel.UseGpu,
            (float)SettingsViewModel.YoloConfidence,
            (float)SettingsViewModel.YoloIoU);
        YoloDetectionViewModel.ModelPathChanged += path => SettingsViewModel.UpdateYoloModelPath(path);

        SettingsViewModel.YoloSettingsChanged += async () =>
        {
            YoloDetectionViewModel.SetYoloSettings(
                SettingsViewModel.UseGpu,
                (float)SettingsViewModel.YoloConfidence,
                (float)SettingsViewModel.YoloIoU);

            if (detectionService.IsModelLoaded && !string.IsNullOrEmpty(detectionService.ModelPath))
            {
                var modelPath = detectionService.ModelPath;
                var useGpu = SettingsViewModel.UseGpu;
                var conf = (float)SettingsViewModel.YoloConfidence;
                var iou = (float)SettingsViewModel.YoloIoU;

                try
                {
                    YoloDetectionViewModel.StatusMessage = "正在重新加载模型...";
                    await System.Threading.Tasks.Task.Run(() =>
                        detectionService.LoadModel(modelPath, useGpu, conf, iou));
                    YoloDetectionViewModel.UpdateModelStatus();

                    if (useGpu && !detectionService.IsUsingGpu)
                    {
                        SettingsViewModel.UseGpu = false;
                        YoloDetectionViewModel.StatusMessage =
                            $"GPU 不可用，已回退到 CPU 模式（{detectionService.GpuFallbackReason}）";
                    }
                    else
                    {
                        YoloDetectionViewModel.StatusMessage =
                            $"模型已重新加载 ({(detectionService.IsUsingGpu ? "GPU" : "CPU")})";
                    }
                }
                catch (System.Exception ex)
                {
                    YoloDetectionViewModel.IsModelLoaded = false;
                    YoloDetectionViewModel.StatusMessage = $"模型重载失败: {ex.Message}";
                }
            }
        };

        CurrentPage = HomeViewModel;
    }
}

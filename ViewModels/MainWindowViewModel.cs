using CommunityToolkit.Mvvm.ComponentModel;
using EndFieldFightHelper.Models;
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
            if (!detectionService.IsModelLoaded || string.IsNullOrEmpty(detectionService.ModelPath))
                return;

            isReloading = true;
            var modelPath = detectionService.ModelPath;
            var device = SettingsViewModel.SelectedInferenceDevice;
            var conf = (float)SettingsViewModel.YoloConfidence;
            var iou = (float)SettingsViewModel.YoloIoU;

            try
            {
                YoloDetectionViewModel.StatusMessage = "正在重新加载模型...";
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
                        $"模型已重新加载 ({detectionService.ActiveDevice.Name})";
                }
            }
            catch (System.Exception ex)
            {
                YoloDetectionViewModel.StatusMessage = $"模型重载失败: {ex.Message}";
            }
            finally
            {
                isReloading = false;
            }
        };

        CurrentPage = HomeViewModel;
    }
}

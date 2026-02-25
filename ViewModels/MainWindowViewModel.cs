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

        YoloDetectionViewModel.TryLoadSavedModel(SettingsViewModel.YoloModelPath);
        YoloDetectionViewModel.ModelPathChanged += path => SettingsViewModel.UpdateYoloModelPath(path);

        CurrentPage = HomeViewModel;
    }
}

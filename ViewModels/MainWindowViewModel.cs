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

    public MainWindowViewModel(ISukiToastManager toastManager)
    {
        var screenshotService = new ScreenshotService();

        HomeViewModel = new HomeViewModel();
        SettingsViewModel = new SettingsViewModel(toastManager);
        ScreenshotViewModel = new ScreenshotViewModel(screenshotService);
        OverlayViewModel = new OverlayViewModel();

        SettingsViewModel.AttachOverlay(OverlayViewModel);

        SettingsViewModel.CaptureMethodChanged += method => ScreenshotViewModel.SetCaptureMethod(method);
        ScreenshotViewModel.SetCaptureMethod(SettingsViewModel.SelectedMethod);

        CurrentPage = HomeViewModel;
    }
}

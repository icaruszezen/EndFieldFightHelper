using CommunityToolkit.Mvvm.ComponentModel;

namespace EndFieldFightHelper.ViewModels;

public partial class OverlayViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _overlayText = "自定义内容示例";

    public OverlayViewModel()
    {
    }

    public void UpdateText(string text)
    {
        OverlayText = text;
    }
}

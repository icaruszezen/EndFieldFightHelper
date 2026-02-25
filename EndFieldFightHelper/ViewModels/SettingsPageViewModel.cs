using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;
using SukiUI;
using SukiUI.Models;

namespace EndFieldFightHelper.ViewModels;

public partial class SettingsPageViewModel : PageBase
{
    private readonly SukiTheme _theme;

    public IAvaloniaReadOnlyList<SukiColorTheme> ColorThemes { get; }

    public SettingsPageViewModel() : base("设置", MaterialIconKind.Cog, 1)
    {
        _theme = SukiTheme.GetInstance();
        ColorThemes = _theme.ColorThemes;
    }

    [RelayCommand]
    private void ChangeColorTheme(SukiColorTheme theme)
    {
        _theme.ChangeColorTheme(theme);
    }
}

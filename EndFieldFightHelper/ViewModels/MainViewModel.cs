using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Linq;

namespace EndFieldFightHelper.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public IAvaloniaReadOnlyList<PageBase> Pages { get; }

    [ObservableProperty] private PageBase? _activePage;

    public MainViewModel(
        HomePageViewModel homePage,
        SettingsPageViewModel settingsPage)
    {
        var ordered = new PageBase[] { homePage, settingsPage }
            .OrderBy(p => p.Index)
            .ToList();
        Pages = new AvaloniaList<PageBase>(ordered);
        ActivePage = ordered[0];
    }
}

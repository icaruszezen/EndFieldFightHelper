using CommunityToolkit.Mvvm.ComponentModel;
using EndFieldFightHelper.Models;

namespace EndFieldFightHelper.ViewModels;

public partial class TeamSlotViewModel : ViewModelBase
{
    public int Index { get; init; }

    public string Label => $"{Index}号位";

    [ObservableProperty]
    private CharacterInfo? _character;

    [ObservableProperty]
    private bool _isActive;
}

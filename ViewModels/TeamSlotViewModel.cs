using Avalonia.Media.Imaging;
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

    public bool HasCharacter => Character != null;
    public Bitmap? CharacterAvatarImage => Character?.AvatarImage;
    public string CharacterName => Character?.Name ?? "";

    partial void OnCharacterChanged(CharacterInfo? value)
    {
        OnPropertyChanged(nameof(HasCharacter));
        OnPropertyChanged(nameof(CharacterAvatarImage));
        OnPropertyChanged(nameof(CharacterName));
    }
}

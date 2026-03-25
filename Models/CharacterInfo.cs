using System;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace EndFieldFightHelper.Models;

public partial class CharacterInfo : ObservableObject, IDisposable
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public int Rarity { get; init; }
    public string Element { get; init; } = "";
    public string AvatarRelativePath { get; init; } = "";

    [ObservableProperty]
    private Bitmap? _avatarImage;

    [ObservableProperty]
    private bool _isSelected;

    public void Dispose()
    {
        AvatarImage?.Dispose();
        AvatarImage = null;
    }
}

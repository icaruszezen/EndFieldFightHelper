using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EndFieldFightHelper.Models;

namespace EndFieldFightHelper.ViewModels;

public partial class TeamSetupViewModel : ViewModelBase
{
    private const int MaxSlots = 4;

    public ObservableCollection<CharacterInfo> AllCharacters { get; } = [];

    [ObservableProperty]
    private CharacterInfo? _teamSlot1;

    [ObservableProperty]
    private CharacterInfo? _teamSlot2;

    [ObservableProperty]
    private CharacterInfo? _teamSlot3;

    [ObservableProperty]
    private CharacterInfo? _teamSlot4;

    [ObservableProperty]
    private int _selectedSlotIndex = 1;

    [ObservableProperty]
    private bool _isSlot1Active = true;

    [ObservableProperty]
    private bool _isSlot2Active;

    [ObservableProperty]
    private bool _isSlot3Active;

    [ObservableProperty]
    private bool _isSlot4Active;

    [ObservableProperty]
    private int _teamCount;

    public TeamSetupViewModel()
    {
        LoadCharacters();
    }

    partial void OnSelectedSlotIndexChanged(int value)
    {
        IsSlot1Active = value == 1;
        IsSlot2Active = value == 2;
        IsSlot3Active = value == 3;
        IsSlot4Active = value == 4;
    }

    private void LoadCharacters()
    {
        var basePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "public");
        var gamedataPath = Path.Combine(basePath, "gamedata.json");

        if (!File.Exists(gamedataPath))
            return;

        try
        {
            var json = File.ReadAllText(gamedataPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("characterRoster", out var roster))
                return;

            foreach (var entry in roster.EnumerateArray())
            {
                var id = entry.GetProperty("id").GetString() ?? "";
                var name = entry.GetProperty("name").GetString() ?? "";
                var rarity = entry.GetProperty("rarity").GetInt32();
                var element = entry.GetProperty("element").GetString() ?? "";
                var avatarRel = entry.GetProperty("avatar").GetString() ?? "";

                var avatarFullPath = Path.Combine(basePath,
                    avatarRel.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                Bitmap? avatarBitmap = null;
                if (File.Exists(avatarFullPath))
                {
                    try { avatarBitmap = new Bitmap(avatarFullPath); }
                    catch { /* skip broken images */ }
                }

                AllCharacters.Add(new CharacterInfo
                {
                    Id = id,
                    Name = name,
                    Rarity = rarity,
                    Element = element,
                    AvatarRelativePath = avatarRel,
                    AvatarImage = avatarBitmap,
                });
            }
        }
        catch
        {
            // ignore parse errors at startup
        }
    }

    public CharacterInfo? GetSlot(int index) => index switch
    {
        1 => TeamSlot1,
        2 => TeamSlot2,
        3 => TeamSlot3,
        4 => TeamSlot4,
        _ => null,
    };

    private void SetSlot(int index, CharacterInfo? character)
    {
        switch (index)
        {
            case 1: TeamSlot1 = character; break;
            case 2: TeamSlot2 = character; break;
            case 3: TeamSlot3 = character; break;
            case 4: TeamSlot4 = character; break;
        }
    }

    private IEnumerable<CharacterInfo?> AllSlots =>
        [TeamSlot1, TeamSlot2, TeamSlot3, TeamSlot4];

    private bool IsCharacterInTeam(CharacterInfo character) =>
        AllSlots.Any(s => s != null && s.Id == character.Id);

    [RelayCommand]
    private void SelectSlot(object? param)
    {
        if (int.TryParse(param?.ToString(), out var index) && index is >= 1 and <= MaxSlots)
            SelectedSlotIndex = index;
    }

    [RelayCommand]
    private void SelectCharacter(CharacterInfo? character)
    {
        if (character == null || IsCharacterInTeam(character))
            return;

        var prev = GetSlot(SelectedSlotIndex);
        if (prev != null)
            prev.IsSelected = false;

        character.IsSelected = true;
        SetSlot(SelectedSlotIndex, character);
        RefreshSelectionFlags();
        UpdateTeamCount();
        AutoAdvanceSlot();
    }

    [RelayCommand]
    private void ClearSlot(object? param)
    {
        if (!int.TryParse(param?.ToString(), out var index) || index is < 1 or > MaxSlots)
            return;

        var character = GetSlot(index);
        if (character != null)
            character.IsSelected = false;

        SetSlot(index, null);
        SelectedSlotIndex = index;
        RefreshSelectionFlags();
        UpdateTeamCount();
    }

    [RelayCommand]
    private void ClearAllSlots()
    {
        for (var i = 1; i <= MaxSlots; i++)
        {
            var c = GetSlot(i);
            if (c != null) c.IsSelected = false;
            SetSlot(i, null);
        }
        SelectedSlotIndex = 1;
        RefreshSelectionFlags();
        UpdateTeamCount();
    }

    private void RefreshSelectionFlags()
    {
        var selectedIds = AllSlots.Where(s => s != null).Select(s => s!.Id).ToHashSet();
        foreach (var c in AllCharacters)
            c.IsSelected = selectedIds.Contains(c.Id);
    }

    private void UpdateTeamCount()
    {
        TeamCount = AllSlots.Count(s => s != null);
    }

    private void AutoAdvanceSlot()
    {
        for (var i = 1; i <= MaxSlots; i++)
        {
            if (GetSlot(i) == null)
            {
                SelectedSlotIndex = i;
                return;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EndFieldFightHelper.Models;

namespace EndFieldFightHelper.ViewModels;

public partial class TeamSetupViewModel : ViewModelBase, IDisposable
{
    private const int MaxSlots = 4;

    public ObservableCollection<CharacterInfo> AllCharacters { get; } = [];

    public ObservableCollection<TeamSlotViewModel> TeamSlots { get; } =
    [
        new() { Index = 1, IsActive = true },
        new() { Index = 2 },
        new() { Index = 3 },
        new() { Index = 4 },
    ];

    [ObservableProperty]
    private int _selectedSlotIndex = 1;

    [ObservableProperty]
    private int _teamCount;

    public TeamSetupViewModel()
    {
        _ = LoadCharactersAsync();
    }

    partial void OnSelectedSlotIndexChanged(int value)
    {
        foreach (var slot in TeamSlots)
            slot.IsActive = slot.Index == value;
    }

    private async Task LoadCharactersAsync()
    {
        var basePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "public");
        var gamedataPath = Path.Combine(basePath, "gamedata.json");

        if (!File.Exists(gamedataPath))
            return;

        try
        {
            var characters = await Task.Run(() =>
            {
                var json = File.ReadAllText(gamedataPath);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!root.TryGetProperty("characterRoster", out var roster))
                    return new List<CharacterInfo>();

                var list = new List<CharacterInfo>();
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
                        catch (Exception) { }
                    }

                    list.Add(new CharacterInfo
                    {
                        Id = id,
                        Name = name,
                        Rarity = rarity,
                        Element = element,
                        AvatarRelativePath = avatarRel,
                        AvatarImage = avatarBitmap,
                    });
                }

                return list;
            });

            foreach (var c in characters)
                AllCharacters.Add(c);
        }
        catch (Exception)
        {
        }
    }

    public CharacterInfo? GetSlot(int index) =>
        index is >= 1 and <= MaxSlots ? TeamSlots[index - 1].Character : null;

    private void SetSlot(int index, CharacterInfo? character)
    {
        if (index is >= 1 and <= MaxSlots)
            TeamSlots[index - 1].Character = character;
    }

    private IEnumerable<CharacterInfo?> AllSlots => TeamSlots.Select(s => s.Character);

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

    public void Dispose()
    {
        foreach (var c in AllCharacters)
            c.AvatarImage?.Dispose();
        AllCharacters.Clear();
    }
}

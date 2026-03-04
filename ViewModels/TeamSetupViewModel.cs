using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EndFieldFightHelper.Models;

namespace EndFieldFightHelper.ViewModels;

public partial class TeamSetupViewModel : ViewModelBase, IDisposable
{
    private const int MaxSlots = 4;

    private readonly string _presetsPath;
    private CancellationTokenSource? _saveCts;
    private bool _isApplyingPreset;

    public ObservableCollection<CharacterInfo> AllCharacters { get; } = [];

    public ObservableCollection<TeamSlotViewModel> TeamSlots { get; } =
    [
        new() { Index = 1, IsActive = true },
        new() { Index = 2 },
        new() { Index = 3 },
        new() { Index = 4 },
    ];

    public ObservableCollection<TeamPreset> SavedPresets { get; } = [];

    [ObservableProperty]
    private TeamPreset? _selectedPreset;

    [ObservableProperty]
    private bool _isRenaming;

    [ObservableProperty]
    private string _renamingText = "";

    [ObservableProperty]
    private int _selectedSlotIndex = 1;

    [ObservableProperty]
    private int _teamCount;

    public bool HasSelectedPreset => SelectedPreset != null;

    public bool HasGaps
    {
        get
        {
            var foundEmpty = false;
            for (var i = 1; i <= MaxSlots; i++)
            {
                if (GetSlot(i) == null)
                    foundEmpty = true;
                else if (foundEmpty)
                    return true;
            }
            return false;
        }
    }

    public TeamSetupViewModel()
    {
        _presetsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EndFieldFightHelper",
            "team_presets.json");

        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        await LoadCharactersAsync();
        LoadPresets();
    }

    public async Task ReloadCharactersAsync()
    {
        foreach (var c in AllCharacters)
            c.AvatarImage?.Dispose();
        AllCharacters.Clear();

        await LoadCharactersAsync();

        var savedSlotIds = CaptureCurrentSlotIds();
        for (var i = 1; i <= MaxSlots; i++)
            SetSlot(i, null);

        for (var i = 0; i < MaxSlots && i < savedSlotIds.Count; i++)
        {
            var charId = savedSlotIds[i];
            if (charId == null) continue;
            var character = AllCharacters.FirstOrDefault(c => c.Id == charId);
            if (character != null)
            {
                character.IsSelected = true;
                SetSlot(i + 1, character);
            }
        }

        RefreshSelectionFlags();
        UpdateTeamCount();
    }

    partial void OnSelectedSlotIndexChanged(int value)
    {
        foreach (var slot in TeamSlots)
            slot.IsActive = slot.Index == value;
    }

    partial void OnSelectedPresetChanged(TeamPreset? value)
    {
        OnPropertyChanged(nameof(HasSelectedPreset));
        if (value != null && !_isApplyingPreset)
            ApplyPreset(value);
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

    private void LoadPresets()
    {
        try
        {
            if (!File.Exists(_presetsPath))
                return;

            var json = File.ReadAllText(_presetsPath);
            var data = JsonSerializer.Deserialize<TeamPresetsData>(json);
            if (data == null)
                return;

            foreach (var preset in data.Presets)
                SavedPresets.Add(preset);

            if (data.CurrentSlotCharacterIds is { Count: > 0 })
            {
                ApplySlotIds(data.CurrentSlotCharacterIds);
                SyncPresetSelection();
            }
            else if (data.ActivePresetId != null)
            {
                var active = SavedPresets.FirstOrDefault(p => p.Id == data.ActivePresetId);
                if (active != null)
                {
                    _isApplyingPreset = true;
                    SelectedPreset = active;
                    _isApplyingPreset = false;
                    ApplyPreset(active);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load team presets: {ex}");
        }
    }

    private void ApplySlotIds(List<string?> slotIds)
    {
        _isApplyingPreset = true;
        try
        {
            for (var i = 1; i <= MaxSlots; i++)
            {
                var c = GetSlot(i);
                if (c != null) c.IsSelected = false;
                SetSlot(i, null);
            }

            for (var i = 0; i < MaxSlots && i < slotIds.Count; i++)
            {
                var charId = slotIds[i];
                if (charId == null) continue;

                var character = AllCharacters.FirstOrDefault(c => c.Id == charId);
                if (character != null)
                {
                    character.IsSelected = true;
                    SetSlot(i + 1, character);
                }
            }

            SelectedSlotIndex = 1;
            RefreshSelectionFlags();
            UpdateTeamCount();
        }
        finally
        {
            _isApplyingPreset = false;
        }
    }

    private void ApplyPreset(TeamPreset preset)
    {
        ApplySlotIds(preset.SlotCharacterIds);
        ScheduleSave();
    }

    private List<string?> CaptureCurrentSlotIds()
    {
        var ids = new List<string?>(MaxSlots);
        for (var i = 1; i <= MaxSlots; i++)
            ids.Add(GetSlot(i)?.Id);
        return ids;
    }

    private static bool SlotIdsMatch(List<string?> a, List<string?> b)
    {
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
        {
            if (a[i] != b[i]) return false;
        }
        return true;
    }

    private void SyncPresetSelection()
    {
        var currentIds = CaptureCurrentSlotIds();

        foreach (var preset in SavedPresets)
        {
            if (!SlotIdsMatch(currentIds, preset.SlotCharacterIds)) continue;

            if (SelectedPreset != preset)
            {
                _isApplyingPreset = true;
                SelectedPreset = preset;
                _isApplyingPreset = false;
            }
            return;
        }

        if (SelectedPreset != null)
        {
            _isApplyingPreset = true;
            SelectedPreset = null;
            _isApplyingPreset = false;
        }
    }

    private void OnTeamChanged()
    {
        OnPropertyChanged(nameof(HasGaps));
        SyncPresetSelection();
        ScheduleSave();
    }

    public void ApplyCharacterOrder(List<string?> characterIds)
    {
        ApplySlotIds(characterIds);
        OnTeamChanged();
    }

    public void CompactTeam()
    {
        var characters = new List<CharacterInfo>();
        for (var i = 1; i <= MaxSlots; i++)
        {
            var c = GetSlot(i);
            if (c != null)
                characters.Add(c);
        }

        for (var i = 1; i <= MaxSlots; i++)
        {
            var c = GetSlot(i);
            if (c != null) c.IsSelected = false;
            SetSlot(i, null);
        }

        for (var i = 0; i < characters.Count; i++)
        {
            characters[i].IsSelected = true;
            SetSlot(i + 1, characters[i]);
        }

        SelectedSlotIndex = 1;
        RefreshSelectionFlags();
        UpdateTeamCount();
        OnTeamChanged();
    }

    [RelayCommand]
    private void SavePreset()
    {
        if (SelectedPreset != null)
        {
            SelectedPreset.SlotCharacterIds = CaptureCurrentSlotIds();
            ScheduleSave();
            return;
        }

        SaveAsNewPreset();
    }

    [RelayCommand]
    private void SaveAsNewPreset()
    {
        var preset = new TeamPreset
        {
            Name = $"配队 {SavedPresets.Count + 1}",
            SlotCharacterIds = CaptureCurrentSlotIds(),
        };
        SavedPresets.Add(preset);

        _isApplyingPreset = true;
        SelectedPreset = preset;
        _isApplyingPreset = false;

        ScheduleSave();
    }

    [RelayCommand]
    private void BeginRename()
    {
        if (SelectedPreset == null) return;
        RenamingText = SelectedPreset.Name;
        IsRenaming = true;
    }

    [RelayCommand]
    private void ConfirmRename()
    {
        if (SelectedPreset == null || !IsRenaming) return;

        var trimmed = RenamingText.Trim();
        if (!string.IsNullOrEmpty(trimmed))
        {
            SelectedPreset.Name = trimmed;
            ScheduleSave();
        }

        IsRenaming = false;
    }

    [RelayCommand]
    private void CancelRename()
    {
        IsRenaming = false;
    }

    [RelayCommand]
    private void DeletePreset()
    {
        if (SelectedPreset == null) return;

        var toRemove = SelectedPreset;
        SavedPresets.Remove(toRemove);

        _isApplyingPreset = true;
        SelectedPreset = null;
        _isApplyingPreset = false;

        SyncPresetSelection();
        ScheduleSave();
    }

    private void ScheduleSave()
    {
        _saveCts?.Cancel();
        _saveCts = new CancellationTokenSource();
        var token = _saveCts.Token;
        _ = SavePresetsDebouncedAsync(token);
    }

    private async Task SavePresetsDebouncedAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(300, token);
            if (token.IsCancellationRequested) return;

            var dir = Path.GetDirectoryName(_presetsPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var data = new TeamPresetsData
            {
                ActivePresetId = SelectedPreset?.Id,
                CurrentSlotCharacterIds = CaptureCurrentSlotIds(),
                Presets = SavedPresets.ToList(),
            };

            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_presetsPath, json, token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save team presets: {ex}");
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
        OnTeamChanged();
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
        OnTeamChanged();
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
        OnTeamChanged();
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
        _saveCts?.Cancel();
        _saveCts?.Dispose();
        foreach (var c in AllCharacters)
            c.AvatarImage?.Dispose();
        AllCharacters.Clear();
    }
}

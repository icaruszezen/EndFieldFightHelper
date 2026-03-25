using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EndFieldFightHelper.Models.BattleAxis;

namespace EndFieldFightHelper.ViewModels;

public partial class BattleAxisViewModel : ViewModelBase, IDisposable
{
    private const double DefaultPixelsPerSecond = 30;
    private const double TotalDuration = 120;
    private const double TrackHeight = 40;
    private const double LabelColumnWidth = 120;

    private static readonly HashSet<string> RelevantActionTypes = ["attack", "skill", "link", "ultimate"];

    internal static readonly Dictionary<string, string> ActionTypeLabels = new()
    {
        ["attack"] = "重击",
        ["skill"] = "战技",
        ["link"] = "连携技",
        ["ultimate"] = "终结技",
    };

    private readonly string _presetsPath;
    private CancellationTokenSource? _saveCts;
    private string? _currentJsonContent;

    private Dictionary<string, (string Name, int Rarity, string Element, Bitmap? Avatar)> _characterMap = new();
    private EndaxisProject? _project;

    [ObservableProperty]
    private double _pixelsPerSecond = DefaultPixelsPerSecond;

    [ObservableProperty]
    private double _timelineWidth = TotalDuration * DefaultPixelsPerSecond;

    [ObservableProperty]
    private ObservableCollection<ScenarioItem> _scenarios = [];

    [ObservableProperty]
    private ScenarioItem? _selectedScenario;

    [ObservableProperty]
    private ObservableCollection<TrackDisplayModel> _tracks = [];

    [ObservableProperty]
    private ObservableCollection<ActiveCharacterSegment> _activeSegments = [];

    [ObservableProperty]
    private ObservableCollection<TimelineTick> _timelineTicks = [];

    [ObservableProperty]
    private bool _hasData;

    [ObservableProperty]
    private string _statusText = "请导入 Endaxis JSON 文件";

    [ObservableProperty]
    private bool _hasSwitchData;

    public ObservableCollection<BattleAxisPreset> SavedPresets { get; } = [];

    public bool HasSavedPresets => SavedPresets.Count > 0;

    [ObservableProperty]
    private BattleAxisPreset? _selectedSavedPreset;

    [ObservableProperty]
    private bool _isRenaming;

    [ObservableProperty]
    private string _renamingText = "";

    public Action<List<string?>>? ApplyToTeamSetupAction { get; }

    public bool CanApplyToTeamSetup => HasData && Tracks.Count > 0 && ApplyToTeamSetupAction != null;

    public bool HasSelectedPreset => SelectedSavedPreset != null;

    partial void OnSelectedSavedPresetChanged(BattleAxisPreset? value)
    {
        OnPropertyChanged(nameof(HasSelectedPreset));
    }

    public BattleAxisViewModel(Action<List<string?>>? applyToTeamSetupAction = null)
    {
        ApplyToTeamSetupAction = applyToTeamSetupAction;
        _presetsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EndFieldFightHelper",
            "battleaxis_presets.json");

        SavedPresets.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasSavedPresets));

        _ = LoadCharacterMapAsync();
        LoadSavedPresets();
        RebuildTicks();
    }

    partial void OnTracksChanged(ObservableCollection<TrackDisplayModel> value)
    {
        OnPropertyChanged(nameof(CanApplyToTeamSetup));
    }

    partial void OnSelectedScenarioChanged(ScenarioItem? value)
    {
        if (value != null)
            ApplyScenario(value.Scenario);
    }

    partial void OnPixelsPerSecondChanged(double value)
    {
        TimelineWidth = TotalDuration * value;
        RebuildTicks();
        if (_selectedScenario != null)
            ApplyScenario(_selectedScenario.Scenario);
    }

    public async Task ReloadCharacterMapAsync()
    {
        foreach (var (_, _, _, avatar) in _characterMap.Values)
            avatar?.Dispose();
        _characterMap.Clear();

        await LoadCharacterMapAsync();
    }

    private async Task LoadCharacterMapAsync()
    {
        var basePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "public");
        var gamedataPath = Path.Combine(basePath, "gamedata.json");

        if (!File.Exists(gamedataPath))
            return;

        try
        {
            _characterMap = await Task.Run(() =>
            {
                var json = File.ReadAllText(gamedataPath);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!root.TryGetProperty("characterRoster", out var roster))
                    return new Dictionary<string, (string, int, string, Bitmap?)>();

                var map = new Dictionary<string, (string, int, string, Bitmap?)>();
                foreach (var entry in roster.EnumerateArray())
                {
                    var id = entry.GetProperty("id").GetString() ?? "";
                    var name = entry.GetProperty("name").GetString() ?? "";
                    var rarity = entry.GetProperty("rarity").GetInt32();
                    var element = entry.GetProperty("element").GetString() ?? "";
                    var avatarRel = entry.GetProperty("avatar").GetString() ?? "";

                    Bitmap? avatarBitmap = null;
                    if (!string.IsNullOrEmpty(avatarRel))
                    {
                        var avatarPath = Path.Combine(basePath,
                            avatarRel.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                        if (File.Exists(avatarPath))
                        {
                            try { avatarBitmap = new Bitmap(avatarPath); }
                            catch { /* ignore */ }
                        }
                    }

                    map[id] = (name, rarity, element, avatarBitmap);
                }

                return map;
            });
        }
        catch
        {
            /* ignore */
        }
    }

    public void LoadFromJson(string jsonContent)
    {
        try
        {
            var project = JsonSerializer.Deserialize<EndaxisProject>(jsonContent);
            if (project == null || project.ScenarioList.Count == 0)
            {
                StatusText = "JSON 文件中没有找到有效的方案数据";
                return;
            }

            _currentJsonContent = jsonContent;
            _project = project;
            Scenarios.Clear();

            foreach (var scenario in project.ScenarioList)
            {
                var displayName = !string.IsNullOrEmpty(scenario.Name) ? scenario.Name : scenario.Id;
                Scenarios.Add(new ScenarioItem(displayName, scenario));
            }

            var activeId = project.ActiveScenarioId;
            var active = Scenarios.FirstOrDefault(s => s.Scenario.Id == activeId) ?? Scenarios.First();
            SelectedScenario = active;

            HasData = true;
            StatusText = $"已加载 {project.ScenarioList.Count} 个方案";
        }
        catch (JsonException ex)
        {
            _currentJsonContent = null;
            StatusText = $"JSON 解析失败: {ex.Message}";
            HasData = false;
        }
    }

    private void ApplyScenario(EndaxisScenario scenario)
    {
        var data = scenario.Data;
        var pps = PixelsPerSecond;

        var trackModels = new ObservableCollection<TrackDisplayModel>();
        foreach (var track in data.Tracks)
        {
            var charName = track.Id;
            var rarity = 5;
            Bitmap? avatar = null;

            if (_characterMap.TryGetValue(track.Id, out var info))
            {
                charName = info.Name;
                rarity = info.Rarity;
                avatar = info.Avatar;
            }

            var actions = track.Actions
                .Where(a => RelevantActionTypes.Contains(a.Type) && !a.IsDisabled)
                .OrderBy(a => a.StartTime)
                .Select(a => CreateActionDisplay(a, pps))
                .ToList();

            trackModels.Add(new TrackDisplayModel
            {
                CharacterId = track.Id,
                CharacterName = charName,
                Rarity = rarity,
                AvatarImage = avatar,
                Actions = new ObservableCollection<ActionDisplayModel>(actions),
            });
        }

        Tracks = trackModels;

        BuildActiveSegments(data, pps);
    }

    private static ActionDisplayModel CreateActionDisplay(EndaxisAction action, double pps)
    {
        var typeLabel = ActionTypeLabels.GetValueOrDefault(action.Type, action.Type);
        var displayName = action.Name;
        if (string.IsNullOrEmpty(displayName))
            displayName = typeLabel;

        var tooltip = $"{displayName}\n类型: {typeLabel}\n时间: {action.StartTime:F2}s ~ {action.StartTime + action.Duration:F2}s\n持续: {action.Duration:F2}s";
        if (action.Type == "skill" && action.SpCost > 0)
            tooltip += $"\nSP消耗: {action.SpCost}";
        if (action.Type == "link" && action.Cooldown > 0)
            tooltip += $"\n冷却: {action.Cooldown:F1}s";
        if (action.Type == "ultimate")
        {
            if (action.GaugeCost > 0) tooltip += $"\n充能消耗: {action.GaugeCost}";
            if (action.AnimationTime > 0) tooltip += $"\n时停: {action.AnimationTime:F2}s";
            if (action.EnhancementTime > 0) tooltip += $"\n强化: {action.EnhancementTime:F1}s";
        }

        var widthPx = Math.Max(action.Duration * pps, 2);

        return new ActionDisplayModel
        {
            Name = displayName,
            Type = action.Type,
            TypeLabel = typeLabel,
            StartTime = action.StartTime,
            Duration = action.Duration,
            LeftPx = action.StartTime * pps,
            WidthPx = widthPx,
            TooltipText = tooltip,
        };
    }

    private void BuildActiveSegments(EndaxisScenarioData data, double pps)
    {
        var switchEvents = data.SwitchEvents;
        if (switchEvents == null || switchEvents.Count == 0)
        {
            ActiveSegments = [];
            HasSwitchData = false;
            return;
        }

        HasSwitchData = true;
        var sorted = switchEvents.OrderBy(e => e.Time).ToList();

        var maxTime = TotalDuration;
        foreach (var track in data.Tracks)
        {
            foreach (var action in track.Actions)
            {
                var end = action.StartTime + action.Duration;
                if (end > maxTime) maxTime = end;
            }
        }

        var segments = new ObservableCollection<ActiveCharacterSegment>();
        for (var i = 0; i < sorted.Count; i++)
        {
            var ev = sorted[i];
            var start = ev.Time;
            var end = i + 1 < sorted.Count ? sorted[i + 1].Time : maxTime;
            if (end <= start) continue;

            var charName = ev.CharacterId;
            if (_characterMap.TryGetValue(ev.CharacterId, out var info))
                charName = info.Name;

            segments.Add(new ActiveCharacterSegment
            {
                CharacterId = ev.CharacterId,
                CharacterName = charName,
                StartTime = start,
                EndTime = end,
                LeftPx = start * pps,
                WidthPx = (end - start) * pps,
                Color = GetCharacterColor(ev.CharacterId),
            });
        }

        if (sorted.Count > 0 && sorted[0].Time > 0)
        {
            var firstId = data.Tracks.Count > 0 ? data.Tracks[0].Id : sorted[0].CharacterId;
            var firstName = firstId;
            if (_characterMap.TryGetValue(firstId, out var info))
                firstName = info.Name;

            segments.Insert(0, new ActiveCharacterSegment
            {
                CharacterId = firstId,
                CharacterName = firstName,
                StartTime = 0,
                EndTime = sorted[0].Time,
                LeftPx = 0,
                WidthPx = sorted[0].Time * pps,
                Color = GetCharacterColor(firstId),
            });
        }

        ActiveSegments = segments;
    }

    private void RebuildTicks()
    {
        var ticks = new ObservableCollection<TimelineTick>();
        var pps = PixelsPerSecond;

        var majorInterval = pps >= 40 ? 5.0 : pps >= 20 ? 10.0 : 30.0;

        for (double t = 0; t <= TotalDuration; t += majorInterval)
        {
            ticks.Add(new TimelineTick
            {
                Time = t,
                LeftPx = t * pps,
                Label = $"{t:F0}s",
                IsMajor = true,
            });
        }

        TimelineTicks = ticks;
    }

    private static readonly IBrush[] CharacterPalette =
    [
        new SolidColorBrush(Color.Parse("#EF5350")),
        new SolidColorBrush(Color.Parse("#42A5F5")),
        new SolidColorBrush(Color.Parse("#66BB6A")),
        new SolidColorBrush(Color.Parse("#FFA726")),
    ];

    private readonly Dictionary<string, IBrush> _characterColorCache = new();

    private IBrush GetCharacterColor(string characterId)
    {
        if (_characterColorCache.TryGetValue(characterId, out var cached))
            return cached;

        var index = _characterColorCache.Count % CharacterPalette.Length;
        var brush = CharacterPalette[index];
        _characterColorCache[characterId] = brush;
        return brush;
    }

    [RelayCommand]
    private void ApplyToTeamSetup()
    {
        if (ApplyToTeamSetupAction == null || Tracks.Count == 0) return;

        var ids = Tracks.Take(4).Select(t => (string?)t.CharacterId).ToList();
        ApplyToTeamSetupAction(ids);
        StatusText = $"已将 {ids.Count(id => id != null)} 个角色应用到配队";
    }

    [RelayCommand]
    private async Task ImportJsonAsync()
    {
        await Task.CompletedTask;
    }

    public async Task LoadFromFile(string filePath)
    {
        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            LoadFromJson(json);
        }
        catch (Exception ex)
        {
            StatusText = $"读取文件失败: {ex.Message}";
        }
    }

    [RelayCommand]
    private void SavePreset()
    {
        if (string.IsNullOrEmpty(_currentJsonContent)) return;

        var preset = new BattleAxisPreset
        {
            Name = $"排轴 {SavedPresets.Count + 1}",
            JsonContent = _currentJsonContent,
            ImportedAt = DateTime.Now,
        };
        SavedPresets.Add(preset);
        SelectedSavedPreset = preset;
        SchedulePresetSave();
    }

    [RelayCommand]
    private void LoadSelectedPreset()
    {
        if (SelectedSavedPreset == null) return;
        LoadFromJson(SelectedSavedPreset.JsonContent);
    }

    [RelayCommand]
    private void DeletePreset()
    {
        if (SelectedSavedPreset == null) return;

        SavedPresets.Remove(SelectedSavedPreset);
        SelectedSavedPreset = null;
        SchedulePresetSave();
    }

    [RelayCommand]
    private void BeginRename()
    {
        if (SelectedSavedPreset == null) return;
        RenamingText = SelectedSavedPreset.Name;
        IsRenaming = true;
    }

    [RelayCommand]
    private void ConfirmRename()
    {
        if (SelectedSavedPreset == null || !IsRenaming) return;

        var trimmed = RenamingText.Trim();
        if (!string.IsNullOrEmpty(trimmed))
        {
            SelectedSavedPreset.Name = trimmed;
            var idx = SavedPresets.IndexOf(SelectedSavedPreset);
            if (idx >= 0)
            {
                var item = SavedPresets[idx];
                SavedPresets.RemoveAt(idx);
                SavedPresets.Insert(idx, item);
                SelectedSavedPreset = item;
            }
            SchedulePresetSave();
        }

        IsRenaming = false;
    }

    [RelayCommand]
    private void CancelRename()
    {
        IsRenaming = false;
    }

    private void LoadSavedPresets()
    {
        try
        {
            if (!File.Exists(_presetsPath)) return;

            var json = File.ReadAllText(_presetsPath);
            var data = JsonSerializer.Deserialize<BattleAxisPresetsData>(json);
            if (data == null) return;

            foreach (var preset in data.Presets)
                SavedPresets.Add(preset);

            if (data.ActivePresetId != null)
                SelectedSavedPreset = SavedPresets.FirstOrDefault(p => p.Id == data.ActivePresetId);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load battleaxis presets: {ex}");
        }
    }

    private void SchedulePresetSave()
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

            var data = new BattleAxisPresetsData
            {
                ActivePresetId = SelectedSavedPreset?.Id,
                Presets = SavedPresets.ToList(),
            };

            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_presetsPath, json, token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save battleaxis presets: {ex}");
        }
    }

    public void Dispose()
    {
        _saveCts?.Cancel();
        _saveCts?.Dispose();
        foreach (var (_, _, _, avatar) in _characterMap.Values)
            avatar?.Dispose();
        _characterMap.Clear();
    }
}

public class ScenarioItem(string displayName, EndaxisScenario scenario)
{
    public string DisplayName { get; } = displayName;
    public EndaxisScenario Scenario { get; } = scenario;

    public override string ToString() => DisplayName;
}

public class TrackDisplayModel
{
    public string CharacterId { get; init; } = "";
    public string CharacterName { get; init; } = "";
    public int Rarity { get; init; }
    public Bitmap? AvatarImage { get; init; }
    public ObservableCollection<ActionDisplayModel> Actions { get; init; } = [];
}

public class ActionDisplayModel
{
    public string Name { get; init; } = "";
    public string Type { get; init; } = "";
    public string TypeLabel { get; init; } = "";
    public double StartTime { get; init; }
    public double Duration { get; init; }
    public double LeftPx { get; init; }
    public double WidthPx { get; init; }
    public string TooltipText { get; init; } = "";
}

public class ActiveCharacterSegment
{
    public string CharacterId { get; init; } = "";
    public string CharacterName { get; init; } = "";
    public double StartTime { get; init; }
    public double EndTime { get; init; }
    public double LeftPx { get; init; }
    public double WidthPx { get; init; }
    public IBrush Color { get; init; } = Brushes.Gray;
}

public class TimelineTick
{
    public double Time { get; init; }
    public double LeftPx { get; init; }
    public string Label { get; init; } = "";
    public bool IsMajor { get; init; }
}

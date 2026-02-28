using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace EndFieldFightHelper.Models;

public partial class TeamPreset : ObservableObject
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [ObservableProperty]
    private string _name = "新配队";

    public List<string?> SlotCharacterIds { get; set; } = [null, null, null, null];
}

public class TeamPresetsData
{
    public string? ActivePresetId { get; set; }
    public List<string?>? CurrentSlotCharacterIds { get; set; }
    public List<TeamPreset> Presets { get; set; } = [];
}

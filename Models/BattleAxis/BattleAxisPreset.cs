using System;
using System.Collections.Generic;

namespace EndFieldFightHelper.Models.BattleAxis;

public class BattleAxisPreset
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string JsonContent { get; set; } = "";
    public DateTime ImportedAt { get; set; } = DateTime.Now;

    public override string ToString() => Name;
}

public class BattleAxisPresetsData
{
    public List<BattleAxisPreset> Presets { get; set; } = [];
    public string? ActivePresetId { get; set; }
}

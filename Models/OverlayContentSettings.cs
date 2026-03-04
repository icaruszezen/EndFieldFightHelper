using System.Collections.Generic;

namespace EndFieldFightHelper.Models;

public class OverlayContentSettings
{
    public bool ShowLog { get; set; } = true;
    public bool ShowPipelineStatus { get; set; } = false;
    public List<string> VisiblePipelineNames { get; set; } = [];
    public bool ShowBattleAxis { get; set; } = false;
    public double BattleAxisFontSize { get; set; } = 11;
}

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace EndFieldFightHelper.Models.BattleAxis;

public class EndaxisTrack
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("actions")]
    public List<EndaxisAction> Actions { get; set; } = [];
}

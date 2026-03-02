using System.Text.Json.Serialization;

namespace EndFieldFightHelper.Models.BattleAxis;

public class EndaxisAction
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("instanceId")]
    public string InstanceId { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("startTime")]
    public double StartTime { get; set; }

    [JsonPropertyName("duration")]
    public double Duration { get; set; }

    [JsonPropertyName("isDisabled")]
    public bool IsDisabled { get; set; }

    [JsonPropertyName("triggerWindow")]
    public double TriggerWindow { get; set; } = -1;

    [JsonPropertyName("animationTime")]
    public double AnimationTime { get; set; }

    [JsonPropertyName("enhancementTime")]
    public double EnhancementTime { get; set; }

    [JsonPropertyName("spCost")]
    public double SpCost { get; set; }

    [JsonPropertyName("cooldown")]
    public double Cooldown { get; set; }

    [JsonPropertyName("gaugeCost")]
    public double GaugeCost { get; set; }
}

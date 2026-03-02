using System.Text.Json.Serialization;

namespace EndFieldFightHelper.Models.BattleAxis;

public class EndaxisSwitchEvent
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("time")]
    public double Time { get; set; }

    [JsonPropertyName("characterId")]
    public string CharacterId { get; set; } = "";
}

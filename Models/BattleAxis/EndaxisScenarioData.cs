using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace EndFieldFightHelper.Models.BattleAxis;

public class EndaxisScenarioData
{
    [JsonPropertyName("tracks")]
    public List<EndaxisTrack> Tracks { get; set; } = [];

    [JsonPropertyName("switchEvents")]
    public List<EndaxisSwitchEvent>? SwitchEvents { get; set; }

    [JsonPropertyName("prepDuration")]
    public double PrepDuration { get; set; } = 5;
}

public class EndaxisScenario
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("data")]
    public EndaxisScenarioData Data { get; set; } = new();
}

public class EndaxisProject
{
    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("scenarioList")]
    public List<EndaxisScenario> ScenarioList { get; set; } = [];

    [JsonPropertyName("activeScenarioId")]
    public string? ActiveScenarioId { get; set; }
}

using System.Text.Json.Serialization;

namespace SGMDTXTools.Core.Models;

public class MatchResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("elapsed_ms")]
    public int ElapsedMs { get; set; }

    [JsonPropertyName("matches")]
    public List<MatchItem> Matches { get; set; } = [];

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

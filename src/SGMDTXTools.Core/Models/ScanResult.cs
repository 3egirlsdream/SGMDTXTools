using System.Text.Json.Serialization;

namespace SGMDTXTools.Core.Models;

public class ScanResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("elapsed_ms")]
    public int ElapsedMs { get; set; }

    [JsonPropertyName("image_size")]
    public ImageSize? ImageSize { get; set; }

    [JsonPropertyName("texts")]
    public List<TextItem> Texts { get; set; } = [];

    [JsonPropertyName("matches")]
    public List<MatchItem> Matches { get; set; } = [];

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

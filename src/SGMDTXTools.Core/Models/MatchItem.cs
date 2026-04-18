using System.Text.Json.Serialization;

namespace SGMDTXTools.Core.Models;

public class MatchItem
{
    [JsonPropertyName("template")]
    public string Template { get; set; } = string.Empty;

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("bbox")]
    public BoundingBox Bbox { get; set; } = new();

    [JsonPropertyName("center")]
    public PointCoord Center { get; set; } = new();

    public override string ToString() =>
        $"[{Template}] ({Bbox.X},{Bbox.Y}) {Bbox.Width}x{Bbox.Height} conf:{Confidence:F2}";
}

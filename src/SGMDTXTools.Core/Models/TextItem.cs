using System.Text.Json.Serialization;

namespace SGMDTXTools.Core.Models;

public class TextItem
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("bbox")]
    public BoundingBox Bbox { get; set; } = new();

    [JsonPropertyName("center")]
    public PointCoord Center { get; set; } = new();

    public override string ToString() =>
        $"\"{Text}\" ({Bbox.X},{Bbox.Y}) {Bbox.Width}x{Bbox.Height} conf:{Confidence:F2}";
}

public class PointCoord
{
    [JsonPropertyName("x")]
    public int X { get; set; }

    [JsonPropertyName("y")]
    public int Y { get; set; }
}

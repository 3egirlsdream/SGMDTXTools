using System.Text.Json.Serialization;

namespace SGMDTXTools.Core.Models;

public class BoundingBox
{
    [JsonPropertyName("x")]
    public int X { get; set; }

    [JsonPropertyName("y")]
    public int Y { get; set; }

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }

    [JsonIgnore]
    public int CenterX => X + Width / 2;

    [JsonIgnore]
    public int CenterY => Y + Height / 2;

    /// <summary>
    /// 转换为 InputCoordinate，可直接传给 SendInputSimulator
    /// </summary>
    public InputCoordinate ToCoordinate() => InputCoordinate.FromPixel(CenterX, CenterY);

    public override string ToString() => $"({X},{Y}) {Width}x{Height}";
}

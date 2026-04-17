namespace SGMDTXTools.Core.Models;

public class CaptureResult
{
    public byte[] ImageData { get; set; } = Array.Empty<byte>();
    public int Width { get; set; }
    public int Height { get; set; }
    public DateTime CapturedAt { get; set; } = DateTime.Now;
    public long CaptureTimeMs { get; set; }
    public string? SavedPath { get; set; }
}

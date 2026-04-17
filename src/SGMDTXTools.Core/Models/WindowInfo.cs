namespace SGMDTXTools.Core.Models;

public class WindowInfo
{
    public IntPtr Handle { get; set; }
    public string Title { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    public uint ProcessId { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public DateTime LastUpdated { get; set; } = DateTime.Now;

    public override string ToString() =>
        $"[Handle=0x{Handle:X}, PID={ProcessId}, Process='{ProcessName}', Title='{Title}', Rect=({X},{Y},{Width}x{Height})]";
}

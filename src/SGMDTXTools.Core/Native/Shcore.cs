using System.Runtime.InteropServices;

namespace SGMDTXTools.Core.Native;

public static class Shcore
{
    // DPI_AWARENESS_CONTEXT values
    public static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new(-4);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    [DllImport("shcore.dll", SetLastError = true)]
    public static extern int GetDpiForMonitor(
        IntPtr hMonitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    // MONITOR_DEFAULTTONEAREST
    public const uint MONITOR_DEFAULTTONEAREST = 2;

    // MDT_EFFECTIVE_DPI
    public const int MDT_EFFECTIVE_DPI = 0;
}

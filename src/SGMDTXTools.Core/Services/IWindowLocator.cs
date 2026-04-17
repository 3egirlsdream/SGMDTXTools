using SGMDTXTools.Core.Models;

namespace SGMDTXTools.Core.Services;

public interface IWindowLocator
{
    WindowInfo? FindWindow(string processName);
    IReadOnlyList<WindowInfo> FindAllWindows(string processName);
    WindowInfo? RefreshWindowInfo(IntPtr handle);
    Task WatchWindow(IntPtr handle, Action<WindowInfo> onChanged, CancellationToken ct);
}

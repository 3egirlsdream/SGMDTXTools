using System.Diagnostics;
using System.Text;
using Serilog;
using SGMDTXTools.Core.Models;
using SGMDTXTools.Core.Native;

namespace SGMDTXTools.Core.Services;

public class ProcessWindowLocator : IWindowLocator
{
    private readonly ILogger _log;

    public ProcessWindowLocator(ILogger logger)
    {
        _log = logger.ForContext<ProcessWindowLocator>();
    }

    public WindowInfo? FindWindow(string processName)
    {
        var windows = FindAllWindows(processName);
        if (windows.Count == 0)
            return null;

        // 取最大可见窗口作为主窗口
        var main = windows.OrderByDescending(w => w.Width * w.Height).First();
        _log.Information("选定主窗口: {Window}", main);
        return main;
    }

    public IReadOnlyList<WindowInfo> FindAllWindows(string processName)
    {
        _log.Debug("开始查找进程: {ProcessName}", processName);

        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName(processName);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "获取进程列表失败: {ProcessName}", processName);
            return Array.Empty<WindowInfo>();
        }

        if (processes.Length == 0)
        {
            _log.Error("进程 '{ProcessName}' 未运行", processName);
            return Array.Empty<WindowInfo>();
        }

        var pids = new HashSet<uint>(processes.Select(p => (uint)p.Id));
        _log.Debug("找到 {Count} 个进程实例, PIDs: [{PIDs}]",
            processes.Length, string.Join(", ", pids));

        var results = new List<WindowInfo>();

        User32.EnumWindows((hWnd, _) =>
        {
            User32.GetWindowThreadProcessId(hWnd, out uint windowPid);

            if (!pids.Contains(windowPid))
                return true; // 继续枚举

            if (!User32.IsWindowVisible(hWnd))
            {
                _log.Debug("跳过不可见窗口: Handle=0x{Handle:X}, PID={PID}", hWnd, windowPid);
                return true;
            }

            var title = GetWindowTitle(hWnd);
            var info = BuildWindowInfo(hWnd, title, processName, windowPid);

            if (info.Width > 0 && info.Height > 0)
            {
                _log.Information("找到游戏窗口: {Window}", info);
                results.Add(info);
            }
            else
            {
                _log.Debug("跳过零尺寸窗口: Handle=0x{Handle:X}, Title='{Title}'", hWnd, title);
            }

            return true; // 继续枚举
        }, IntPtr.Zero);

        if (results.Count == 0)
        {
            _log.Warning("进程 '{ProcessName}' 正在运行但未找到可见窗口", processName);
        }

        return results;
    }

    public WindowInfo? RefreshWindowInfo(IntPtr handle)
    {
        if (!User32.IsWindow(handle))
        {
            _log.Warning("窗口句柄已失效: Handle=0x{Handle:X}", handle);
            return null;
        }

        User32.GetWindowThreadProcessId(handle, out uint pid);
        var title = GetWindowTitle(handle);

        string processName = string.Empty;
        try
        {
            var process = Process.GetProcessById((int)pid);
            processName = process.ProcessName;
        }
        catch
        {
            _log.Warning("无法获取进程名: PID={PID}", pid);
        }

        return BuildWindowInfo(handle, title, processName, pid);
    }

    public async Task WatchWindow(IntPtr handle, Action<WindowInfo> onChanged, CancellationToken ct)
    {
        _log.Information("开始监控窗口位置: Handle=0x{Handle:X}", handle);
        RECT lastRect = default;
        bool firstCheck = true;

        while (!ct.IsCancellationRequested)
        {
            if (!User32.IsWindow(handle))
            {
                _log.Warning("监控的窗口已关闭: Handle=0x{Handle:X}", handle);
                break;
            }

            User32.GetWindowRect(handle, out RECT currentRect);

            if (firstCheck ||
                currentRect.Left != lastRect.Left ||
                currentRect.Top != lastRect.Top ||
                currentRect.Right != lastRect.Right ||
                currentRect.Bottom != lastRect.Bottom)
            {
                lastRect = currentRect;
                firstCheck = false;

                var info = RefreshWindowInfo(handle);
                if (info != null)
                {
                    _log.Information("窗口位置变化: {Window}", info);
                    onChanged(info);
                }
            }

            try
            {
                await Task.Delay(500, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _log.Information("停止监控窗口位置: Handle=0x{Handle:X}", handle);
    }

    private static string GetWindowTitle(IntPtr hWnd)
    {
        int length = User32.GetWindowTextLength(hWnd);
        if (length == 0)
            return string.Empty;

        var sb = new StringBuilder(length + 1);
        User32.GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private WindowInfo BuildWindowInfo(IntPtr hWnd, string title, string processName, uint pid)
    {
        User32.GetClientRect(hWnd, out RECT clientRect);

        var point = new POINT(0, 0);
        User32.ClientToScreen(hWnd, ref point);

        var info = new WindowInfo
        {
            Handle = hWnd,
            Title = title,
            ProcessName = processName,
            ProcessId = pid,
            X = point.X,
            Y = point.Y,
            Width = clientRect.Width,
            Height = clientRect.Height,
            LastUpdated = DateTime.Now
        };

        _log.Debug("构建窗口信息: Handle=0x{Handle:X}, ClientOrigin={Origin}, ClientSize={W}x{H}",
            hWnd, point, clientRect.Width, clientRect.Height);

        return info;
    }
}

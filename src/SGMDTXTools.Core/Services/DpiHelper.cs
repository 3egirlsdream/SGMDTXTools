using System.Runtime.InteropServices;
using Serilog;
using SGMDTXTools.Core.Native;

namespace SGMDTXTools.Core.Services;

public static class DpiHelper
{
    private const int ERROR_ACCESS_DENIED = 5;

    public static bool SetDpiAwareness(ILogger log)
    {
        try
        {
            bool result = Shcore.SetProcessDpiAwarenessContext(
                Shcore.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);

            if (result)
            {
                log.Information("DPI感知已设置: PerMonitorV2");
                return true;
            }

            // 返回false时检查原因: ERROR_ACCESS_DENIED 表示manifest已设置DPI感知，属正常情况
            int lastError = Marshal.GetLastWin32Error();
            if (lastError == ERROR_ACCESS_DENIED)
            {
                log.Information("DPI感知已由manifest设置, 无需重复配置");
                return true;
            }

            log.Warning("SetProcessDpiAwarenessContext(PerMonitorV2)返回false, Win32Error={Error}, 尝试后备方案", lastError);
        }
        catch (EntryPointNotFoundException)
        {
            log.Warning("SetProcessDpiAwarenessContext不可用(低于Win10 1703), 尝试SetProcessDPIAware");
        }

        // 后备方案
        try
        {
            bool fallback = User32.SetProcessDPIAware();
            log.Information("DPI感知后备方案: SetProcessDPIAware={Result}", fallback);
            return fallback;
        }
        catch (Exception ex)
        {
            log.Error(ex, "DPI感知设置完全失败");
            return false;
        }
    }

    public static double GetScaleForWindow(IntPtr hWnd, ILogger log)
    {
        try
        {
            var hMonitor = Shcore.MonitorFromWindow(hWnd, Shcore.MONITOR_DEFAULTTONEAREST);
            int hr = Shcore.GetDpiForMonitor(hMonitor, Shcore.MDT_EFFECTIVE_DPI, out uint dpiX, out uint _);

            if (hr == 0) // S_OK
            {
                double scale = dpiX / 96.0;
                log.Debug("窗口DPI缩放: Handle=0x{Handle:X}, DPI={DPI}, Scale={Scale:F2}", hWnd, dpiX, scale);
                return scale;
            }

            log.Warning("GetDpiForMonitor失败: HRESULT=0x{HR:X}", hr);
        }
        catch (EntryPointNotFoundException)
        {
            log.Warning("GetDpiForMonitor不可用, 使用默认缩放1.0");
        }

        return 1.0;
    }
}

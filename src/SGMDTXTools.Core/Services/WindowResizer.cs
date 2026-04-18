using System.Runtime.InteropServices;
using Serilog;
using SGMDTXTools.Core.Native;

namespace SGMDTXTools.Core.Services;

/// <summary>
/// 将游戏窗口的客户区精确调整为目标像素尺寸（自动补偿标题栏和边框）
/// </summary>
public class WindowResizer
{
    private readonly ILogger _log;

    public WindowResizer(ILogger logger)
    {
        _log = logger.ForContext<WindowResizer>();
    }

    /// <summary>
    /// 将窗口客户区调整为目标尺寸
    /// </summary>
    /// <returns>(是否做了调整, 实际客户区宽, 实际客户区高)</returns>
    public (bool WasResized, int ActualWidth, int ActualHeight) ResizeClientArea(
        IntPtr hWnd, int targetWidth, int targetHeight)
    {
        if (!User32.IsWindow(hWnd))
        {
            _log.Error("窗口句柄无效: Handle=0x{Handle:X}", hWnd);
            return (false, 0, 0);
        }

        // 如果窗口最小化或最大化，先还原
        if (User32.IsIconic(hWnd) || User32.IsZoomed(hWnd))
        {
            _log.Debug("窗口处于最小化/最大化状态，先还原: Handle=0x{Handle:X}", hWnd);
            User32.ShowWindow(hWnd, User32.SW_RESTORE);
            Thread.Sleep(100);
        }

        // 获取当前客户区尺寸
        if (!User32.GetClientRect(hWnd, out RECT clientRect))
        {
            _log.Error("GetClientRect 失败: Handle=0x{Handle:X}, Win32Error={Error}",
                hWnd, Marshal.GetLastWin32Error());
            return (false, 0, 0);
        }

        int currentW = clientRect.Width;
        int currentH = clientRect.Height;

        // 如果已是目标尺寸（±1px 容差），跳过
        if (Math.Abs(currentW - targetWidth) <= 1 && Math.Abs(currentH - targetHeight) <= 1)
        {
            _log.Information("窗口已是目标尺寸: {W}x{H}", currentW, currentH);
            return (false, currentW, currentH);
        }

        // 获取当前窗口完整矩形（含边框）
        if (!User32.GetWindowRect(hWnd, out RECT windowRect))
        {
            _log.Error("GetWindowRect 失败: Handle=0x{Handle:X}, Win32Error={Error}",
                hWnd, Marshal.GetLastWin32Error());
            return (false, currentW, currentH);
        }

        // 通过 AdjustWindowRectEx 精确计算目标窗口尺寸
        IntPtr stylePtr = User32.GetWindowLongPtr(hWnd, User32.GWL_STYLE);
        IntPtr exStylePtr = User32.GetWindowLongPtr(hWnd, User32.GWL_EXSTYLE);
        uint style = (uint)(long)stylePtr;
        uint exStyle = (uint)(long)exStylePtr;

        var targetRect = new RECT
        {
            Left = 0,
            Top = 0,
            Right = targetWidth,
            Bottom = targetHeight
        };

        if (!User32.AdjustWindowRectEx(ref targetRect, style, false, exStyle))
        {
            // AdjustWindowRectEx 失败时回退到差值法
            _log.Warning("AdjustWindowRectEx 失败, 回退到差值法: Win32Error={Error}",
                Marshal.GetLastWin32Error());
            int borderW = windowRect.Width - currentW;
            int borderH = windowRect.Height - currentH;
            targetRect = new RECT
            {
                Left = 0,
                Top = 0,
                Right = targetWidth + borderW,
                Bottom = targetHeight + borderH
            };
        }

        int newWindowW = targetRect.Width;
        int newWindowH = targetRect.Height;

        _log.Debug("计算目标窗口尺寸: 客户区={TW}x{TH} → 窗口={WW}x{WH} (边框补偿={BW}x{BH})",
            targetWidth, targetHeight, newWindowW, newWindowH,
            newWindowW - targetWidth, newWindowH - targetHeight);

        // 保持窗口左上角位置不变，调整大小
        if (!User32.MoveWindow(hWnd, windowRect.Left, windowRect.Top, newWindowW, newWindowH, true))
        {
            int err = Marshal.GetLastWin32Error();
            _log.Error("MoveWindow 失败: Handle=0x{Handle:X}, Win32Error={Error}", hWnd, err);
            return (false, currentW, currentH);
        }

        // 等待窗口重绘
        Thread.Sleep(100);

        // 验证调整结果
        User32.GetClientRect(hWnd, out RECT verifyRect);
        int actualW = verifyRect.Width;
        int actualH = verifyRect.Height;

        if (Math.Abs(actualW - targetWidth) > 2 || Math.Abs(actualH - targetHeight) > 2)
        {
            _log.Warning("调整后尺寸偏差: 目标={TW}x{TH}, 实际={AW}x{AH} (游戏可能限制了窗口大小)",
                targetWidth, targetHeight, actualW, actualH);
        }
        else
        {
            _log.Information("调整窗口客户区: {OldW}x{OldH} → {NewW}x{NewH}",
                currentW, currentH, actualW, actualH);
        }

        return (true, actualW, actualH);
    }

    /// <summary>
    /// 获取窗口当前客户区尺寸
    /// </summary>
    public (int Width, int Height) GetClientSize(IntPtr hWnd)
    {
        if (!User32.GetClientRect(hWnd, out RECT rect))
            return (0, 0);
        return (rect.Width, rect.Height);
    }
}

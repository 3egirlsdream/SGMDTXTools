using System.Runtime.InteropServices;
using Serilog;
using SGMDTXTools.Core.Models;
using SGMDTXTools.Core.Native;

namespace SGMDTXTools.Core.Services;

/// <summary>
/// 基于 SendInput 的输入模拟实现，通过硬件级事件注入操作游戏窗口（需前台激活）
/// </summary>
public class SendInputSimulator : IInputSimulator
{
    private readonly ILogger _log;
    private readonly GridOverlay _gridOverlay;
    private readonly InputSimulatorConfig _config;

    public SendInputSimulator(ILogger logger, GridOverlay gridOverlay, InputSimulatorConfig? config = null)
    {
        _log = logger.ForContext<SendInputSimulator>();
        _gridOverlay = gridOverlay;
        _config = config ?? new InputSimulatorConfig();
    }

    public async Task ClickAsync(IntPtr hWnd, InputCoordinate coord, MouseButton button = MouseButton.Left, CancellationToken ct = default)
    {
        var (sx, sy) = ResolveToScreen(hWnd, coord);
        await EnsureForeground(hWnd, ct);

        User32.SetCursorPos(sx, sy);
        await Task.Delay(_config.PreActionDelayMs, ct);

        if (button == MouseButton.Left)
        {
            DoSendInput(User32.MOUSEEVENTF_LEFTDOWN);
            await Task.Delay(_config.ClickDelayMs, ct);
            DoSendInput(User32.MOUSEEVENTF_LEFTUP);
        }
        else
        {
            DoSendInput(User32.MOUSEEVENTF_RIGHTDOWN);
            await Task.Delay(_config.ClickDelayMs, ct);
            DoSendInput(User32.MOUSEEVENTF_RIGHTUP);
        }

        _log.Information("点击: {Button} @ ({X},{Y}) [{Coord}] [hWnd=0x{Handle:X}]",
            button, sx, sy, coord, hWnd);
    }

    public async Task DoubleClickAsync(IntPtr hWnd, InputCoordinate coord, CancellationToken ct = default)
    {
        var (sx, sy) = ResolveToScreen(hWnd, coord);
        await EnsureForeground(hWnd, ct);

        User32.SetCursorPos(sx, sy);
        await Task.Delay(_config.PreActionDelayMs, ct);

        // 两次快速点击产生双击
        DoSendInput(User32.MOUSEEVENTF_LEFTDOWN);
        await Task.Delay(_config.ClickDelayMs, ct);
        DoSendInput(User32.MOUSEEVENTF_LEFTUP);

        await Task.Delay(_config.DoubleClickIntervalMs, ct);

        DoSendInput(User32.MOUSEEVENTF_LEFTDOWN);
        await Task.Delay(_config.ClickDelayMs, ct);
        DoSendInput(User32.MOUSEEVENTF_LEFTUP);

        _log.Information("双击: ({X},{Y}) [{Coord}] [hWnd=0x{Handle:X}]", sx, sy, coord, hWnd);
    }

    public async Task DragAsync(IntPtr hWnd, InputCoordinate from, InputCoordinate to, CancellationToken ct = default)
    {
        var (fromSx, fromSy) = ResolveToScreen(hWnd, from);
        var (toSx, toSy) = ResolveToScreen(hWnd, to);

        await EnsureForeground(hWnd, ct);

        // 移动到起点并按下
        User32.SetCursorPos(fromSx, fromSy);
        await Task.Delay(_config.PreActionDelayMs, ct);

        DoSendInput(User32.MOUSEEVENTF_LEFTDOWN);
        await Task.Delay(_config.ClickDelayMs, ct);

        // 线性插值移动（屏幕坐标）
        for (int i = 1; i <= _config.DragStepCount; i++)
        {
            ct.ThrowIfCancellationRequested();

            float t = (float)i / _config.DragStepCount;
            int interpX = fromSx + (int)((toSx - fromSx) * t);
            int interpY = fromSy + (int)((toSy - fromSy) * t);

            User32.SetCursorPos(interpX, interpY);
            await Task.Delay(_config.MoveStepDelayMs, ct);
        }

        // 释放
        DoSendInput(User32.MOUSEEVENTF_LEFTUP);

        _log.Information("拖拽: ({FromX},{FromY}) → ({ToX},{ToY}), {Steps}步 [hWnd=0x{Handle:X}]",
            fromSx, fromSy, toSx, toSy, _config.DragStepCount, hWnd);
    }

    public async Task ScrollAsync(IntPtr hWnd, InputCoordinate coord, ScrollDirection direction, int clicks = 3, CancellationToken ct = default)
    {
        var (sx, sy) = ResolveToScreen(hWnd, coord);
        await EnsureForeground(hWnd, ct);

        User32.SetCursorPos(sx, sy);
        await Task.Delay(_config.PreActionDelayMs, ct);

        int delta = direction == ScrollDirection.Up ? _config.ScrollDelta : -_config.ScrollDelta;

        for (int i = 0; i < clicks; i++)
        {
            ct.ThrowIfCancellationRequested();
            DoSendInput(User32.MOUSEEVENTF_WHEEL, unchecked((uint)delta));
            await Task.Delay(_config.MoveStepDelayMs, ct);
        }

        _log.Information("滚轮: {Direction} x{Clicks} @ ({X},{Y}) [{Coord}] [hWnd=0x{Handle:X}]",
            direction, clicks, sx, sy, coord, hWnd);
    }

    public async Task MoveToAsync(IntPtr hWnd, InputCoordinate coord, CancellationToken ct = default)
    {
        var (sx, sy) = ResolveToScreen(hWnd, coord);
        await EnsureForeground(hWnd, ct);

        User32.SetCursorPos(sx, sy);

        _log.Information("移动: ({X},{Y}) [{Coord}] [hWnd=0x{Handle:X}]", sx, sy, coord, hWnd);
    }

    // --- 私有辅助 ---

    private (int screenX, int screenY) ResolveToScreen(IntPtr hWnd, InputCoordinate coord)
    {
        if (!User32.GetClientRect(hWnd, out RECT rect))
            throw new InvalidOperationException($"GetClientRect 失败, hWnd=0x{hWnd:X}");
        if (rect.Width <= 0 || rect.Height <= 0)
            throw new InvalidOperationException($"客户区尺寸无效: {rect.Width}x{rect.Height}, hWnd=0x{hWnd:X}");

        var (cx, cy) = coord.ResolvePixel(_gridOverlay, rect.Width, rect.Height);
        _log.Debug("坐标解析: {Coord} → 客户区({CX},{CY}), 尺寸={W}x{H}", coord, cx, cy, rect.Width, rect.Height);

        var point = new POINT(cx, cy);
        User32.ClientToScreen(hWnd, ref point);
        _log.Debug("屏幕坐标: ({SX},{SY})", point.X, point.Y);

        return (point.X, point.Y);
    }

    private async Task EnsureForeground(IntPtr hWnd, CancellationToken ct)
    {
        if (User32.GetForegroundWindow() == hWnd)
            return;

        if (User32.IsIconic(hWnd))
            User32.ShowWindow(hWnd, User32.SW_RESTORE);

        User32.SetForegroundWindow(hWnd);
        await Task.Delay(50, ct);
        _log.Debug("窗口前台激活: hWnd=0x{Handle:X}", hWnd);
    }

    private void DoSendInput(uint flags, uint mouseData = 0)
    {
        var input = new INPUT
        {
            type = User32.INPUT_MOUSE,
            u = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dx = 0,
                    dy = 0,
                    mouseData = mouseData,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = UIntPtr.Zero
                }
            }
        };

        uint sent = User32.SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        if (sent == 0)
        {
            int err = Marshal.GetLastWin32Error();
            _log.Warning("SendInput 失败: flags=0x{Flags:X4}, error={Error} (如果游戏以管理员运行，请尝试提权启动本工具)",
                flags, err);
        }
        else
        {
            _log.Debug("SendInput: flags=0x{Flags:X4}, mouseData=0x{Data:X}", flags, mouseData);
        }
    }
}

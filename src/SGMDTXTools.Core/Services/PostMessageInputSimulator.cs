using Serilog;
using SGMDTXTools.Core.Models;
using SGMDTXTools.Core.Native;

namespace SGMDTXTools.Core.Services;

/// <summary>
/// 基于 PostMessage 的输入模拟实现，发送到窗口句柄，无需前台激活
/// </summary>
public class PostMessageInputSimulator : IInputSimulator
{
    private readonly ILogger _log;
    private readonly GridOverlay _gridOverlay;
    private readonly InputSimulatorConfig _config;

    public PostMessageInputSimulator(ILogger logger, GridOverlay gridOverlay, InputSimulatorConfig? config = null)
    {
        _log = logger.ForContext<PostMessageInputSimulator>();
        _gridOverlay = gridOverlay;
        _config = config ?? new InputSimulatorConfig();
    }

    public async Task ClickAsync(IntPtr hWnd, InputCoordinate coord, MouseButton button = MouseButton.Left, CancellationToken ct = default)
    {
        var (w, h) = GetClientSize(hWnd);
        var (x, y) = ResolveAndLog(coord, w, h);
        var lParam = User32.MakeLParam(x, y);

        // 先移动光标到目标位置
        PostMsg(hWnd, User32.WM_MOUSEMOVE, IntPtr.Zero, lParam);
        await Task.Delay(_config.PreActionDelayMs, ct);

        if (button == MouseButton.Left)
        {
            PostMsg(hWnd, User32.WM_LBUTTONDOWN, (IntPtr)User32.MK_LBUTTON, lParam);
            await Task.Delay(_config.ClickDelayMs, ct);
            PostMsg(hWnd, User32.WM_LBUTTONUP, IntPtr.Zero, lParam);
        }
        else
        {
            PostMsg(hWnd, User32.WM_RBUTTONDOWN, (IntPtr)User32.MK_RBUTTON, lParam);
            await Task.Delay(_config.ClickDelayMs, ct);
            PostMsg(hWnd, User32.WM_RBUTTONUP, IntPtr.Zero, lParam);
        }

        _log.Information("点击: {Button} @ ({X},{Y}) [{Coord}] [hWnd=0x{Handle:X}]",
            button, x, y, coord, hWnd);
    }

    public async Task DoubleClickAsync(IntPtr hWnd, InputCoordinate coord, CancellationToken ct = default)
    {
        var (w, h) = GetClientSize(hWnd);
        var (x, y) = ResolveAndLog(coord, w, h);
        var lParam = User32.MakeLParam(x, y);

        // 先移动光标
        PostMsg(hWnd, User32.WM_MOUSEMOVE, IntPtr.Zero, lParam);
        await Task.Delay(_config.PreActionDelayMs, ct);

        // Windows 双击序列: DOWN → UP → DBLCLK → UP
        PostMsg(hWnd, User32.WM_LBUTTONDOWN, (IntPtr)User32.MK_LBUTTON, lParam);
        await Task.Delay(_config.ClickDelayMs, ct);
        PostMsg(hWnd, User32.WM_LBUTTONUP, IntPtr.Zero, lParam);

        await Task.Delay(_config.DoubleClickIntervalMs, ct);

        PostMsg(hWnd, User32.WM_LBUTTONDBLCLK, (IntPtr)User32.MK_LBUTTON, lParam);
        await Task.Delay(_config.ClickDelayMs, ct);
        PostMsg(hWnd, User32.WM_LBUTTONUP, IntPtr.Zero, lParam);

        _log.Information("双击: ({X},{Y}) [{Coord}] [hWnd=0x{Handle:X}]", x, y, coord, hWnd);
    }

    public async Task DragAsync(IntPtr hWnd, InputCoordinate from, InputCoordinate to, CancellationToken ct = default)
    {
        var (w, h) = GetClientSize(hWnd);
        var (fromX, fromY) = ResolveAndLog(from, w, h);
        var (toX, toY) = ResolveAndLog(to, w, h);

        // 移动到起点
        var fromLParam = User32.MakeLParam(fromX, fromY);
        PostMsg(hWnd, User32.WM_MOUSEMOVE, IntPtr.Zero, fromLParam);
        await Task.Delay(_config.PreActionDelayMs, ct);

        // 按下左键
        PostMsg(hWnd, User32.WM_LBUTTONDOWN, (IntPtr)User32.MK_LBUTTON, fromLParam);
        await Task.Delay(_config.ClickDelayMs, ct);

        // 线性插值移动
        for (int i = 1; i <= _config.DragStepCount; i++)
        {
            ct.ThrowIfCancellationRequested();

            float t = (float)i / _config.DragStepCount;
            int interpX = fromX + (int)((toX - fromX) * t);
            int interpY = fromY + (int)((toY - fromY) * t);

            var stepLParam = User32.MakeLParam(interpX, interpY);
            PostMsg(hWnd, User32.WM_MOUSEMOVE, (IntPtr)User32.MK_LBUTTON, stepLParam);
            await Task.Delay(_config.MoveStepDelayMs, ct);
        }

        // 释放左键
        var toLParam = User32.MakeLParam(toX, toY);
        PostMsg(hWnd, User32.WM_LBUTTONUP, IntPtr.Zero, toLParam);

        _log.Information("拖拽: ({FromX},{FromY}) → ({ToX},{ToY}), {Steps}步 [hWnd=0x{Handle:X}]",
            fromX, fromY, toX, toY, _config.DragStepCount, hWnd);
    }

    public async Task ScrollAsync(IntPtr hWnd, InputCoordinate coord, ScrollDirection direction, int clicks = 3, CancellationToken ct = default)
    {
        var (w, h) = GetClientSize(hWnd);
        var (x, y) = ResolveAndLog(coord, w, h);

        // WM_MOUSEWHEEL 的 lParam 必须使用屏幕坐标
        var (screenX, screenY) = ClientToScreenCoord(hWnd, x, y);
        var screenLParam = User32.MakeLParam(screenX, screenY);

        int delta = direction == ScrollDirection.Up ? _config.ScrollDelta : -_config.ScrollDelta;

        for (int i = 0; i < clicks; i++)
        {
            ct.ThrowIfCancellationRequested();

            var wParam = User32.MakeWheelWParam(delta);
            PostMsg(hWnd, User32.WM_MOUSEWHEEL, wParam, screenLParam);
            await Task.Delay(_config.MoveStepDelayMs, ct);
        }

        _log.Information("滚轮: {Direction} x{Clicks} @ ({X},{Y}) [{Coord}] [hWnd=0x{Handle:X}]",
            direction, clicks, x, y, coord, hWnd);
    }

    public Task MoveToAsync(IntPtr hWnd, InputCoordinate coord, CancellationToken ct = default)
    {
        var (w, h) = GetClientSize(hWnd);
        var (x, y) = ResolveAndLog(coord, w, h);
        var lParam = User32.MakeLParam(x, y);

        PostMsg(hWnd, User32.WM_MOUSEMOVE, IntPtr.Zero, lParam);

        _log.Information("移动: ({X},{Y}) [{Coord}] [hWnd=0x{Handle:X}]", x, y, coord, hWnd);
        return Task.CompletedTask;
    }

    private (int width, int height) GetClientSize(IntPtr hWnd)
    {
        if (!User32.GetClientRect(hWnd, out RECT rect))
            throw new InvalidOperationException($"GetClientRect 失败, hWnd=0x{hWnd:X}");

        if (rect.Width <= 0 || rect.Height <= 0)
            throw new InvalidOperationException($"客户区尺寸无效: {rect.Width}x{rect.Height}, hWnd=0x{hWnd:X}");

        return (rect.Width, rect.Height);
    }

    private (int x, int y) ResolveAndLog(InputCoordinate coord, int clientWidth, int clientHeight)
    {
        var (x, y) = coord.ResolvePixel(_gridOverlay, clientWidth, clientHeight);
        _log.Debug("坐标解析: {Coord} → ({X},{Y}), 客户区={W}x{H}", coord, x, y, clientWidth, clientHeight);
        return (x, y);
    }

    private static (int screenX, int screenY) ClientToScreenCoord(IntPtr hWnd, int clientX, int clientY)
    {
        var point = new POINT(clientX, clientY);
        User32.ClientToScreen(hWnd, ref point);
        return (point.X, point.Y);
    }

    private void PostMsg(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        bool ok = User32.PostMessage(hWnd, msg, wParam, lParam);
        if (!ok)
            _log.Warning("PostMessage 失败: hWnd=0x{Handle:X}, msg=0x{Msg:X4}", hWnd, msg);
        else
            _log.Debug("PostMessage: hWnd=0x{Handle:X}, msg=0x{Msg:X4}, wParam=0x{W:X}, lParam=0x{L:X}",
                hWnd, msg, wParam, lParam);
    }
}

using SGMDTXTools.Core.Models;

namespace SGMDTXTools.Core.Services;

/// <summary>
/// 输入模拟接口，支持鼠标点击、双击、拖拽、滚轮、移动操作
/// </summary>
public interface IInputSimulator
{
    Task ClickAsync(IntPtr hWnd, InputCoordinate coord, MouseButton button = MouseButton.Left, CancellationToken ct = default);
    Task DoubleClickAsync(IntPtr hWnd, InputCoordinate coord, CancellationToken ct = default);
    Task DragAsync(IntPtr hWnd, InputCoordinate from, InputCoordinate to, CancellationToken ct = default);
    Task ScrollAsync(IntPtr hWnd, InputCoordinate coord, ScrollDirection direction, int clicks = 3, CancellationToken ct = default);
    Task MoveToAsync(IntPtr hWnd, InputCoordinate coord, CancellationToken ct = default);
}

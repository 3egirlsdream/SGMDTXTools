namespace SGMDTXTools.Core.Models;

/// <summary>
/// 输入模拟延迟与步数配置
/// </summary>
public class InputSimulatorConfig
{
    /// <summary>BUTTONDOWN → BUTTONUP 之间的延迟(ms)</summary>
    public int ClickDelayMs { get; set; } = 50;

    /// <summary>双击中两次点击之间的间隔(ms)</summary>
    public int DoubleClickIntervalMs { get; set; } = 80;

    /// <summary>每步 MOUSEMOVE 之间的延迟(ms)</summary>
    public int MoveStepDelayMs { get; set; } = 15;

    /// <summary>拖拽操作的线性插值步数</summary>
    public int DragStepCount { get; set; } = 20;

    /// <summary>操作前 MOUSEMOVE 后的等待(ms)</summary>
    public int PreActionDelayMs { get; set; } = 30;

    /// <summary>单次滚轮增量 (Windows WHEEL_DELTA = 120)</summary>
    public int ScrollDelta { get; set; } = 120;
}

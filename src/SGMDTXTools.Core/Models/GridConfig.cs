namespace SGMDTXTools.Core.Models;

/// <summary>
/// 网格叠加配置，用于在截图上绘制坐标网格，帮助LLM定位点击位置
/// </summary>
public class GridConfig
{
    /// <summary>列数（水平方向）A-J = 10列</summary>
    public int Columns { get; set; } = 10;

    /// <summary>行数（垂直方向）1-18 = 18行</summary>
    public int Rows { get; set; } = 18;

    /// <summary>网格线颜色 ARGB</summary>
    public uint LineColor { get; set; } = 0x80FF0000; // 半透明红

    /// <summary>标签文字颜色 ARGB</summary>
    public uint LabelColor { get; set; } = 0xFFFFFF00; // 黄色

    /// <summary>标签背景颜色 ARGB</summary>
    public uint LabelBgColor { get; set; } = 0xA0000000; // 半透明黑

    /// <summary>网格线宽度（像素）</summary>
    public float LineWidth { get; set; } = 1.0f;

    /// <summary>标签字号</summary>
    public float FontSize { get; set; } = 10.0f;

    /// <summary>
    /// 获取列标签 (A, B, C, ..., J, K, ...)
    /// </summary>
    public string GetColumnLabel(int col)
    {
        if (col < 0 || col >= Columns)
            throw new ArgumentOutOfRangeException(nameof(col), $"列索引超出范围: {col}, 有效范围: 0-{Columns - 1}");
        return ((char)('A' + col)).ToString();
    }

    /// <summary>
    /// 获取行标签 (1, 2, 3, ..., 18)
    /// </summary>
    public string GetRowLabel(int row)
    {
        if (row < 0 || row >= Rows)
            throw new ArgumentOutOfRangeException(nameof(row), $"行索引超出范围: {row}, 有效范围: 0-{Rows - 1}");
        return (row + 1).ToString();
    }

    /// <summary>
    /// 获取网格引用标签，如 "A1", "F8"
    /// </summary>
    public string GetGridRef(int col, int row)
    {
        return GetColumnLabel(col) + GetRowLabel(row);
    }

    /// <summary>
    /// 解析网格引用字符串为 (col, row) 索引
    /// </summary>
    public (int col, int row) ParseGridRef(string gridRef)
    {
        if (string.IsNullOrWhiteSpace(gridRef) || gridRef.Length < 2)
            throw new ArgumentException($"无效的网格引用: '{gridRef}'");

        char colChar = char.ToUpper(gridRef[0]);
        if (colChar < 'A' || colChar >= 'A' + Columns)
            throw new ArgumentException($"无效的列标识: '{colChar}', 有效范围: A-{(char)('A' + Columns - 1)}");

        string rowStr = gridRef.Substring(1);
        if (!int.TryParse(rowStr, out int rowNum) || rowNum < 1 || rowNum > Rows)
            throw new ArgumentException($"无效的行号: '{rowStr}', 有效范围: 1-{Rows}");

        return (colChar - 'A', rowNum - 1);
    }
}

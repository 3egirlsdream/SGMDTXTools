using SGMDTXTools.Core.Services;

namespace SGMDTXTools.Core.Models;

/// <summary>
/// 统一坐标抽象，支持像素坐标和网格引用两种模式
/// </summary>
public sealed class InputCoordinate
{
    public bool IsPixel { get; }
    public int PixelX { get; }
    public int PixelY { get; }
    public string GridRef { get; }

    private InputCoordinate(bool isPixel, int pixelX, int pixelY, string gridRef)
    {
        IsPixel = isPixel;
        PixelX = pixelX;
        PixelY = pixelY;
        GridRef = gridRef;
    }

    public static InputCoordinate FromPixel(int x, int y)
        => new(true, x, y, string.Empty);

    public static InputCoordinate FromGridRef(string gridRef)
    {
        if (string.IsNullOrWhiteSpace(gridRef))
            throw new ArgumentException("网格引用不能为空", nameof(gridRef));
        return new(false, 0, 0, gridRef.ToUpperInvariant());
    }

    /// <summary>
    /// 解析坐标字符串：包含逗号则为 "x,y" 像素坐标，否则为网格引用
    /// </summary>
    public static InputCoordinate Parse(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new ArgumentException("坐标输入不能为空", nameof(input));

        if (input.Contains(','))
        {
            var parts = input.Split(',', 2);
            if (!int.TryParse(parts[0].Trim(), out int x) || !int.TryParse(parts[1].Trim(), out int y))
                throw new ArgumentException($"无效的像素坐标格式: '{input}', 应为 'x,y' (如 100,200)");
            return FromPixel(x, y);
        }

        return FromGridRef(input);
    }

    /// <summary>
    /// 解析为最终像素坐标
    /// </summary>
    public (int x, int y) ResolvePixel(GridOverlay overlay, int clientWidth, int clientHeight)
    {
        if (IsPixel)
        {
            if (PixelX < 0 || PixelX >= clientWidth || PixelY < 0 || PixelY >= clientHeight)
                throw new ArgumentOutOfRangeException(
                    $"像素坐标 ({PixelX},{PixelY}) 超出客户区范围 ({clientWidth}x{clientHeight})");
            return (PixelX, PixelY);
        }

        var (px, py) = overlay.GridRefToPixel(GridRef, clientWidth, clientHeight);
        if (px < 0 || px >= clientWidth || py < 0 || py >= clientHeight)
            throw new ArgumentOutOfRangeException(
                $"网格引用 '{GridRef}' 解析的坐标 ({px},{py}) 超出客户区范围 ({clientWidth}x{clientHeight})");
        return (px, py);
    }

    public override string ToString()
        => IsPixel ? $"Pixel({PixelX},{PixelY})" : $"Grid({GridRef})";
}

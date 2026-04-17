using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using Serilog;
using SGMDTXTools.Core.Models;

namespace SGMDTXTools.Core.Services;

/// <summary>
/// 网格叠加服务：在截图上绘制坐标网格，并提供网格引用与像素坐标的互转
/// </summary>
public class GridOverlay
{
    private readonly ILogger _log;
    private readonly GridConfig _config;

    public GridOverlay(ILogger logger, GridConfig? config = null)
    {
        _log = logger.ForContext<GridOverlay>();
        _config = config ?? new GridConfig();
    }

    public GridConfig Config => _config;

    /// <summary>
    /// 在截图上绘制网格并保存为新文件
    /// </summary>
    public string DrawGridOnCapture(string inputPath, string outputDir)
    {
        if (!File.Exists(inputPath))
            throw new FileNotFoundException($"截图文件不存在: {inputPath}", inputPath);

        Directory.CreateDirectory(outputDir);

        using var bitmap = new Bitmap(inputPath);
        DrawGrid(bitmap);

        string fileName = Path.GetFileNameWithoutExtension(inputPath) + "_grid.png";
        string outputPath = Path.Combine(outputDir, fileName);
        bitmap.Save(outputPath, ImageFormat.Png);

        _log.Information("网格截图已保存: {Path}, 尺寸={W}x{H}, 网格={Cols}x{Rows}",
            outputPath, bitmap.Width, bitmap.Height, _config.Columns, _config.Rows);

        return outputPath;
    }

    /// <summary>
    /// 在截图PNG数据上绘制网格，返回带网格的PNG字节
    /// </summary>
    public byte[] DrawGridOnImage(byte[] pngData)
    {
        using var ms = new MemoryStream(pngData);
        using var bitmap = new Bitmap(ms);
        DrawGrid(bitmap);

        using var outMs = new MemoryStream();
        bitmap.Save(outMs, ImageFormat.Png);
        return outMs.ToArray();
    }

    /// <summary>
    /// 在CaptureResult上叠加网格，返回新的CaptureResult（原始结果不变）
    /// </summary>
    public CaptureResult DrawGridOnCapture(CaptureResult capture)
    {
        byte[] gridImage = DrawGridOnImage(capture.ImageData);
        return new CaptureResult
        {
            ImageData = gridImage,
            Width = capture.Width,
            Height = capture.Height,
            CapturedAt = capture.CapturedAt,
            CaptureTimeMs = capture.CaptureTimeMs,
            SavedPath = null
        };
    }

    /// <summary>
    /// 将网格引用（如 "F8"）转为像素中心坐标
    /// </summary>
    public (int pixelX, int pixelY) GridRefToPixel(string gridRef, int imageWidth, int imageHeight)
    {
        var (col, row) = _config.ParseGridRef(gridRef);

        float cellWidth = (float)imageWidth / _config.Columns;
        float cellHeight = (float)imageHeight / _config.Rows;

        int pixelX = (int)(col * cellWidth + cellWidth / 2);
        int pixelY = (int)(row * cellHeight + cellHeight / 2);

        _log.Debug("网格引用转像素: {Ref} -> ({X},{Y}), 图片尺寸={W}x{H}, 单元格={CW:F1}x{CH:F1}",
            gridRef, pixelX, pixelY, imageWidth, imageHeight, cellWidth, cellHeight);

        return (pixelX, pixelY);
    }

    /// <summary>
    /// 将像素坐标转为网格引用
    /// </summary>
    public string PixelToGridRef(int pixelX, int pixelY, int imageWidth, int imageHeight)
    {
        float cellWidth = (float)imageWidth / _config.Columns;
        float cellHeight = (float)imageHeight / _config.Rows;

        int col = Math.Clamp((int)(pixelX / cellWidth), 0, _config.Columns - 1);
        int row = Math.Clamp((int)(pixelY / cellHeight), 0, _config.Rows - 1);

        string gridRef = _config.GetGridRef(col, row);

        _log.Debug("像素转网格引用: ({X},{Y}) -> {Ref}, 图片尺寸={W}x{H}",
            pixelX, pixelY, gridRef, imageWidth, imageHeight);

        return gridRef;
    }

    /// <summary>
    /// 获取指定网格单元格的边界矩形（像素坐标）
    /// </summary>
    public Rectangle GetCellBounds(string gridRef, int imageWidth, int imageHeight)
    {
        var (col, row) = _config.ParseGridRef(gridRef);

        float cellWidth = (float)imageWidth / _config.Columns;
        float cellHeight = (float)imageHeight / _config.Rows;

        int x = (int)(col * cellWidth);
        int y = (int)(row * cellHeight);
        int w = (int)((col + 1) * cellWidth) - x;
        int h = (int)((row + 1) * cellHeight) - y;

        return new Rectangle(x, y, w, h);
    }

    private void DrawGrid(Bitmap bitmap)
    {
        int width = bitmap.Width;
        int height = bitmap.Height;

        float cellWidth = (float)width / _config.Columns;
        float cellHeight = (float)height / _config.Rows;

        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        using var linePen = new Pen(Color.FromArgb((int)_config.LineColor), _config.LineWidth);
        using var labelFont = new Font("Consolas", _config.FontSize, FontStyle.Bold);
        using var labelBrush = new SolidBrush(Color.FromArgb((int)_config.LabelColor));
        using var bgBrush = new SolidBrush(Color.FromArgb((int)_config.LabelBgColor));

        // 绘制垂直线
        for (int col = 0; col <= _config.Columns; col++)
        {
            float x = col * cellWidth;
            graphics.DrawLine(linePen, x, 0, x, height);
        }

        // 绘制水平线
        for (int row = 0; row <= _config.Rows; row++)
        {
            float y = row * cellHeight;
            graphics.DrawLine(linePen, 0, y, width, y);
        }

        // 绘制标签
        for (int col = 0; col < _config.Columns; col++)
        {
            for (int row = 0; row < _config.Rows; row++)
            {
                string label = _config.GetGridRef(col, row);
                float x = col * cellWidth + 2;
                float y = row * cellHeight + 1;

                var textSize = graphics.MeasureString(label, labelFont);
                var bgRect = new RectangleF(x, y, textSize.Width + 2, textSize.Height);

                graphics.FillRectangle(bgBrush, bgRect);
                graphics.DrawString(label, labelFont, labelBrush, x + 1, y);
            }
        }

        _log.Debug("网格绘制完成: 图片={W}x{H}, 网格={Cols}x{Rows}, 单元格={CW:F1}x{CH:F1}",
            width, height, _config.Columns, _config.Rows, cellWidth, cellHeight);
    }
}

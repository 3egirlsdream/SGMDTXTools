using OpenCvSharp;
using Sdcb.PaddleInference;
using Sdcb.PaddleOCR;
using Sdcb.PaddleOCR.Models;
using Sdcb.PaddleOCR.Models.Local;
using Serilog;
using SGMDTXTools.Core.Models;

namespace SGMDTXTools.Core.Services;

public class OcrEngine : IDisposable
{
    private readonly ILogger _log;
    private PaddleOcrAll? _ocr;
    private bool _ready;
    private bool _disposed;
    private readonly object _lock = new();

    public OcrEngine(ILogger logger)
    {
        _log = logger.ForContext<OcrEngine>();
    }

    public bool IsReady => _ready;

    public void Initialize()
    {
        lock (_lock)
        {
            if (_ready) return;

            _log.Information("初始化 PaddleOCR (ChineseV4, MKLDNN)...");
            try
            {
                FullOcrModel model = LocalFullModels.ChineseV4;
                _ocr = new PaddleOcrAll(model, PaddleDevice.Mkldnn())
                {
                    AllowRotateDetection = true,
                    Enable180Classification = false
                };
                _ready = true;
                _log.Information("PaddleOCR 初始化成功");
            }
            catch (Exception ex)
            {
                _log.Error(ex, "PaddleOCR 初始化失败");
                throw;
            }
        }
    }

    public List<TextItem> Detect(Mat image, double minConfidence = 0.5)
    {
        if (_ocr == null)
            throw new InvalidOperationException("OCR 引擎未初始化，请先调用 Initialize()");

        var items = new List<TextItem>();

        // 确保是 BGR 3 通道
        using var bgr = EnsureBgr(image);

        PaddleOcrResult result;
        lock (_lock)
        {
            result = _ocr.Run(bgr);
        }

        foreach (var region in result.Regions)
        {
            if (region.Score < minConfidence) continue;

            // RotatedRect → BoundingBox
            var rect = region.Rect;
            var points = rect.Points();
            int xMin = (int)Math.Floor(points.Min(p => p.X));
            int yMin = (int)Math.Floor(points.Min(p => p.Y));
            int xMax = (int)Math.Ceiling(points.Max(p => p.X));
            int yMax = (int)Math.Ceiling(points.Max(p => p.Y));

            // 边界裁剪
            xMin = Math.Max(0, xMin);
            yMin = Math.Max(0, yMin);
            xMax = Math.Min(image.Cols, xMax);
            yMax = Math.Min(image.Rows, yMax);

            int w = xMax - xMin;
            int h = yMax - yMin;

            items.Add(new TextItem
            {
                Text = region.Text,
                Confidence = Math.Round(region.Score, 4),
                Bbox = new BoundingBox { X = xMin, Y = yMin, Width = w, Height = h },
                Center = new PointCoord { X = xMin + w / 2, Y = yMin + h / 2 }
            });
        }

        return items;
    }

    public List<TextItem> DetectRegion(Mat image, int x, int y, int w, int h,
        double minConfidence = 0.5)
    {
        using var cropped = new Mat(image, new Rect(x, y, w, h));
        var items = Detect(cropped, minConfidence);

        // 偏移回原图坐标
        foreach (var item in items)
        {
            item.Bbox.X += x;
            item.Bbox.Y += y;
            item.Center.X += x;
            item.Center.Y += y;
        }

        return items;
    }

    private static Mat EnsureBgr(Mat image)
    {
        if (image.Channels() == 3)
            return image.Clone();

        if (image.Channels() == 4)
        {
            var bgr = new Mat();
            Cv2.CvtColor(image, bgr, ColorConversionCodes.BGRA2BGR);
            return bgr;
        }

        if (image.Channels() == 1)
        {
            var bgr = new Mat();
            Cv2.CvtColor(image, bgr, ColorConversionCodes.GRAY2BGR);
            return bgr;
        }

        return image.Clone();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _ocr?.Dispose();
    }
}

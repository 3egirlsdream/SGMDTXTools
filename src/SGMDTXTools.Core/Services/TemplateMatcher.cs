using OpenCvSharp;
using Serilog;
using SGMDTXTools.Core.Models;

namespace SGMDTXTools.Core.Services;

public class TemplateMatcher : IDisposable
{
    private readonly ILogger _log;
    private readonly TemplateStore _store;
    private readonly Dictionary<string, Mat> _cache = new();
    private bool _disposed;

    private static readonly double[] Scales = [0.85, 0.9, 0.95, 1.0, 1.05, 1.1, 1.15];

    public TemplateMatcher(ILogger logger, TemplateStore store)
    {
        _log = logger.ForContext<TemplateMatcher>();
        _store = store;
        LoadImages();
    }

    private void LoadImages()
    {
        DisposeCache();

        foreach (var info in _store.ListAll())
        {
            var imgPath = _store.GetImagePath(info.Name);
            if (imgPath == null) continue;

            var img = Cv2.ImRead(imgPath, ImreadModes.Color);
            if (img.Empty())
            {
                _log.Warning("无法加载模板图片: {Path}", imgPath);
                img.Dispose();
                continue;
            }
            _cache[info.Name] = img;
        }

        _log.Information("加载了 {Count} 个模板图片到缓存", _cache.Count);
    }

    public void Reload()
    {
        _store.Reload();
        LoadImages();
    }

    public List<MatchItem> MatchAll(Mat image)
    {
        var results = new List<MatchItem>();
        foreach (var info in _store.ListAll())
        {
            if (!_cache.TryGetValue(info.Name, out var tmpl)) continue;
            results.AddRange(MatchTemplate(image, tmpl, info.Name, info.Threshold));
        }
        return results;
    }

    public List<MatchItem> Match(Mat image, string[] templateNames)
    {
        var results = new List<MatchItem>();
        foreach (var name in templateNames)
        {
            var info = _store.Get(name);
            if (info == null) continue;
            if (!_cache.TryGetValue(name, out var tmpl)) continue;
            results.AddRange(MatchTemplate(image, tmpl, name, info.Threshold));
        }
        return results;
    }

    private List<MatchItem> MatchTemplate(Mat image, Mat template, string name, double threshold)
    {
        int th = template.Rows, tw = template.Cols;
        int ih = image.Rows, iw = image.Cols;

        var allBoxes = new List<(int X, int Y, int W, int H, double Conf)>();

        foreach (var scale in Scales)
        {
            int sw = (int)(tw * scale);
            int sh = (int)(th * scale);
            if (sw <= 0 || sh <= 0 || sw > iw || sh > ih) continue;

            Mat scaled;
            if (Math.Abs(scale - 1.0) < 0.001)
            {
                scaled = template;
            }
            else
            {
                scaled = new Mat();
                Cv2.Resize(template, scaled, new Size(sw, sh), interpolation: InterpolationFlags.Linear);
            }

            using var result = new Mat();
            Cv2.MatchTemplate(image, scaled, result, TemplateMatchModes.CCoeffNormed);

            // 找到所有超过阈值的位置
            for (int row = 0; row < result.Rows; row++)
            {
                for (int col = 0; col < result.Cols; col++)
                {
                    float conf = result.At<float>(row, col);
                    if (conf >= threshold)
                    {
                        allBoxes.Add((col, row, sw, sh, conf));
                    }
                }
            }

            if (Math.Abs(scale - 1.0) >= 0.001)
                scaled.Dispose();
        }

        var filtered = Nms(allBoxes, 0.3);

        var items = new List<MatchItem>();
        foreach (var (bx, by, bw, bh, conf) in filtered)
        {
            items.Add(new MatchItem
            {
                Template = name,
                Confidence = Math.Round(conf, 4),
                Bbox = new BoundingBox { X = bx, Y = by, Width = bw, Height = bh },
                Center = new PointCoord { X = bx + bw / 2, Y = by + bh / 2 }
            });
        }
        return items;
    }

    private static List<(int X, int Y, int W, int H, double Conf)> Nms(
        List<(int X, int Y, int W, int H, double Conf)> boxes, double iouThreshold)
    {
        if (boxes.Count == 0) return [];

        var sorted = boxes.OrderByDescending(b => b.Conf).ToList();
        var keep = new List<(int X, int Y, int W, int H, double Conf)>();

        while (sorted.Count > 0)
        {
            var best = sorted[0];
            sorted.RemoveAt(0);
            keep.Add(best);

            sorted.RemoveAll(box => Iou(best, box) >= iouThreshold);
        }

        return keep;
    }

    private static double Iou(
        (int X, int Y, int W, int H, double Conf) a,
        (int X, int Y, int W, int H, double Conf) b)
    {
        int ax2 = a.X + a.W, ay2 = a.Y + a.H;
        int bx2 = b.X + b.W, by2 = b.Y + b.H;

        int ix1 = Math.Max(a.X, b.X);
        int iy1 = Math.Max(a.Y, b.Y);
        int ix2 = Math.Min(ax2, bx2);
        int iy2 = Math.Min(ay2, by2);

        if (ix2 <= ix1 || iy2 <= iy1) return 0.0;

        double inter = (ix2 - ix1) * (iy2 - iy1);
        double union = (double)a.W * a.H + (double)b.W * b.H - inter;
        return union > 0 ? inter / union : 0.0;
    }

    private void DisposeCache()
    {
        foreach (var mat in _cache.Values)
            mat.Dispose();
        _cache.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DisposeCache();
    }
}

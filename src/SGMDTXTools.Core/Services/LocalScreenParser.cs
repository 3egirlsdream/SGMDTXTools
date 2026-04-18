using System.Diagnostics;
using OpenCvSharp;
using Serilog;
using SGMDTXTools.Core.Models;

namespace SGMDTXTools.Core.Services;

/// <summary>
/// 本地 C# 实现的屏幕感知服务，替代 Python HTTP 服务。
/// 内部使用 OcrEngine (PaddleOCR) + TemplateMatcher (OpenCvSharp4)。
/// </summary>
public class LocalScreenParser : IScreenParser
{
    private readonly ILogger _log;
    private readonly OcrEngine _ocrEngine;
    private readonly TemplateMatcher _matcher;
    private bool _disposed;

    public LocalScreenParser(ILogger logger, OcrEngine ocrEngine, TemplateMatcher matcher)
    {
        _log = logger.ForContext<LocalScreenParser>();
        _ocrEngine = ocrEngine;
        _matcher = matcher;
    }

    public Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        return Task.FromResult(_ocrEngine.IsReady);
    }

    public Task<OcrResult> OcrAsync(string imagePath, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            var sw = Stopwatch.StartNew();
            try
            {
                using var image = Cv2.ImRead(imagePath, ImreadModes.Color);
                if (image.Empty())
                    return new OcrResult { Success = false, Error = $"无法读取图片: {imagePath}" };

                var texts = _ocrEngine.Detect(image);
                sw.Stop();

                return new OcrResult
                {
                    Success = true,
                    ElapsedMs = (int)sw.ElapsedMilliseconds,
                    ImageSize = new ImageSize { Width = image.Cols, Height = image.Rows },
                    Texts = texts
                };
            }
            catch (Exception ex)
            {
                _log.Error(ex, "OCR 失败: {Path}", imagePath);
                return new OcrResult { Success = false, Error = ex.Message, ElapsedMs = (int)sw.ElapsedMilliseconds };
            }
        }, ct);
    }

    public Task<OcrResult> OcrRegionAsync(string imagePath, int x, int y, int width, int height,
        CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            var sw = Stopwatch.StartNew();
            try
            {
                using var image = Cv2.ImRead(imagePath, ImreadModes.Color);
                if (image.Empty())
                    return new OcrResult { Success = false, Error = $"无法读取图片: {imagePath}" };

                var texts = _ocrEngine.DetectRegion(image, x, y, width, height);
                sw.Stop();

                return new OcrResult
                {
                    Success = true,
                    ElapsedMs = (int)sw.ElapsedMilliseconds,
                    ImageSize = new ImageSize { Width = image.Cols, Height = image.Rows },
                    Texts = texts
                };
            }
            catch (Exception ex)
            {
                _log.Error(ex, "区域 OCR 失败: {Path} ({X},{Y},{W},{H})", imagePath, x, y, width, height);
                return new OcrResult { Success = false, Error = ex.Message, ElapsedMs = (int)sw.ElapsedMilliseconds };
            }
        }, ct);
    }

    public Task<MatchResult> MatchAsync(string imagePath, string[]? templates = null,
        CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            var sw = Stopwatch.StartNew();
            try
            {
                using var image = Cv2.ImRead(imagePath, ImreadModes.Color);
                if (image.Empty())
                    return new MatchResult { Success = false, Error = $"无法读取图片: {imagePath}" };

                var matches = templates is { Length: > 0 }
                    ? _matcher.Match(image, templates)
                    : _matcher.MatchAll(image);
                sw.Stop();

                return new MatchResult
                {
                    Success = true,
                    ElapsedMs = (int)sw.ElapsedMilliseconds,
                    Matches = matches
                };
            }
            catch (Exception ex)
            {
                _log.Error(ex, "模板匹配失败: {Path}", imagePath);
                return new MatchResult { Success = false, Error = ex.Message, ElapsedMs = (int)sw.ElapsedMilliseconds };
            }
        }, ct);
    }

    public Task<ScanResult> ScanAsync(string imagePath, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            var sw = Stopwatch.StartNew();
            try
            {
                using var image = Cv2.ImRead(imagePath, ImreadModes.Color);
                if (image.Empty())
                    return new ScanResult { Success = false, Error = $"无法读取图片: {imagePath}" };

                // 并行执行 OCR 和模板匹配
                List<TextItem>? texts = null;
                List<MatchItem>? matches = null;
                Exception? ocrEx = null, matchEx = null;

                Parallel.Invoke(
                    () =>
                    {
                        try { texts = _ocrEngine.Detect(image); }
                        catch (Exception ex) { ocrEx = ex; }
                    },
                    () =>
                    {
                        try { matches = _matcher.MatchAll(image); }
                        catch (Exception ex) { matchEx = ex; }
                    }
                );

                sw.Stop();

                if (ocrEx != null)
                    _log.Warning(ocrEx, "Scan 中 OCR 失败，继续返回模板匹配结果");
                if (matchEx != null)
                    _log.Warning(matchEx, "Scan 中模板匹配失败，继续返回 OCR 结果");

                return new ScanResult
                {
                    Success = true,
                    ElapsedMs = (int)sw.ElapsedMilliseconds,
                    ImageSize = new ImageSize { Width = image.Cols, Height = image.Rows },
                    Texts = texts ?? [],
                    Matches = matches ?? []
                };
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Scan 失败: {Path}", imagePath);
                return new ScanResult { Success = false, Error = ex.Message, ElapsedMs = (int)sw.ElapsedMilliseconds };
            }
        }, ct);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // OcrEngine 和 TemplateMatcher 由外部管理生命周期
    }
}

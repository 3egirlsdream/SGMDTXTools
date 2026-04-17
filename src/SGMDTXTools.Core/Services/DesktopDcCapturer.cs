using System.Drawing;
using System.Drawing.Imaging;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Serilog;
using SGMDTXTools.Core.Models;
using SGMDTXTools.Core.Native;

namespace SGMDTXTools.Core.Services;

public class DesktopDcCapturer : IScreenCapturer
{
    private readonly ILogger _log;
    private readonly ProcessWindowLocator _locator;

    public DesktopDcCapturer(ILogger logger, ProcessWindowLocator locator)
    {
        _log = logger.ForContext<DesktopDcCapturer>();
        _locator = locator;
    }

    public CaptureResult Capture(WindowInfo window)
    {
        if (!User32.IsWindow(window.Handle))
        {
            _log.Error("窗口句柄无效: Handle=0x{Handle:X}", window.Handle);
            throw new InvalidOperationException($"窗口句柄无效: 0x{window.Handle:X}");
        }

        // 刷新窗口位置
        var refreshed = _locator.RefreshWindowInfo(window.Handle);
        if (refreshed == null)
        {
            _log.Error("无法刷新窗口信息: Handle=0x{Handle:X}", window.Handle);
            throw new InvalidOperationException($"无法刷新窗口信息: 0x{window.Handle:X}");
        }

        int x = refreshed.X;
        int y = refreshed.Y;
        int width = refreshed.Width;
        int height = refreshed.Height;

        if (width <= 0 || height <= 0)
        {
            _log.Error("窗口尺寸无效: {W}x{H}", width, height);
            throw new InvalidOperationException($"窗口尺寸无效: {width}x{height}");
        }

        _log.Debug("开始截图: 窗口=0x{Handle:X}, 区域=({X},{Y},{W}x{H})", window.Handle, x, y, width, height);

        var sw = Stopwatch.StartNew();
        IntPtr screenDc = IntPtr.Zero;
        IntPtr memDc = IntPtr.Zero;
        IntPtr hBitmap = IntPtr.Zero;
        IntPtr oldObj = IntPtr.Zero;

        try
        {
            // 1. 获取桌面DC（非游戏窗口DC）
            screenDc = User32.GetDC(IntPtr.Zero);
            if (screenDc == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                _log.Error("GetDC(NULL)失败: Win32Error={Error}", err);
                throw new InvalidOperationException($"GetDC(NULL)失败: Win32Error={err}");
            }

            // 2. 创建内存DC
            memDc = Gdi32.CreateCompatibleDC(screenDc);
            if (memDc == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                _log.Error("CreateCompatibleDC失败: Win32Error={Error}", err);
                throw new InvalidOperationException($"CreateCompatibleDC失败: Win32Error={err}");
            }

            // 3. 创建兼容位图
            hBitmap = Gdi32.CreateCompatibleBitmap(screenDc, width, height);
            if (hBitmap == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                _log.Error("CreateCompatibleBitmap失败: Win32Error={Error}", err);
                throw new InvalidOperationException($"CreateCompatibleBitmap失败: Win32Error={err}");
            }

            // 4. 选入位图
            oldObj = Gdi32.SelectObject(memDc, hBitmap);

            // 5. BitBlt: 从桌面DC的游戏客户区坐标处复制像素
            bool success = Gdi32.BitBlt(memDc, 0, 0, width, height,
                screenDc, x, y, Gdi32.SRCCOPY);

            if (!success)
            {
                int err = Marshal.GetLastWin32Error();
                _log.Error("BitBlt失败: Win32Error={Error}", err);
                throw new InvalidOperationException($"BitBlt失败: Win32Error={err}");
            }

            sw.Stop();
            _log.Debug("BitBlt完成, 耗时={ElapsedMs}ms", sw.ElapsedMilliseconds);

            // 6. 转换为Bitmap并编码PNG
            Gdi32.SelectObject(memDc, oldObj);
            oldObj = IntPtr.Zero;

            using var bitmap = Image.FromHbitmap(hBitmap);
            byte[] pngData = EncodePng(bitmap);

            // 7. 黑屏检测
            CheckForBlackScreen(bitmap, width, height);

            var result = new CaptureResult
            {
                ImageData = pngData,
                Width = width,
                Height = height,
                CapturedAt = DateTime.Now,
                CaptureTimeMs = sw.ElapsedMilliseconds
            };

            _log.Information("截图完成: 尺寸={W}x{H}, 大小={Size}KB, 耗时={Ms}ms",
                width, height, pngData.Length / 1024, sw.ElapsedMilliseconds);

            return result;
        }
        finally
        {
            // 严格按反序清理GDI资源
            if (oldObj != IntPtr.Zero)
            {
                Gdi32.SelectObject(memDc, oldObj);
            }
            if (hBitmap != IntPtr.Zero)
            {
                if (!Gdi32.DeleteObject(hBitmap))
                    _log.Warning("DeleteObject(hBitmap)失败");
            }
            if (memDc != IntPtr.Zero)
            {
                if (!Gdi32.DeleteDC(memDc))
                    _log.Warning("DeleteDC(memDc)失败");
            }
            if (screenDc != IntPtr.Zero)
            {
                if (User32.ReleaseDC(IntPtr.Zero, screenDc) == 0)
                    _log.Warning("ReleaseDC(screenDc)失败");
            }
        }
    }

    public string CaptureToFile(WindowInfo window, string outputDir)
    {
        Directory.CreateDirectory(outputDir);

        var result = Capture(window);
        string fileName = $"capture_{result.CapturedAt:yyyyMMdd_HHmmss_fff}.png";
        string filePath = Path.Combine(outputDir, fileName);

        File.WriteAllBytes(filePath, result.ImageData);
        result.SavedPath = filePath;

        _log.Information("截图保存: {Path}, 尺寸={W}x{H}, 耗时={Ms}ms",
            filePath, result.Width, result.Height, result.CaptureTimeMs);

        return filePath;
    }

    public async Task StartPeriodicCapture(WindowInfo window, TimeSpan interval, string outputDir, CancellationToken ct)
    {
        _log.Information("开始定时截图: 间隔={Interval}s, 输出={Dir}", interval.TotalSeconds, outputDir);
        Directory.CreateDirectory(outputDir);

        int successCount = 0;
        int failCount = 0;
        int consecutiveErrors = 0;
        const int maxConsecutiveErrors = 5;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var path = CaptureToFile(window, outputDir);
                successCount++;
                consecutiveErrors = 0;
                _log.Debug("定时截图 #{Count}: {Path}", successCount, path);
            }
            catch (Exception ex)
            {
                failCount++;
                consecutiveErrors++;
                _log.Error(ex, "定时截图失败 (连续失败{Count}次)", consecutiveErrors);

                if (consecutiveErrors >= maxConsecutiveErrors)
                {
                    _log.Error("连续失败{Count}次, 停止定时截图", maxConsecutiveErrors);
                    throw;
                }
            }

            try
            {
                await Task.Delay(interval, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _log.Information("停止定时截图: 成功={Success}, 失败={Fail}", successCount, failCount);
    }

    public void Dispose()
    {
        // 当前实现无需持有长期资源，每次Capture都在finally中清理
    }

    private byte[] EncodePng(Bitmap bitmap)
    {
        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    private void CheckForBlackScreen(Bitmap bitmap, int width, int height)
    {
        const int sampleCount = 20;
        int blackCount = 0;
        var rng = new Random();

        for (int i = 0; i < sampleCount; i++)
        {
            int px = rng.Next(width);
            int py = rng.Next(height);
            var pixel = bitmap.GetPixel(px, py);

            if (pixel.R < 5 && pixel.G < 5 && pixel.B < 5)
                blackCount++;
        }

        double blackRatio = (double)blackCount / sampleCount;
        if (blackRatio > 0.95)
        {
            _log.Warning("截图疑似黑屏: 像素采样显示{Ratio:P0}为黑色, 可能截图异常", blackRatio);
        }
    }
}

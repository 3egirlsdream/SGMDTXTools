using System.Net;
using System.Text;
using System.Text.Json;
using Serilog;
using SGMDTXTools.Core.Models;

namespace SGMDTXTools.Core.Services;

/// <summary>
/// 轻量 HTTP API，暴露截屏/输入/感知能力给 skill 脚本调用
/// </summary>
public class CommandHttpServer : IDisposable
{
    private readonly ILogger _log;
    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private Task? _listenTask;
    private bool _disposed;

    // 服务依赖
    private readonly ProcessWindowLocator _locator;
    private readonly DesktopDcCapturer _capturer;
    private readonly SendInputSimulator _inputSimulator;
    private readonly GridOverlay _gridOverlay;
    private readonly WindowResizer _windowResizer;
    private readonly IScreenParser? _screenParser;

    // 配置
    private readonly string _processName;
    private readonly string _screenshotDir;
    private int _targetWidth;
    private int _targetHeight;
    private readonly int _port;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false
    };

    public CommandHttpServer(
        ILogger logger,
        int port,
        string processName,
        string screenshotDir,
        int targetWidth,
        int targetHeight,
        ProcessWindowLocator locator,
        DesktopDcCapturer capturer,
        SendInputSimulator inputSimulator,
        GridOverlay gridOverlay,
        WindowResizer windowResizer,
        IScreenParser? screenParser)
    {
        _log = logger.ForContext<CommandHttpServer>();
        _port = port;
        _processName = processName;
        _screenshotDir = screenshotDir;
        _targetWidth = targetWidth;
        _targetHeight = targetHeight;
        _locator = locator;
        _capturer = capturer;
        _inputSimulator = inputSimulator;
        _gridOverlay = gridOverlay;
        _windowResizer = windowResizer;
        _screenParser = screenParser;

        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://+:{_port}/");
    }

    public void UpdateTargetSize(int width, int height)
    {
        _targetWidth = width;
        _targetHeight = height;
    }

    public void Start()
    {
        try
        {
            _listener.Start();
        }
        catch (HttpListenerException)
        {
            // 无管理员权限时回退到 localhost
            _listener.Prefixes.Clear();
            _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
            _listener.Start();
        }

        _listenTask = Task.Run(() => ListenLoop(_cts.Token));
        _log.Information("HTTP API 已启动: http://127.0.0.1:{Port}/", _port);
    }

    public void Stop()
    {
        _cts.Cancel();
        _listener.Stop();
        _listenTask?.Wait(TimeSpan.FromSeconds(3));
        _log.Information("HTTP API 已停止");
    }

    private async Task ListenLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await _listener.GetContextAsync().WaitAsync(ct);
            }
            catch (OperationCanceledException) { break; }
            catch (HttpListenerException) { break; }
            catch (ObjectDisposedException) { break; }

            _ = Task.Run(async () =>
            {
                try
                {
                    await HandleRequest(ctx);
                }
                catch (Exception ex)
                {
                    _log.Error(ex, "HTTP 请求处理异常: {Path}", ctx.Request.Url?.AbsolutePath);
                    await WriteJson(ctx.Response, 500, new { error = ex.Message });
                }
            }, CancellationToken.None);
        }
    }

    private async Task HandleRequest(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var path = req.Url?.AbsolutePath?.TrimEnd('/') ?? "";
        var method = req.HttpMethod.ToUpperInvariant();

        _log.Debug("HTTP {Method} {Path}", method, path);

        switch (path)
        {
            case "/api/health":
                await HandleHealth(ctx);
                break;
            case "/api/capture":
                await HandleCapture(ctx);
                break;
            case "/api/click":
                await HandleClick(ctx);
                break;
            case "/api/dblclick":
                await HandleDblClick(ctx);
                break;
            case "/api/drag":
                await HandleDrag(ctx);
                break;
            case "/api/scroll":
                await HandleScroll(ctx);
                break;
            case "/api/scan":
                await HandleScan(ctx);
                break;
            case "/api/ocr":
                await HandleOcr(ctx);
                break;
            case "/api/match":
                await HandleMatch(ctx);
                break;
            case "/api/resize":
                await HandleResize(ctx);
                break;
            default:
                await WriteJson(ctx.Response, 404, new { error = $"未知路径: {path}" });
                break;
        }
    }

    // ── GET /api/health ──

    private async Task HandleHealth(HttpListenerContext ctx)
    {
        var window = _locator.FindWindow(_processName);
        bool parserReady = _screenParser != null && await _screenParser.IsAvailableAsync();
        await WriteJson(ctx.Response, 200, new
        {
            status = "ok",
            process = _processName,
            window_found = window != null,
            window_size = window != null ? $"{window.Width}x{window.Height}" : null,
            target_size = $"{_targetWidth}x{_targetHeight}",
            ocr_ready = parserReady
        });
    }

    // ── POST /api/capture ──

    private async Task HandleCapture(HttpListenerContext ctx)
    {
        var window = FindAndResizeWindow();
        if (window == null)
        {
            await WriteJson(ctx.Response, 404, new { error = $"未找到进程 '{_processName}' 的窗口" });
            return;
        }

        string path = _capturer.CaptureToFile(window, _screenshotDir);
        await WriteJson(ctx.Response, 200, new
        {
            path = path,
            width = window.Width,
            height = window.Height
        });
    }

    // ── POST /api/click ──

    private async Task HandleClick(HttpListenerContext ctx)
    {
        var body = await ReadBody(ctx.Request);
        var x = GetString(body, "x");
        var y = GetString(body, "y");
        if (x == null || y == null)
        {
            await WriteJson(ctx.Response, 400, new { error = "缺少参数 x, y" });
            return;
        }

        var window = FindAndResizeWindow();
        if (window == null)
        {
            await WriteJson(ctx.Response, 404, new { error = $"未找到进程 '{_processName}' 的窗口" });
            return;
        }

        var coord = InputCoordinate.Parse($"{x},{y}");
        var buttonStr = GetString(body, "button") ?? "left";
        var button = buttonStr.Equals("right", StringComparison.OrdinalIgnoreCase)
            ? MouseButton.Right : MouseButton.Left;

        await _inputSimulator.ClickAsync(window.Handle, coord, button);
        await WriteJson(ctx.Response, 200, new { action = "click", x, y, button = buttonStr });
    }

    // ── POST /api/dblclick ──

    private async Task HandleDblClick(HttpListenerContext ctx)
    {
        var body = await ReadBody(ctx.Request);
        var x = GetString(body, "x");
        var y = GetString(body, "y");
        if (x == null || y == null)
        {
            await WriteJson(ctx.Response, 400, new { error = "缺少参数 x, y" });
            return;
        }

        var window = FindAndResizeWindow();
        if (window == null)
        {
            await WriteJson(ctx.Response, 404, new { error = $"未找到进程 '{_processName}' 的窗口" });
            return;
        }

        var coord = InputCoordinate.Parse($"{x},{y}");
        await _inputSimulator.DoubleClickAsync(window.Handle, coord);
        await WriteJson(ctx.Response, 200, new { action = "dblclick", x, y });
    }

    // ── POST /api/drag ──

    private async Task HandleDrag(HttpListenerContext ctx)
    {
        var body = await ReadBody(ctx.Request);
        var x1 = GetString(body, "x1");
        var y1 = GetString(body, "y1");
        var x2 = GetString(body, "x2");
        var y2 = GetString(body, "y2");
        if (x1 == null || y1 == null || x2 == null || y2 == null)
        {
            await WriteJson(ctx.Response, 400, new { error = "缺少参数 x1, y1, x2, y2" });
            return;
        }

        var window = FindAndResizeWindow();
        if (window == null)
        {
            await WriteJson(ctx.Response, 404, new { error = $"未找到进程 '{_processName}' 的窗口" });
            return;
        }

        var from = InputCoordinate.Parse($"{x1},{y1}");
        var to = InputCoordinate.Parse($"{x2},{y2}");
        await _inputSimulator.DragAsync(window.Handle, from, to);
        await WriteJson(ctx.Response, 200, new { action = "drag", from = new[] { x1, y1 }, to = new[] { x2, y2 } });
    }

    // ── POST /api/scroll ──

    private async Task HandleScroll(HttpListenerContext ctx)
    {
        var body = await ReadBody(ctx.Request);
        var x = GetString(body, "x");
        var y = GetString(body, "y");
        var dirStr = GetString(body, "direction");
        if (x == null || y == null || dirStr == null)
        {
            await WriteJson(ctx.Response, 400, new { error = "缺少参数 x, y, direction" });
            return;
        }

        if (!Enum.TryParse<ScrollDirection>(dirStr, true, out var direction))
        {
            await WriteJson(ctx.Response, 400, new { error = $"无效方向: '{dirStr}', 应为 up 或 down" });
            return;
        }

        var window = FindAndResizeWindow();
        if (window == null)
        {
            await WriteJson(ctx.Response, 404, new { error = $"未找到进程 '{_processName}' 的窗口" });
            return;
        }

        int clicks = GetInt(body, "clicks") ?? 3;
        var coord = InputCoordinate.Parse($"{x},{y}");
        await _inputSimulator.ScrollAsync(window.Handle, coord, direction, clicks);
        await WriteJson(ctx.Response, 200, new { action = "scroll", x, y, direction = dirStr, clicks });
    }

    // ── POST /api/scan ──

    private async Task HandleScan(HttpListenerContext ctx)
    {
        if (_screenParser == null)
        {
            await WriteJson(ctx.Response, 503, new { error = "感知服务未配置" });
            return;
        }

        var window = FindAndResizeWindow();
        if (window == null)
        {
            await WriteJson(ctx.Response, 404, new { error = $"未找到进程 '{_processName}' 的窗口" });
            return;
        }

        string imagePath = _capturer.CaptureToFile(window, _screenshotDir);
        try
        {
            var result = await _screenParser.ScanAsync(imagePath);
            await WriteJsonRaw(ctx.Response, 200, JsonSerializer.Serialize(result, JsonOptions));
        }
        finally
        {
            TryDeleteFile(imagePath);
        }
    }

    // ── POST /api/ocr ──

    private async Task HandleOcr(HttpListenerContext ctx)
    {
        if (_screenParser == null)
        {
            await WriteJson(ctx.Response, 503, new { error = "感知服务未配置" });
            return;
        }

        var window = FindAndResizeWindow();
        if (window == null)
        {
            await WriteJson(ctx.Response, 404, new { error = $"未找到进程 '{_processName}' 的窗口" });
            return;
        }

        string imagePath = _capturer.CaptureToFile(window, _screenshotDir);
        try
        {
            var body = await ReadBody(ctx.Request);
            var regionStr = GetString(body, "region");

            OcrResult result;
            if (regionStr != null)
            {
                // 解析 "x,y,w,h" 格式
                var parts = regionStr.Split(',');
                if (parts.Length == 4 &&
                    int.TryParse(parts[0], out int rx) && int.TryParse(parts[1], out int ry) &&
                    int.TryParse(parts[2], out int rw) && int.TryParse(parts[3], out int rh))
                {
                    result = await _screenParser.OcrRegionAsync(imagePath, rx, ry, rw, rh);
                }
                else
                {
                    await WriteJson(ctx.Response, 400, new { error = $"无效的 region 格式: '{regionStr}', 应为 'x,y,w,h'" });
                    return;
                }
            }
            else
            {
                result = await _screenParser.OcrAsync(imagePath);
            }

            await WriteJsonRaw(ctx.Response, 200, JsonSerializer.Serialize(result, JsonOptions));
        }
        finally
        {
            TryDeleteFile(imagePath);
        }
    }

    // ── POST /api/match ──

    private async Task HandleMatch(HttpListenerContext ctx)
    {
        if (_screenParser == null)
        {
            await WriteJson(ctx.Response, 503, new { error = "感知服务未配置" });
            return;
        }

        var window = FindAndResizeWindow();
        if (window == null)
        {
            await WriteJson(ctx.Response, 404, new { error = $"未找到进程 '{_processName}' 的窗口" });
            return;
        }

        string imagePath = _capturer.CaptureToFile(window, _screenshotDir);
        try
        {
            var body = await ReadBody(ctx.Request);
            string[]? templates = null;

            if (body.TryGetProperty("templates", out var tArr) && tArr.ValueKind == JsonValueKind.Array)
            {
                templates = tArr.EnumerateArray()
                    .Select(e => e.GetString()!)
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToArray();
            }

            var result = await _screenParser.MatchAsync(imagePath, templates);
            await WriteJsonRaw(ctx.Response, 200, JsonSerializer.Serialize(result, JsonOptions));
        }
        finally
        {
            TryDeleteFile(imagePath);
        }
    }

    // ── POST /api/resize ──

    private async Task HandleResize(HttpListenerContext ctx)
    {
        var body = await ReadBody(ctx.Request);
        int w = GetInt(body, "width") ?? _targetWidth;
        int h = GetInt(body, "height") ?? _targetHeight;

        var window = _locator.FindWindow(_processName);
        if (window == null)
        {
            await WriteJson(ctx.Response, 404, new { error = $"未找到进程 '{_processName}' 的窗口" });
            return;
        }

        var (wasResized, actualW, actualH) = _windowResizer.ResizeClientArea(window.Handle, w, h);
        _targetWidth = w;
        _targetHeight = h;

        await WriteJson(ctx.Response, 200, new
        {
            action = "resize",
            target_width = w,
            target_height = h,
            actual_width = actualW,
            actual_height = actualH,
            was_resized = wasResized
        });
    }

    // ── 辅助方法 ──

    private WindowInfo? FindAndResizeWindow()
    {
        var window = _locator.FindWindow(_processName);
        if (window == null) return null;

        if (window.Width != _targetWidth || window.Height != _targetHeight)
        {
            var (wasResized, _, _) = _windowResizer.ResizeClientArea(
                window.Handle, _targetWidth, _targetHeight);
            if (wasResized)
                window = _locator.RefreshWindowInfo(window.Handle) ?? window;
        }

        return window;
    }

    private void TryDeleteFile(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) { _log.Debug(ex, "删除临时截图失败: {Path}", path); }
    }

    private static async Task<JsonElement> ReadBody(HttpListenerRequest req)
    {
        if (!req.HasEntityBody)
            return default;
        using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
        var text = await reader.ReadToEndAsync();
        if (string.IsNullOrWhiteSpace(text))
            return default;
        try { return JsonSerializer.Deserialize<JsonElement>(text); }
        catch { return default; }
    }

    private static string? GetString(JsonElement body, string key)
    {
        if (body.ValueKind != JsonValueKind.Object) return null;
        if (!body.TryGetProperty(key, out var val)) return null;
        return val.ValueKind switch
        {
            JsonValueKind.String => val.GetString(),
            JsonValueKind.Number => val.GetRawText(),
            _ => null
        };
    }

    private static int? GetInt(JsonElement body, string key)
    {
        if (body.ValueKind != JsonValueKind.Object) return null;
        if (!body.TryGetProperty(key, out var val)) return null;
        if (val.ValueKind == JsonValueKind.Number && val.TryGetInt32(out int i)) return i;
        if (val.ValueKind == JsonValueKind.String && int.TryParse(val.GetString(), out int j)) return j;
        return null;
    }

    private static async Task WriteJson(HttpListenerResponse resp, int statusCode, object data)
    {
        var json = JsonSerializer.Serialize(data, JsonOptions);
        await WriteJsonRaw(resp, statusCode, json);
    }

    private static async Task WriteJsonRaw(HttpListenerResponse resp, int statusCode, string json)
    {
        resp.StatusCode = statusCode;
        resp.ContentType = "application/json; charset=utf-8";
        resp.Headers.Add("Access-Control-Allow-Origin", "*");
        var bytes = Encoding.UTF8.GetBytes(json);
        resp.ContentLength64 = bytes.Length;
        await resp.OutputStream.WriteAsync(bytes);
        resp.Close();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _cts.Dispose();
    }
}

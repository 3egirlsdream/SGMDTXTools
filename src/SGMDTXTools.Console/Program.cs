using System.Diagnostics;
using Serilog;
using SGMDTXTools.Core.Logging;
using SGMDTXTools.Core.Models;
using SGMDTXTools.Core.Services;

namespace SGMDTXTools.Console;

class Program
{
    private static ILogger _log = null!;
    private static ProcessWindowLocator _locator = null!;
    private static DesktopDcCapturer _capturer = null!;
    private static GridOverlay _gridOverlay = null!;
    private static KnowledgeManager _knowledge = null!;
    private static SendInputSimulator _inputSimulator = null!;
    private static HttpScreenParser? _screenParser;
    private static PythonServiceManager? _parserManager;
    private static WindowResizer _windowResizer = null!;
    private static CommandHttpServer? _commandServer;
    private static CancellationTokenSource _cts = new();
    private static string _processName = string.Empty;
    private static string _screenshotDir = string.Empty;
    private static int _targetClientWidth = 1280;
    private static int _targetClientHeight = 720;

    static async Task<int> Main(string[] args)
    {
        // 1. 初始化日志
        Log.Logger = LoggerConfig.CreateLogger();
        _log = Log.Logger.ForContext<Program>();
        _log.Information("========== SGMDTXTools 启动 ==========");

        try
        {
            // 2. 设置DPI感知
            DpiHelper.SetDpiAwareness(Log.Logger);

            // 3. 获取进程名
            if (args.Length > 0)
            {
                _processName = args[0];
            }
            else
            {
                System.Console.Write("请输入游戏进程名(不含.exe): ");
                _processName = System.Console.ReadLine()?.Trim() ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(_processName))
            {
                _log.Error("进程名不能为空");
                System.Console.WriteLine("错误: 进程名不能为空");
                return 1;
            }

            _log.Information("目标进程: {ProcessName}", _processName);

            // 4. 创建服务
            _locator = new ProcessWindowLocator(Log.Logger);
            _capturer = new DesktopDcCapturer(Log.Logger, _locator);
            _gridOverlay = new GridOverlay(Log.Logger);

            string knowledgeDir = Path.Combine(Directory.GetCurrentDirectory(), "knowledge");
            _knowledge = new KnowledgeManager(Log.Logger, knowledgeDir);
            _inputSimulator = new SendInputSimulator(Log.Logger, _gridOverlay);
            _windowResizer = new WindowResizer(Log.Logger);

            // 4.1 屏幕感知服务
            _screenshotDir = Path.Combine(Directory.GetCurrentDirectory(), "screenshots");
            string pythonDir = Path.Combine(Directory.GetCurrentDirectory(), "python");
            // 优先使用 venv 中的 Python（已安装 PaddleOCR 等依赖）
            string venvPython = Path.Combine(pythonDir, "venv", "bin", "python3");
            if (!File.Exists(venvPython))
                venvPython = Path.Combine(pythonDir, "venv", "Scripts", "python.exe"); // Windows
            if (!File.Exists(venvPython))
                _log.Warning("未找到 Python venv，将使用系统 Python。如缺少依赖，请运行: cd python && bash setup_env.sh");
            var parserConfig = new ScreenParserConfig
            {
                PythonServiceDir = pythonDir,
                PythonExe = File.Exists(venvPython) ? venvPython : "python"
            };
            _parserManager = new PythonServiceManager(Log.Logger, parserConfig);
            _screenParser = new HttpScreenParser(Log.Logger, parserConfig);

            // 5. Ctrl+C 优雅退出
            System.Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                _log.Information("收到Ctrl+C, 准备退出...");
                _cts.Cancel();
            };

            // 6. 命令循环
            await RunCommandLoop();
            return 0;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "未捕获异常");
            return 1;
        }
        finally
        {
            _log.Information("========== SGMDTXTools 退出 ==========");
            await Log.CloseAndFlushAsync();
            _screenParser?.Dispose();
            _parserManager?.Dispose();
            _capturer?.Dispose();
        }
    }

    static async Task RunCommandLoop()
    {
        PrintHelp();

        while (!_cts.IsCancellationRequested)
        {
            System.Console.Write("\n> ");
            string? input = System.Console.ReadLine()?.Trim();

            if (string.IsNullOrEmpty(input))
                continue;

            var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var command = parts[0].ToLower();

            try
            {
                switch (command)
                {
                    case "find":
                        HandleFind();
                        break;
                    case "capture":
                        bool withGrid = parts.Length > 1 && parts[1].ToLower() == "--grid";
                        HandleCapture(withGrid);
                        break;
                    case "watch":
                        int interval = parts.Length > 1 && int.TryParse(parts[1], out int sec) ? sec : 5;
                        await HandleWatch(interval);
                        break;
                    case "monitor":
                        await HandleMonitor();
                        break;
                    case "resize":
                        HandleResize(parts);
                        break;
                    case "grid":
                        if (parts.Length > 1)
                            HandleGridConvert(parts[1]);
                        else
                            System.Console.WriteLine("用法: grid <网格引用>  例: grid F8");
                        break;
                    case "knowledge":
                    case "kb":
                        string subCmd = parts.Length > 1 ? parts[1].ToLower() : "list";
                        string subArg = parts.Length > 2 ? string.Join(" ", parts.Skip(2)) : "";
                        HandleKnowledge(subCmd, subArg);
                        break;
                    case "click":
                        await HandleClick(parts);
                        break;
                    case "dblclick":
                        await HandleDoubleClick(parts);
                        break;
                    case "drag":
                        await HandleDrag(parts);
                        break;
                    case "scroll":
                        await HandleScroll(parts);
                        break;
                    case "moveto":
                        await HandleMoveTo(parts);
                        break;
                    case "scan":
                        await HandleScan();
                        break;
                    case "ocr":
                        await HandleOcr(parts);
                        break;
                    case "match":
                        await HandleMatch(parts);
                        break;
                    case "template":
                        await HandleTemplate(parts);
                        break;
                    case "parser":
                        await HandleParser(parts);
                        break;
                    case "status":
                        await HandleStatus();
                        break;
                    case "help":
                        PrintHelp();
                        break;
                    case "quit":
                    case "exit":
                        _cts.Cancel();
                        return;
                    default:
                        System.Console.WriteLine($"未知命令: {command}, 输入 help 查看帮助");
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                System.Console.WriteLine("操作已取消");
            }
            catch (Exception ex)
            {
                _log.Error(ex, "命令 '{Command}' 执行失败", command);
                System.Console.WriteLine($"错误: {ex.Message}");
            }
        }
    }

    static void HandleFind()
    {
        var window = _locator.FindWindow(_processName);
        if (window == null)
        {
            System.Console.WriteLine($"未找到进程 '{_processName}' 的窗口");
            return;
        }

        System.Console.WriteLine($"找到窗口:");
        System.Console.WriteLine($"  句柄:   0x{window.Handle:X}");
        System.Console.WriteLine($"  标题:   {window.Title}");
        System.Console.WriteLine($"  进程:   {window.ProcessName} (PID: {window.ProcessId})");
        System.Console.WriteLine($"  位置:   ({window.X}, {window.Y})");
        System.Console.WriteLine($"  尺寸:   {window.Width} x {window.Height}");

        string sizeStatus = (window.Width == _targetClientWidth && window.Height == _targetClientHeight)
            ? "匹配" : $"需调整 → {_targetClientWidth}x{_targetClientHeight}";
        System.Console.WriteLine($"  目标:   {_targetClientWidth}x{_targetClientHeight} ({sizeStatus})");

        double scale = DpiHelper.GetScaleForWindow(window.Handle, Log.Logger);
        System.Console.WriteLine($"  DPI缩放: {scale:F2}x");
    }

    static void HandleCapture(bool withGrid = false)
    {
        var window = _locator.FindWindow(_processName);
        if (window == null)
        {
            System.Console.WriteLine($"未找到进程 '{_processName}' 的窗口");
            return;
        }

        // 自动调整窗口到目标分辨率
        if (window.Width != _targetClientWidth || window.Height != _targetClientHeight)
        {
            System.Console.WriteLine($"[自动调整窗口: {window.Width}x{window.Height} → {_targetClientWidth}x{_targetClientHeight}]");
            var (wasResized, _, _) = _windowResizer.ResizeClientArea(
                window.Handle, _targetClientWidth, _targetClientHeight);
            if (wasResized)
                window = _locator.RefreshWindowInfo(window.Handle) ?? window;
        }

        string outputDir = Path.Combine(Directory.GetCurrentDirectory(), "screenshots");
        string path = _capturer.CaptureToFile(window, outputDir);
        System.Console.WriteLine($"截图已保存: {path}");

        if (withGrid)
        {
            string gridPath = _gridOverlay.DrawGridOnCapture(path, outputDir);
            System.Console.WriteLine($"网格截图: {gridPath}");
            System.Console.WriteLine($"  网格: {_gridOverlay.Config.Columns}列 x {_gridOverlay.Config.Rows}行");
            System.Console.WriteLine($"  引用: A1(左上) - {_gridOverlay.Config.GetGridRef(_gridOverlay.Config.Columns - 1, _gridOverlay.Config.Rows - 1)}(右下)");
        }
    }

    static void HandleResize(string[] parts)
    {
        var window = EnsureWindow();
        if (window == null) return;

        int targetW = _targetClientWidth;
        int targetH = _targetClientHeight;

        // 解析参数: resize 1280x720
        if (parts.Length > 1)
        {
            var sizeStr = parts[1].ToLower();
            var sep = sizeStr.Contains('x') ? 'x' : '*';
            var dims = sizeStr.Split(sep);
            if (dims.Length == 2 && int.TryParse(dims[0], out int w) && int.TryParse(dims[1], out int h))
            {
                targetW = w;
                targetH = h;
            }
            else
            {
                System.Console.WriteLine("用法: resize [宽x高]  (如: resize 1280x720)");
                return;
            }
        }

        System.Console.WriteLine($"当前客户区: {window.Width}x{window.Height}");
        System.Console.WriteLine($"目标客户区: {targetW}x{targetH}");

        var (wasResized, actualW, actualH) = _windowResizer.ResizeClientArea(window.Handle, targetW, targetH);

        _targetClientWidth = targetW;
        _targetClientHeight = targetH;
        _commandServer?.UpdateTargetSize(targetW, targetH);

        if (wasResized)
            System.Console.WriteLine($"调整完成: {actualW}x{actualH}");
        else if (actualW > 0)
            System.Console.WriteLine("已是目标尺寸，无需调整");
        else
            System.Console.WriteLine("调整失败，请检查日志");
    }

    static void HandleGridConvert(string gridRef)
    {
        int imageWidth = 0, imageHeight = 0;

        var window = _locator.FindWindow(_processName);
        if (window != null)
        {
            imageWidth = window.Width;
            imageHeight = window.Height;
        }
        else
        {
            string screenshotDir = Path.Combine(Directory.GetCurrentDirectory(), "screenshots");
            if (Directory.Exists(screenshotDir))
            {
                var latestFile = Directory.GetFiles(screenshotDir, "*.png")
                    .Where(f => !f.Contains("_grid"))
                    .OrderByDescending(f => new FileInfo(f).LastWriteTime)
                    .FirstOrDefault();

                if (latestFile != null)
                {
                    using var img = System.Drawing.Image.FromFile(latestFile);
                    imageWidth = img.Width;
                    imageHeight = img.Height;
                    System.Console.WriteLine($"(使用最近截图尺寸: {imageWidth}x{imageHeight})");
                }
            }
        }

        if (imageWidth <= 0 || imageHeight <= 0)
        {
            System.Console.WriteLine("无法确定图片尺寸，请先执行 find 或 capture");
            return;
        }

        try
        {
            var (px, py) = _gridOverlay.GridRefToPixel(gridRef.ToUpper(), imageWidth, imageHeight);
            var bounds = _gridOverlay.GetCellBounds(gridRef.ToUpper(), imageWidth, imageHeight);

            System.Console.WriteLine($"网格 {gridRef.ToUpper()}:");
            System.Console.WriteLine($"  中心像素: ({px}, {py})");
            System.Console.WriteLine($"  单元格区域: ({bounds.X},{bounds.Y}) {bounds.Width}x{bounds.Height}");
            System.Console.WriteLine($"  图片尺寸: {imageWidth}x{imageHeight}");
        }
        catch (ArgumentException ex)
        {
            System.Console.WriteLine($"无效的网格引用: {ex.Message}");
        }
    }

    static void HandleKnowledge(string subCommand, string argument)
    {
        switch (subCommand)
        {
            case "list":
            case "ls":
                var files = _knowledge.ListFiles();
                if (files.Count == 0)
                {
                    System.Console.WriteLine("知识库为空");
                    return;
                }
                System.Console.WriteLine($"知识文件 ({files.Count}个):");
                foreach (var f in files)
                {
                    System.Console.WriteLine($"  {f.FileName,-25} {f.Title,-20} {f.LineCount,4}行 {f.SizeBytes / 1024,4}KB  {f.LastModified:MM-dd HH:mm}");
                }
                break;

            case "read":
                if (string.IsNullOrEmpty(argument))
                {
                    System.Console.WriteLine("用法: knowledge read <文件名>");
                    return;
                }
                string? content = _knowledge.ReadFile(argument);
                if (content == null)
                {
                    System.Console.WriteLine($"文件不存在: {argument}");
                    return;
                }
                System.Console.WriteLine(content);
                break;

            case "search":
                if (string.IsNullOrEmpty(argument))
                {
                    System.Console.WriteLine("用法: knowledge search <关键词>");
                    return;
                }
                var results = _knowledge.Search(argument);
                if (results.Count == 0)
                {
                    System.Console.WriteLine($"未找到包含 '{argument}' 的内容");
                    return;
                }
                System.Console.WriteLine($"搜索 '{argument}' - 找到 {results.Count} 条结果:");
                foreach (var r in results)
                {
                    System.Console.WriteLine($"  [{r.FileName}:{r.LineNumber}] {r.LineContent}");
                }
                break;

            case "stats":
                var stats = _knowledge.GetStats();
                System.Console.WriteLine("知识库统计:");
                System.Console.WriteLine($"  文件数: {stats.FileCount}");
                System.Console.WriteLine($"  总行数: {stats.TotalLines}");
                System.Console.WriteLine($"  总大小: {stats.TotalSizeBytes / 1024}KB");
                System.Console.WriteLine($"  目录:   {stats.KnowledgeDir}");
                break;

            case "context":
                string ctx = _knowledge.ReadAllAsContext();
                System.Console.WriteLine($"知识上下文 (总长度: {ctx.Length}字符):");
                if (ctx.Length > 500)
                    System.Console.WriteLine(ctx[..500] + "\n... (截断)");
                else
                    System.Console.WriteLine(ctx);
                break;

            default:
                System.Console.WriteLine("知识库命令:");
                System.Console.WriteLine("  knowledge list              列出所有知识文件");
                System.Console.WriteLine("  knowledge read <文件名>     读取知识文件");
                System.Console.WriteLine("  knowledge search <关键词>   搜索知识库");
                System.Console.WriteLine("  knowledge stats             知识库统计");
                System.Console.WriteLine("  knowledge context           预览LLM上下文");
                break;
        }
    }

    static async Task HandleWatch(int intervalSeconds)
    {
        var window = _locator.FindWindow(_processName);
        if (window == null)
        {
            System.Console.WriteLine($"未找到进程 '{_processName}' 的窗口");
            return;
        }

        string outputDir = Path.Combine(Directory.GetCurrentDirectory(), "screenshots");
        System.Console.WriteLine($"开始定时截图 (间隔{intervalSeconds}秒), 按Ctrl+C停止...");

        using var watchCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);

        var captureTask = _capturer.StartPeriodicCapture(
            window,
            TimeSpan.FromSeconds(intervalSeconds),
            outputDir,
            watchCts.Token);

        var inputTask = Task.Run(() =>
        {
            System.Console.WriteLine("按回车键停止定时截图...");
            System.Console.ReadLine();
            watchCts.Cancel();
        });

        try
        {
            await Task.WhenAny(captureTask, inputTask);
        }
        catch (OperationCanceledException)
        {
            // 正常取消
        }

        System.Console.WriteLine("定时截图已停止");
    }

    static async Task HandleMonitor()
    {
        var window = _locator.FindWindow(_processName);
        if (window == null)
        {
            System.Console.WriteLine($"未找到进程 '{_processName}' 的窗口");
            return;
        }

        System.Console.WriteLine("开始监控窗口位置变化, 按回车键停止...");

        using var monitorCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);

        var watchTask = _locator.WatchWindow(window.Handle, info =>
        {
            System.Console.WriteLine($"  位置变化: ({info.X},{info.Y}) {info.Width}x{info.Height}");
        }, monitorCts.Token);

        var inputTask = Task.Run(() =>
        {
            System.Console.ReadLine();
            monitorCts.Cancel();
        });

        try
        {
            await Task.WhenAny(watchTask, inputTask);
        }
        catch (OperationCanceledException)
        {
            // 正常取消
        }

        System.Console.WriteLine("监控已停止");
    }

    static WindowInfo? EnsureWindow()
    {
        var window = _locator.FindWindow(_processName);
        if (window == null)
            System.Console.WriteLine($"未找到进程 '{_processName}' 的窗口");
        return window;
    }

    static async Task HandleClick(string[] parts)
    {
        if (parts.Length < 2)
        {
            System.Console.WriteLine("用法: click <坐标> [right]  (如: click F8, click 100,200 right)");
            return;
        }

        var window = EnsureWindow();
        if (window == null) return;

        var coord = InputCoordinate.Parse(parts[1]);
        var button = parts.Length > 2 && parts[2].Equals("right", StringComparison.OrdinalIgnoreCase)
            ? MouseButton.Right : MouseButton.Left;

        await _inputSimulator.ClickAsync(window.Handle, coord, button, _cts.Token);
        System.Console.WriteLine($"已点击: {coord} ({button})");
    }

    static async Task HandleDoubleClick(string[] parts)
    {
        if (parts.Length < 2)
        {
            System.Console.WriteLine("用法: dblclick <坐标>  (如: dblclick E5)");
            return;
        }

        var window = EnsureWindow();
        if (window == null) return;

        var coord = InputCoordinate.Parse(parts[1]);
        await _inputSimulator.DoubleClickAsync(window.Handle, coord, _cts.Token);
        System.Console.WriteLine($"已双击: {coord}");
    }

    static async Task HandleDrag(string[] parts)
    {
        if (parts.Length < 3)
        {
            System.Console.WriteLine("用法: drag <起点> <终点>  (如: drag A1 J18, drag 100,200 500,600)");
            return;
        }

        var window = EnsureWindow();
        if (window == null) return;

        var from = InputCoordinate.Parse(parts[1]);
        var to = InputCoordinate.Parse(parts[2]);
        await _inputSimulator.DragAsync(window.Handle, from, to, _cts.Token);
        System.Console.WriteLine($"已拖拽: {from} → {to}");
    }

    static async Task HandleScroll(string[] parts)
    {
        if (parts.Length < 3)
        {
            System.Console.WriteLine("用法: scroll <坐标> up|down [次数]  (如: scroll F8 down 5)");
            return;
        }

        var window = EnsureWindow();
        if (window == null) return;

        var coord = InputCoordinate.Parse(parts[1]);

        if (!Enum.TryParse<ScrollDirection>(parts[2], true, out var direction))
        {
            System.Console.WriteLine($"无效方向: '{parts[2]}', 应为 up 或 down");
            return;
        }

        int clicks = parts.Length > 3 && int.TryParse(parts[3], out int c) ? c : 3;
        await _inputSimulator.ScrollAsync(window.Handle, coord, direction, clicks, _cts.Token);
        System.Console.WriteLine($"已滚动: {coord} {direction} x{clicks}");
    }

    static async Task HandleMoveTo(string[] parts)
    {
        if (parts.Length < 2)
        {
            System.Console.WriteLine("用法: moveto <坐标>  (如: moveto C3)");
            return;
        }

        var window = EnsureWindow();
        if (window == null) return;

        var coord = InputCoordinate.Parse(parts[1]);
        await _inputSimulator.MoveToAsync(window.Handle, coord, _cts.Token);
        System.Console.WriteLine($"已移动: {coord}");
    }

    // ========== 屏幕感知命令 ==========

    static string? GetLatestScreenshot()
    {
        if (!Directory.Exists(_screenshotDir)) return null;
        return Directory.GetFiles(_screenshotDir, "*.png")
            .Where(f => !f.Contains("_grid"))
            .OrderByDescending(f => new FileInfo(f).LastWriteTime)
            .FirstOrDefault();
    }

    static string? CaptureForParser()
    {
        var window = _locator.FindWindow(_processName);
        if (window == null)
        {
            System.Console.WriteLine($"未找到进程 '{_processName}' 的窗口");
            return null;
        }

        // 自动调整窗口到目标分辨率
        if (window.Width != _targetClientWidth || window.Height != _targetClientHeight)
        {
            System.Console.WriteLine($"[自动调整窗口: {window.Width}x{window.Height} → {_targetClientWidth}x{_targetClientHeight}]");
            var (wasResized, actualW, actualH) = _windowResizer.ResizeClientArea(
                window.Handle, _targetClientWidth, _targetClientHeight);
            if (wasResized)
            {
                window = _locator.RefreshWindowInfo(window.Handle) ?? window;
                System.Console.WriteLine($"[窗口已调整: {actualW}x{actualH}]");
            }
        }

        string path = _capturer.CaptureToFile(window, _screenshotDir);
        System.Console.WriteLine($"截图已保存: {Path.GetFileName(path)}");
        return path;
    }

    static async Task EnsureParserAsync()
    {
        if (_parserManager == null) return;
        if (await _screenParser!.IsAvailableAsync(_cts.Token)) return;

        System.Console.WriteLine("[启动 Python 感知服务...]");
        await _parserManager.EnsureStartedAsync(_cts.Token);
    }

    static async Task HandleScan()
    {
        string? imagePath = CaptureForParser();
        if (imagePath == null) return;

        await EnsureParserAsync();

        System.Console.WriteLine("[感知中...]");
        var result = await _screenParser!.ScanAsync(imagePath, _cts.Token);

        if (!result.Success)
        {
            System.Console.WriteLine($"感知失败: {result.Error}");
            return;
        }

        var size = result.ImageSize;
        System.Console.WriteLine($"感知结果 ({result.ElapsedMs}ms{(size != null ? $", {size.Width}x{size.Height}" : "")}):");

        if (result.Texts.Count > 0)
        {
            System.Console.WriteLine($"\n文字 ({result.Texts.Count}项):");
            foreach (var t in result.Texts)
            {
                System.Console.WriteLine($"  \"{t.Text,-16}\" ({t.Bbox.X},{t.Bbox.Y}) {t.Bbox.Width}x{t.Bbox.Height}  置信度:{t.Confidence:F2}");
            }
        }
        else
        {
            System.Console.WriteLine("\n文字: 无");
        }

        if (result.Matches.Count > 0)
        {
            System.Console.WriteLine($"\n图标 ({result.Matches.Count}项):");
            foreach (var m in result.Matches)
            {
                System.Console.WriteLine($"  [{m.Template,-14}] ({m.Bbox.X},{m.Bbox.Y}) {m.Bbox.Width}x{m.Bbox.Height}  置信度:{m.Confidence:F2}");
            }
        }

        System.Console.WriteLine("\n提示: 可用 click <x>,<y> 点击任意目标");
        System.Console.WriteLine("      发现不认识的按钮？执行 template ui 打开管理页面框选添加");
    }

    static async Task HandleOcr(string[] parts)
    {
        string? imagePath = CaptureForParser();
        if (imagePath == null) return;

        await EnsureParserAsync();

        OcrResult result;
        if (parts.Length > 1 && parts[1].Contains(','))
        {
            var nums = parts[1].Split(',');
            if (nums.Length == 4 &&
                int.TryParse(nums[0], out int x) && int.TryParse(nums[1], out int y) &&
                int.TryParse(nums[2], out int w) && int.TryParse(nums[3], out int h))
            {
                System.Console.WriteLine($"[区域 OCR: ({x},{y}) {w}x{h}...]");
                result = await _screenParser!.OcrRegionAsync(imagePath, x, y, w, h, _cts.Token);
            }
            else
            {
                System.Console.WriteLine("用法: ocr [x,y,w,h]  (如: ocr 100,0,400,50)");
                return;
            }
        }
        else
        {
            System.Console.WriteLine("[OCR 全图识别中...]");
            result = await _screenParser!.OcrAsync(imagePath, _cts.Token);
        }

        if (!result.Success)
        {
            System.Console.WriteLine($"OCR 失败: {result.Error}");
            return;
        }

        System.Console.WriteLine($"OCR 结果 ({result.ElapsedMs}ms, {result.Texts.Count}项):");
        foreach (var t in result.Texts)
        {
            System.Console.WriteLine($"  \"{t.Text,-20}\" ({t.Bbox.X},{t.Bbox.Y}) {t.Bbox.Width}x{t.Bbox.Height}  置信度:{t.Confidence:F2}");
        }
    }

    static async Task HandleMatch(string[] parts)
    {
        string? imagePath = CaptureForParser();
        if (imagePath == null) return;

        await EnsureParserAsync();

        string[]? templateNames = null;
        if (parts.Length > 1)
        {
            templateNames = parts.Skip(1).ToArray();
            System.Console.WriteLine($"[匹配模板: {string.Join(", ", templateNames)}...]");
        }
        else
        {
            System.Console.WriteLine("[匹配全部模板...]");
        }

        var result = await _screenParser!.MatchAsync(imagePath, templateNames, _cts.Token);

        if (!result.Success)
        {
            System.Console.WriteLine($"匹配失败: {result.Error}");
            return;
        }

        if (result.Matches.Count == 0)
        {
            System.Console.WriteLine($"匹配结果 ({result.ElapsedMs}ms): 未匹配到任何模板");
            return;
        }

        System.Console.WriteLine($"匹配结果 ({result.ElapsedMs}ms, {result.Matches.Count}项):");
        foreach (var m in result.Matches)
        {
            System.Console.WriteLine($"  [{m.Template,-14}] ({m.Bbox.X},{m.Bbox.Y}) {m.Bbox.Width}x{m.Bbox.Height}  置信度:{m.Confidence:F2}");
        }
    }

    static async Task HandleTemplate(string[] parts)
    {
        string subCmd = parts.Length > 1 ? parts[1].ToLower() : "list";

        switch (subCmd)
        {
            case "list":
                await EnsureParserAsync();
                try
                {
                    using var listHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                    var resp = await listHttp.GetAsync("http://127.0.0.1:5100/api/templates", _cts.Token);
                    var body = await resp.Content.ReadAsStringAsync(_cts.Token);
                    var templates = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(body);

                    if (templates.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        if (templates.GetArrayLength() == 0)
                        {
                            System.Console.WriteLine("模板库为空，执行 template ui 添加模板");
                            return;
                        }
                        System.Console.WriteLine($"模板库 ({templates.GetArrayLength()}个):");
                        foreach (var t in templates.EnumerateArray())
                        {
                            string name = t.GetProperty("name").GetString() ?? "";
                            string cat = t.TryGetProperty("category", out var c) ? c.GetString() ?? "" : "";
                            double thresh = t.TryGetProperty("threshold", out var th) ? th.GetDouble() : 0.8;
                            System.Console.WriteLine($"  {name,-20} {cat,-10} 阈值:{thresh:F2}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Console.WriteLine($"获取模板列表失败: {ex.Message}");
                    System.Console.WriteLine("请先执行 parser start 启动服务");
                }
                break;

            case "ui":
                System.Console.WriteLine("[截图中...]");
                var capPath = CaptureForParser();
                if (capPath != null)
                    System.Console.WriteLine($"已保存: {Path.GetFileName(capPath)}");

                System.Console.WriteLine("[确保 Python 服务运行中...]");
                await EnsureParserAsync();

                string url = "http://127.0.0.1:5100/";
                try
                {
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                    System.Console.WriteLine($"已打开浏览器: {url}");
                }
                catch
                {
                    System.Console.WriteLine($"无法自动打开浏览器，请手动访问: {url}");
                }
                System.Console.WriteLine("提示: 在网页中点击截图 → 框选按钮/图标区域 → 保存为模板");
                break;

            case "test":
                if (parts.Length < 3)
                {
                    System.Console.WriteLine("用法: template test <模板名>  (如: template test close_btn)");
                    return;
                }
                string testName = parts[2];
                string? testImage = GetLatestScreenshot();
                if (testImage == null)
                {
                    System.Console.WriteLine("没有截图可供测试，请先执行 capture");
                    return;
                }

                await EnsureParserAsync();
                System.Console.WriteLine($"[测试模板 '{testName}' 对最新截图...]");
                var matchResult = await _screenParser!.MatchAsync(testImage, new[] { testName }, _cts.Token);
                if (!matchResult.Success)
                {
                    System.Console.WriteLine($"测试失败: {matchResult.Error}");
                    return;
                }
                if (matchResult.Matches.Count == 0)
                {
                    System.Console.WriteLine($"模板 '{testName}' 在最新截图中未匹配到");
                }
                else
                {
                    System.Console.WriteLine($"匹配到 {matchResult.Matches.Count} 处:");
                    foreach (var m in matchResult.Matches)
                    {
                        System.Console.WriteLine($"  ({m.Bbox.X},{m.Bbox.Y}) {m.Bbox.Width}x{m.Bbox.Height}  置信度:{m.Confidence:F2}");
                    }
                }
                break;

            default:
                System.Console.WriteLine("模板命令:");
                System.Console.WriteLine("  template list              列出所有模板");
                System.Console.WriteLine("  template ui                截图 + 打开模板管理网页");
                System.Console.WriteLine("  template test <名称>       用最新截图测试模板");
                break;
        }
    }

    static async Task HandleParser(string[] parts)
    {
        if (_parserManager == null)
        {
            System.Console.WriteLine("感知服务管理器未初始化");
            return;
        }

        string subCmd = parts.Length > 1 ? parts[1].ToLower() : "status";

        switch (subCmd)
        {
            case "start":
                var (available, envMsg) = await _parserManager.CheckEnvironmentAsync();
                if (!available)
                {
                    System.Console.WriteLine($"Python 环境不可用: {envMsg}");
                    return;
                }
                System.Console.WriteLine($"Python 环境: {envMsg}");
                await _parserManager.EnsureStartedAsync(_cts.Token);
                System.Console.WriteLine("Python 感知服务已启动");
                break;

            case "stop":
                await _parserManager.StopAsync(_cts.Token);
                System.Console.WriteLine("Python 感知服务已停止");
                break;

            case "status":
                string status = _parserManager.GetStatus();
                System.Console.WriteLine($"Python 感知服务: {status}");
                if (_parserManager.IsRunning)
                {
                    bool healthy = await _parserManager.CheckHealthAsync(_cts.Token);
                    System.Console.WriteLine($"  健康检查: {(healthy ? "正常" : "异常")}");
                }
                break;

            default:
                System.Console.WriteLine("感知服务命令:");
                System.Console.WriteLine("  parser start               启动 Python 服务");
                System.Console.WriteLine("  parser stop                停止 Python 服务");
                System.Console.WriteLine("  parser status              查看服务状态");
                break;
        }
    }

    // ========== 状态与帮助 ==========

    static async Task HandleStatus()
    {
        System.Console.WriteLine($"目标进程: {_processName}");

        var window = _locator.FindWindow(_processName);
        if (window == null)
        {
            System.Console.WriteLine("窗口状态: 未找到");
        }
        else
        {
            System.Console.WriteLine($"窗口状态: 活跃");
            System.Console.WriteLine($"  句柄: 0x{window.Handle:X}");
            System.Console.WriteLine($"  位置: ({window.X},{window.Y}) {window.Width}x{window.Height}");
            string sizeMatch = (window.Width == _targetClientWidth && window.Height == _targetClientHeight)
                ? "匹配" : "不匹配";
            System.Console.WriteLine($"  目标: {_targetClientWidth}x{_targetClientHeight} ({sizeMatch})");
        }

        if (Directory.Exists(_screenshotDir))
        {
            var files = Directory.GetFiles(_screenshotDir, "*.png");
            System.Console.WriteLine($"截图数量: {files.Length}");
            if (files.Length > 0)
            {
                var latest = files.OrderByDescending(f => f).First();
                var fi = new FileInfo(latest);
                System.Console.WriteLine($"最近截图: {fi.Name} ({fi.Length / 1024}KB, {fi.LastWriteTime:HH:mm:ss})");
            }
        }
        else
        {
            System.Console.WriteLine("截图数量: 0");
        }

        var stats = _knowledge.GetStats();
        System.Console.WriteLine($"知识文件: {stats.FileCount}个, {stats.TotalLines}行, {stats.TotalSizeBytes / 1024}KB");

        if (_parserManager != null)
        {
            string parserStatus = _parserManager.GetStatus();
            System.Console.WriteLine($"感知服务: {parserStatus}");
            if (_parserManager.IsRunning)
            {
                bool healthy = await _parserManager.CheckHealthAsync(_cts.Token);
                System.Console.WriteLine($"  健康检查: {(healthy ? "正常" : "异常")}");
            }
        }
    }

    static void PrintHelp()
    {
        System.Console.WriteLine("SGMDTXTools - 游戏辅助工具");
        System.Console.WriteLine("========================");
        System.Console.WriteLine("命令:");
        System.Console.WriteLine("  find                       查找游戏窗口");
        System.Console.WriteLine("  capture                    单次截图");
        System.Console.WriteLine("  capture --grid             截图并叠加坐标网格");
        System.Console.WriteLine("  watch [秒数]               定时截图 (默认5秒)");
        System.Console.WriteLine("  monitor                    监控窗口位置变化");
        System.Console.WriteLine("  resize [WxH]               调整窗口为目标尺寸 (默认 1280x720)");
        System.Console.WriteLine("  grid <引用>                网格引用转像素坐标 (如: grid F8)");
        System.Console.WriteLine("  click <坐标> [right]       点击 (如: click F8, click 100,200 right)");
        System.Console.WriteLine("  dblclick <坐标>            双击 (如: dblclick E5)");
        System.Console.WriteLine("  drag <起点> <终点>         拖拽 (如: drag A1 J18)");
        System.Console.WriteLine("  scroll <坐标> up|down [次] 滚轮 (如: scroll F8 down 5)");
        System.Console.WriteLine("  moveto <坐标>              移动鼠标 (如: moveto C3)");
        System.Console.WriteLine("  ---- 屏幕感知 ----");
        System.Console.WriteLine("  scan                       截图 + OCR + 模板匹配 (主力命令)");
        System.Console.WriteLine("  ocr                        截图 + 仅 OCR 文字识别");
        System.Console.WriteLine("  ocr <x,y,w,h>             截图 + 区域 OCR (如: ocr 100,0,400,50)");
        System.Console.WriteLine("  match                      截图 + 全模板匹配");
        System.Console.WriteLine("  match <名称>               截图 + 指定模板匹配");
        System.Console.WriteLine("  template list              列出所有模板");
        System.Console.WriteLine("  template ui                截图 + 打开模板管理网页");
        System.Console.WriteLine("  template test <名称>       用最新截图测试模板");
        System.Console.WriteLine("  parser start               启动 Python 感知服务");
        System.Console.WriteLine("  parser stop                停止 Python 感知服务");
        System.Console.WriteLine("  parser status              查看感知服务状态");
        System.Console.WriteLine("  ---- 知识库 ----");
        System.Console.WriteLine("  knowledge list             列出知识文件");
        System.Console.WriteLine("  knowledge read <文件>      读取知识文件");
        System.Console.WriteLine("  knowledge search <词>      搜索知识库");
        System.Console.WriteLine("  knowledge stats            知识库统计");
        System.Console.WriteLine("  knowledge context          预览LLM上下文");
        System.Console.WriteLine("  ---- 其他 ----");
        System.Console.WriteLine("  status                     显示当前状态");
        System.Console.WriteLine("  help                       显示帮助");
        System.Console.WriteLine("  quit                       退出");
    }
}
}
        System.Console.WriteLine("  knowledge stats            知识库统计");
        System.Console.WriteLine("  knowledge context          预览LLM上下文");
        System.Console.WriteLine("  ---- 其他 ----");
        System.Console.WriteLine("  status                     显示当前状态");
        System.Console.WriteLine("  help                       显示帮助");
        System.Console.WriteLine("  quit                       退出");
    }
}

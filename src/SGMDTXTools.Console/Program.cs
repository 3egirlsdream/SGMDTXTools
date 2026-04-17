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
    private static PostMessageInputSimulator _inputSimulator = null!;
    private static CancellationTokenSource _cts = new();
    private static string _processName = string.Empty;

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
            _inputSimulator = new PostMessageInputSimulator(Log.Logger, _gridOverlay);

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
                    case "status":
                        HandleStatus();
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

    static void HandleGridConvert(string gridRef)
    {
        // 先查找窗口获取尺寸，如果找不到则使用最近截图的尺寸
        int imageWidth = 0, imageHeight = 0;

        var window = _locator.FindWindow(_processName);
        if (window != null)
        {
            imageWidth = window.Width;
            imageHeight = window.Height;
        }
        else
        {
            // 尝试从最近截图获取尺寸
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
                // 只显示前500字符预览
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

        // 在后台运行定时截图，同时监听回车键停止
        var captureTask = _capturer.StartPeriodicCapture(
            window,
            TimeSpan.FromSeconds(intervalSeconds),
            outputDir,
            watchCts.Token);

        // 等待用户按回车或Ctrl+C
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

    static void HandleStatus()
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
        }

        string screenshotDir = Path.Combine(Directory.GetCurrentDirectory(), "screenshots");
        if (Directory.Exists(screenshotDir))
        {
            var files = Directory.GetFiles(screenshotDir, "*.png");
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

        // 知识库状态
        var stats = _knowledge.GetStats();
        System.Console.WriteLine($"知识文件: {stats.FileCount}个, {stats.TotalLines}行, {stats.TotalSizeBytes / 1024}KB");
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
        System.Console.WriteLine("  grid <引用>                网格引用转像素坐标 (如: grid F8)");
        System.Console.WriteLine("  click <坐标> [right]       点击 (如: click F8, click 100,200 right)");
        System.Console.WriteLine("  dblclick <坐标>            双击 (如: dblclick E5)");
        System.Console.WriteLine("  drag <起点> <终点>         拖拽 (如: drag A1 J18)");
        System.Console.WriteLine("  scroll <坐标> up|down [次] 滚轮 (如: scroll F8 down 5)");
        System.Console.WriteLine("  moveto <坐标>              移动鼠标 (如: moveto C3)");
        System.Console.WriteLine("  knowledge list             列出知识文件");
        System.Console.WriteLine("  knowledge read <文件>      读取知识文件");
        System.Console.WriteLine("  knowledge search <词>      搜索知识库");
        System.Console.WriteLine("  knowledge stats            知识库统计");
        System.Console.WriteLine("  knowledge context          预览LLM上下文");
        System.Console.WriteLine("  status                     显示当前状态");
        System.Console.WriteLine("  help                       显示帮助");
        System.Console.WriteLine("  quit                       退出");
    }
}

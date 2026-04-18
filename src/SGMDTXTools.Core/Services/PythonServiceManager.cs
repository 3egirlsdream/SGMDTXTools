using System.Diagnostics;
using Serilog;
using SGMDTXTools.Core.Models;

namespace SGMDTXTools.Core.Services;

public class PythonServiceManager : IDisposable
{
    private readonly ILogger _log;
    private readonly ScreenParserConfig _config;
    private Process? _process;
    private bool _disposed;
    private const int MaxRestarts = 3;

    public bool IsRunning => _process is { HasExited: false };

    public PythonServiceManager(ILogger logger, ScreenParserConfig config)
    {
        _log = logger.ForContext<PythonServiceManager>();
        _config = config;
    }

    /// <summary>
    /// 确保 Python 服务已启动并就绪。如未运行则自动启动。
    /// </summary>
    public async Task EnsureStartedAsync(CancellationToken ct = default)
    {
        if (IsRunning && await CheckHealthAsync(ct))
            return;

        if (IsRunning)
        {
            _log.Warning("Python 进程存活但健康检查失败，正在重启...");
            await StopAsync(ct);
        }

        await StartAsync(ct);
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (IsRunning)
        {
            _log.Information("Python 服务已在运行 (PID: {Pid})", _process!.Id);
            return;
        }

        string serviceDir = Path.GetFullPath(_config.PythonServiceDir);
        string runScript = Path.Combine(serviceDir, "run.py");

        if (!File.Exists(runScript))
        {
            throw new FileNotFoundException(
                $"Python 服务入口脚本不存在: {runScript}\n请确保 python/ 目录存在且包含 run.py");
        }

        _log.Information("启动 Python 服务: {Exe} {Script}", _config.PythonExe, runScript);

        var psi = new ProcessStartInfo
        {
            FileName = _config.PythonExe,
            Arguments = $"\"{runScript}\"",
            WorkingDirectory = serviceDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        // 设置环境变量
        psi.Environment["SGMDTX_HOST"] = "0.0.0.0"; // 绑定所有网卡，允许 VM 等外部访问
        psi.Environment["SGMDTX_PORT"] = _config.Port.ToString();
        psi.Environment["PYTHONUNBUFFERED"] = "1";
        psi.Environment["PADDLE_PDX_DISABLE_MODEL_SOURCE_CHECK"] = "True";

        _process = Process.Start(psi);
        if (_process == null)
            throw new InvalidOperationException("无法启动 Python 进程");

        _log.Information("Python 进程已启动 (PID: {Pid})", _process.Id);

        // 异步读取输出，避免缓冲区阻塞
        _process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null) _log.Debug("[Python] {Line}", e.Data);
        };
        _process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null) _log.Debug("[Python:err] {Line}", e.Data);
        };
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        // 轮询健康检查，等待服务就绪
        await WaitForReadyAsync(ct);
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        if (_process == null || _process.HasExited)
        {
            _process = null;
            return;
        }

        _log.Information("停止 Python 服务 (PID: {Pid})", _process.Id);

        try
        {
            _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync(ct).WaitAsync(TimeSpan.FromSeconds(5), ct);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "停止 Python 进程时出错");
        }
        finally
        {
            _process.Dispose();
            _process = null;
        }
    }

    public async Task<bool> CheckHealthAsync(CancellationToken ct = default)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var resp = await http.GetAsync($"{_config.BaseUrl}/api/health", ct);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public string GetStatus()
    {
        if (_process == null)
            return "未启动";
        if (_process.HasExited)
            return $"已退出 (ExitCode: {_process.ExitCode})";
        return $"运行中 (PID: {_process.Id})";
    }

    /// <summary>
    /// 检测 Python 环境是否可用
    /// </summary>
    public async Task<(bool Available, string Message)> CheckEnvironmentAsync()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _config.PythonExe,
                Arguments = "--version",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var proc = Process.Start(psi);
            if (proc == null)
                return (false, $"无法启动 {_config.PythonExe}");

            string output = await proc.StandardOutput.ReadToEndAsync();
            string error = await proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();

            string version = string.IsNullOrEmpty(output) ? error : output;
            version = version.Trim();

            if (proc.ExitCode != 0)
                return (false, $"Python 检测失败: {version}");

            _log.Information("检测到 Python: {Version}", version);
            return (true, version);
        }
        catch (Exception ex)
        {
            return (false, $"Python 不可用: {ex.Message}\n请安装 Python 3.9+ 并确保 '{_config.PythonExe}' 在 PATH 中");
        }
    }

    private async Task WaitForReadyAsync(CancellationToken ct)
    {
        const int maxWaitSeconds = 60;
        const int pollIntervalMs = 500;
        var sw = Stopwatch.StartNew();

        _log.Information("等待 Python 服务就绪 (最长 {Max}s)...", maxWaitSeconds);

        while (sw.Elapsed.TotalSeconds < maxWaitSeconds)
        {
            ct.ThrowIfCancellationRequested();

            if (_process is { HasExited: true })
            {
                throw new InvalidOperationException(
                    $"Python 进程提前退出 (ExitCode: {_process.ExitCode})。请检查日志或手动运行 python run.py 确认依赖是否安装。");
            }

            if (await CheckHealthAsync(ct))
            {
                _log.Information("Python 服务就绪 ({Elapsed}ms)", sw.ElapsedMilliseconds);
                return;
            }

            await Task.Delay(pollIntervalMs, ct);
        }

        throw new TimeoutException(
            $"Python 服务在 {maxWaitSeconds}s 内未就绪。请手动运行:\n  cd {_config.PythonServiceDir} && {_config.PythonExe} run.py\n检查是否有错误输出。");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_process is { HasExited: false })
        {
            try
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(3000);
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Dispose 时停止 Python 进程出错");
            }
        }

        _process?.Dispose();
    }
}

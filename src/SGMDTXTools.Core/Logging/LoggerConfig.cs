using Serilog;
using Serilog.Events;

namespace SGMDTXTools.Core.Logging;

public static class LoggerConfig
{
    private const string OutputTemplate =
        "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}";

    public static ILogger CreateLogger(string logDirectory = "logs")
    {
        Directory.CreateDirectory(logDirectory);

        return new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.FromLogContext()
            .WriteTo.Console(
                restrictedToMinimumLevel: LogEventLevel.Information,
                outputTemplate: OutputTemplate)
            .WriteTo.File(
                path: Path.Combine(logDirectory, "sgmdtx-.log"),
                rollingInterval: RollingInterval.Day,
                fileSizeLimitBytes: 50 * 1024 * 1024, // 50MB
                retainedFileCountLimit: 30,
                outputTemplate: OutputTemplate,
                restrictedToMinimumLevel: LogEventLevel.Debug,
                shared: false,
                flushToDiskInterval: TimeSpan.FromSeconds(1))
            .CreateLogger();
    }
}

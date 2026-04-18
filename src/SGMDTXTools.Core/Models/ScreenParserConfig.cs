namespace SGMDTXTools.Core.Models;

public class ScreenParserConfig
{
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 5100;
    public int TimeoutMs { get; set; } = 30000;
    public string PythonExe { get; set; } = "python";
    public string PythonServiceDir { get; set; } = "python";

    public string BaseUrl => $"http://{Host}:{Port}";
}

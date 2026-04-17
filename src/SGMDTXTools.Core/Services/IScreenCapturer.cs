using SGMDTXTools.Core.Models;

namespace SGMDTXTools.Core.Services;

public interface IScreenCapturer : IDisposable
{
    CaptureResult Capture(WindowInfo window);
    string CaptureToFile(WindowInfo window, string outputDir);
    Task StartPeriodicCapture(WindowInfo window, TimeSpan interval, string outputDir, CancellationToken ct);
}

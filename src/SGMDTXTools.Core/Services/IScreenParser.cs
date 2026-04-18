using SGMDTXTools.Core.Models;

namespace SGMDTXTools.Core.Services;

public interface IScreenParser : IDisposable
{
    Task<bool> IsAvailableAsync(CancellationToken ct = default);
    Task<OcrResult> OcrAsync(string imagePath, CancellationToken ct = default);
    Task<OcrResult> OcrRegionAsync(string imagePath, int x, int y, int width, int height, CancellationToken ct = default);
    Task<MatchResult> MatchAsync(string imagePath, string[]? templates = null, CancellationToken ct = default);
    Task<ScanResult> ScanAsync(string imagePath, CancellationToken ct = default);
}

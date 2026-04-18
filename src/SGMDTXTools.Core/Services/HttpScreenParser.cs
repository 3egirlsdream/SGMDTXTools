using System.Net.Http.Json;
using System.Text.Json;
using Serilog;
using SGMDTXTools.Core.Models;

namespace SGMDTXTools.Core.Services;

public class HttpScreenParser : IScreenParser
{
    private readonly ILogger _log;
    private readonly HttpClient _http;
    private readonly ScreenParserConfig _config;
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    public HttpScreenParser(ILogger logger, ScreenParserConfig config)
    {
        _log = logger.ForContext<HttpScreenParser>();
        _config = config;
        _http = new HttpClient
        {
            BaseAddress = new Uri(config.BaseUrl),
            Timeout = TimeSpan.FromMilliseconds(config.TimeoutMs)
        };
    }

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.GetAsync("/api/health", ct);
            if (!resp.IsSuccessStatusCode) return false;

            var json = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
            return json.TryGetProperty("ocr_ready", out var ready) && ready.GetBoolean();
        }
        catch (Exception ex)
        {
            _log.Debug(ex, "Python 服务不可用");
            return false;
        }
    }

    public async Task<OcrResult> OcrAsync(string imagePath, CancellationToken ct = default)
    {
        return await PostImageAsync<OcrResult>("/api/ocr", imagePath, null, ct);
    }

    public async Task<OcrResult> OcrRegionAsync(string imagePath, int x, int y, int width, int height, CancellationToken ct = default)
    {
        var regionJson = JsonSerializer.Serialize(new { x, y, width, height });
        var extraFields = new Dictionary<string, string> { ["region"] = regionJson };
        return await PostImageAsync<OcrResult>("/api/ocr", imagePath, extraFields, ct);
    }

    public async Task<MatchResult> MatchAsync(string imagePath, string[]? templates = null, CancellationToken ct = default)
    {
        Dictionary<string, string>? extraFields = null;
        if (templates is { Length: > 0 })
        {
            extraFields = new Dictionary<string, string>
            {
                ["templates"] = JsonSerializer.Serialize(templates)
            };
        }
        return await PostImageAsync<MatchResult>("/api/match", imagePath, extraFields, ct);
    }

    public async Task<ScanResult> ScanAsync(string imagePath, CancellationToken ct = default)
    {
        return await PostImageAsync<ScanResult>("/api/scan", imagePath, null, ct);
    }

    private async Task<T> PostImageAsync<T>(string endpoint, string imagePath,
        Dictionary<string, string>? extraFields, CancellationToken ct) where T : new()
    {
        if (!File.Exists(imagePath))
        {
            _log.Error("图片文件不存在: {Path}", imagePath);
            throw new FileNotFoundException($"图片文件不存在: {imagePath}");
        }

        _log.Debug("POST {Endpoint} image={Path}", endpoint, imagePath);

        using var content = new MultipartFormDataContent();

        var imageBytes = await File.ReadAllBytesAsync(imagePath, ct);
        var imageContent = new ByteArrayContent(imageBytes);
        imageContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        content.Add(imageContent, "image", Path.GetFileName(imagePath));

        if (extraFields != null)
        {
            foreach (var (key, value) in extraFields)
            {
                content.Add(new StringContent(value), key);
            }
        }

        var response = await _http.PostAsync(endpoint, content, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _log.Error("Python 服务返回 {StatusCode}: {Body}", response.StatusCode, responseBody);
            throw new HttpRequestException($"Python 服务错误 ({response.StatusCode}): {responseBody}");
        }

        var result = JsonSerializer.Deserialize<T>(responseBody, JsonOptions);
        return result ?? new T();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _http.Dispose();
    }
}

using Serilog;

namespace SGMDTXTools.Core.Services;

/// <summary>
/// 知识库管理器：管理Markdown知识文件的读取、搜索和统计
/// </summary>
public class KnowledgeManager
{
    private readonly ILogger _log;
    private readonly string _knowledgeDir;

    public KnowledgeManager(ILogger logger, string knowledgeDir)
    {
        _log = logger.ForContext<KnowledgeManager>();
        _knowledgeDir = Path.GetFullPath(knowledgeDir);

        if (!Directory.Exists(_knowledgeDir))
        {
            Directory.CreateDirectory(_knowledgeDir);
            _log.Information("创建知识目录: {Dir}", _knowledgeDir);
        }

        _log.Debug("知识管理器初始化: 目录={Dir}", _knowledgeDir);
    }

    /// <summary>
    /// 列出所有知识文件
    /// </summary>
    public List<KnowledgeFileInfo> ListFiles()
    {
        var files = Directory.GetFiles(_knowledgeDir, "*.md")
            .OrderBy(f => Path.GetFileName(f))
            .Select(f =>
            {
                var fi = new FileInfo(f);
                string content = File.ReadAllText(f);
                int lineCount = content.Split('\n').Length;
                string title = ExtractTitle(content);

                return new KnowledgeFileInfo
                {
                    FileName = fi.Name,
                    FilePath = fi.FullName,
                    Title = title,
                    SizeBytes = fi.Length,
                    LineCount = lineCount,
                    LastModified = fi.LastWriteTime
                };
            })
            .ToList();

        _log.Debug("列出知识文件: 共{Count}个", files.Count);
        return files;
    }

    /// <summary>
    /// 读取指定知识文件内容
    /// </summary>
    public string? ReadFile(string fileNameOrTopic)
    {
        string filePath = ResolveFilePath(fileNameOrTopic);
        if (!File.Exists(filePath))
        {
            _log.Warning("知识文件不存在: {Path}", filePath);
            return null;
        }

        string content = File.ReadAllText(filePath);
        _log.Debug("读取知识文件: {Path}, 长度={Len}", filePath, content.Length);
        return content;
    }

    /// <summary>
    /// 读取所有知识文件，合并为一个字符串（用于LLM上下文注入）
    /// </summary>
    public string ReadAllAsContext()
    {
        var files = Directory.GetFiles(_knowledgeDir, "*.md")
            .OrderBy(f => Path.GetFileName(f))
            .ToArray();

        if (files.Length == 0)
        {
            _log.Warning("知识库为空");
            return string.Empty;
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# 游戏知识库");
        sb.AppendLine();

        foreach (var file in files)
        {
            string fileName = Path.GetFileNameWithoutExtension(file);
            string content = File.ReadAllText(file).Trim();

            sb.AppendLine($"---");
            sb.AppendLine($"## 文件: {fileName}");
            sb.AppendLine();
            sb.AppendLine(content);
            sb.AppendLine();
        }

        string result = sb.ToString();
        _log.Information("合并知识库: {Count}个文件, 总长度={Len}字符", files.Length, result.Length);
        return result;
    }

    /// <summary>
    /// 在知识库中搜索关键词
    /// </summary>
    public List<SearchResult> Search(string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
            return new List<SearchResult>();

        var results = new List<SearchResult>();
        var files = Directory.GetFiles(_knowledgeDir, "*.md");

        foreach (var file in files)
        {
            string content = File.ReadAllText(file);
            var lines = content.Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(new SearchResult
                    {
                        FileName = Path.GetFileName(file),
                        LineNumber = i + 1,
                        LineContent = lines[i].Trim(),
                        Context = GetLineContext(lines, i, 1)
                    });
                }
            }
        }

        _log.Information("知识库搜索: 关键词='{Keyword}', 匹配={Count}条", keyword, results.Count);
        return results;
    }

    /// <summary>
    /// 获取知识库统计信息
    /// </summary>
    public KnowledgeStats GetStats()
    {
        var files = Directory.GetFiles(_knowledgeDir, "*.md");
        long totalSize = 0;
        int totalLines = 0;

        foreach (var file in files)
        {
            var fi = new FileInfo(file);
            totalSize += fi.Length;
            totalLines += File.ReadAllLines(file).Length;
        }

        return new KnowledgeStats
        {
            FileCount = files.Length,
            TotalSizeBytes = totalSize,
            TotalLines = totalLines,
            KnowledgeDir = _knowledgeDir
        };
    }

    /// <summary>
    /// 追加内容到指定知识文件
    /// </summary>
    public void AppendToFile(string fileNameOrTopic, string content)
    {
        string filePath = ResolveFilePath(fileNameOrTopic);
        File.AppendAllText(filePath, "\n" + content);
        _log.Information("追加知识: {Path}, 追加长度={Len}", filePath, content.Length);
    }

    /// <summary>
    /// 更新知识文件内容（完全覆盖）
    /// </summary>
    public void WriteFile(string fileNameOrTopic, string content)
    {
        string filePath = ResolveFilePath(fileNameOrTopic);
        File.WriteAllText(filePath, content);
        _log.Information("写入知识: {Path}, 长度={Len}", filePath, content.Length);
    }

    private string ResolveFilePath(string fileNameOrTopic)
    {
        // 如果已有 .md 后缀
        if (fileNameOrTopic.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            return Path.Combine(_knowledgeDir, fileNameOrTopic);

        // 否则加上 .md
        return Path.Combine(_knowledgeDir, fileNameOrTopic + ".md");
    }

    private static string ExtractTitle(string content)
    {
        var lines = content.Split('\n');
        foreach (var line in lines)
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith("# ") && !trimmed.StartsWith("## "))
                return trimmed.Substring(2).Trim();
        }
        return "(无标题)";
    }

    private static string GetLineContext(string[] lines, int lineIndex, int contextLines)
    {
        int start = Math.Max(0, lineIndex - contextLines);
        int end = Math.Min(lines.Length - 1, lineIndex + contextLines);

        var contextParts = new List<string>();
        for (int i = start; i <= end; i++)
        {
            string prefix = i == lineIndex ? ">>> " : "    ";
            contextParts.Add($"{prefix}{lines[i].TrimEnd()}");
        }

        return string.Join("\n", contextParts);
    }
}

public class KnowledgeFileInfo
{
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public int LineCount { get; set; }
    public DateTime LastModified { get; set; }
}

public class SearchResult
{
    public string FileName { get; set; } = string.Empty;
    public int LineNumber { get; set; }
    public string LineContent { get; set; } = string.Empty;
    public string Context { get; set; } = string.Empty;
}

public class KnowledgeStats
{
    public int FileCount { get; set; }
    public long TotalSizeBytes { get; set; }
    public int TotalLines { get; set; }
    public string KnowledgeDir { get; set; } = string.Empty;
}

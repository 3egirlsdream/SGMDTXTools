using Serilog;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SGMDTXTools.Core.Services;

public class TemplateInfo
{
    [YamlMember(Alias = "name")]
    public string Name { get; set; } = string.Empty;

    [YamlMember(Alias = "file")]
    public string File { get; set; } = string.Empty;

    [YamlMember(Alias = "category")]
    public string Category { get; set; } = "other";

    [YamlMember(Alias = "threshold")]
    public double Threshold { get; set; } = 0.80;

    [YamlMember(Alias = "description")]
    public string Description { get; set; } = string.Empty;

    [YamlMember(Alias = "source_screenshot")]
    public string SourceScreenshot { get; set; } = string.Empty;

    [YamlMember(Alias = "source_region")]
    public Dictionary<string, int>? SourceRegion { get; set; }
}

public class TemplateStore
{
    private readonly ILogger _log;
    private readonly string _templatesDir;
    private readonly string _yamlPath;
    private readonly Dictionary<string, TemplateInfo> _templates = new();

    private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly ISerializer YamlSerializer = new SerializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .DisableAliases()
        .Build();

    public TemplateStore(ILogger logger, string templatesDir)
    {
        _log = logger.ForContext<TemplateStore>();
        _templatesDir = templatesDir;
        _yamlPath = Path.Combine(templatesDir, "templates.yaml");
        Load();
    }

    public void Load()
    {
        _templates.Clear();

        if (!System.IO.File.Exists(_yamlPath))
        {
            _log.Information("模板 YAML 不存在，创建空文件: {Path}", _yamlPath);
            Directory.CreateDirectory(_templatesDir);
            SaveYaml();
            return;
        }

        var text = System.IO.File.ReadAllText(_yamlPath);
        var data = YamlDeserializer.Deserialize<TemplateYamlRoot>(text);

        if (data?.Templates != null)
        {
            foreach (var info in data.Templates)
            {
                if (!string.IsNullOrEmpty(info.Name))
                    _templates[info.Name] = info;
            }
        }

        _log.Information("从 {Path} 加载了 {Count} 个模板", _yamlPath, _templates.Count);
    }

    public void Reload() => Load();

    public List<TemplateInfo> ListAll() => _templates.Values.ToList();

    public TemplateInfo? Get(string name) =>
        _templates.TryGetValue(name, out var info) ? info : null;

    public string? GetImagePath(string name)
    {
        var info = Get(name);
        if (info == null) return null;
        var path = Path.Combine(_templatesDir, info.File);
        return System.IO.File.Exists(path) ? path : null;
    }

    public int Count => _templates.Count;

    public string TemplatesDir => _templatesDir;

    private void SaveYaml()
    {
        var root = new TemplateYamlRoot
        {
            Templates = _templates.Values.ToList()
        };
        var yaml = YamlSerializer.Serialize(root);
        Directory.CreateDirectory(_templatesDir);
        System.IO.File.WriteAllText(_yamlPath, yaml);
    }

    private class TemplateYamlRoot
    {
        [YamlMember(Alias = "templates")]
        public List<TemplateInfo> Templates { get; set; } = [];
    }
}

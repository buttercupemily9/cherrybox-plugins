using System.Text.Json;
using CherryBox.Plugins.Abstractions;

namespace CherryBox.StoryCovers.Plugin;

internal sealed class StoryCoverSettings
{
    public bool BackgroundWorkerEnabled { get; set; } = true;
    public bool AutoGenerateOnIndex { get; set; } = true;
    public bool SkipWhenCoverExists { get; set; } = true;
    public bool UseChatPromptRefinement { get; set; } = true;
    public int ImageWidth { get; set; } = 768;
    public int ImageHeight { get; set; } = 1024;
    public int ContextCharLimit { get; set; } = 2500;
}

internal sealed class StoryCoverSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _path;
    private readonly object _lock = new();
    private StoryCoverSettings _settings;

    public StoryCoverSettingsStore(IPluginContext context)
    {
        _path = context.GetConfigFilePath("settings.json");
        _settings = Load();
    }

    public StoryCoverSettings Get()
    {
        lock (_lock)
            return Clone(_settings);
    }

    public StoryCoverSettings Update(Action<StoryCoverSettings> mutate)
    {
        lock (_lock)
        {
            mutate(_settings);
            File.WriteAllText(_path, JsonSerializer.Serialize(_settings, JsonOptions));
            return Clone(_settings);
        }
    }

    private StoryCoverSettings Load()
    {
        if (!File.Exists(_path))
            return new StoryCoverSettings();

        try
        {
            return JsonSerializer.Deserialize<StoryCoverSettings>(File.ReadAllText(_path), JsonOptions)
                ?? new StoryCoverSettings();
        }
        catch
        {
            return new StoryCoverSettings();
        }
    }

    private static StoryCoverSettings Clone(StoryCoverSettings settings) => new()
    {
        BackgroundWorkerEnabled = settings.BackgroundWorkerEnabled,
        AutoGenerateOnIndex = settings.AutoGenerateOnIndex,
        SkipWhenCoverExists = settings.SkipWhenCoverExists,
        UseChatPromptRefinement = settings.UseChatPromptRefinement,
        ImageWidth = settings.ImageWidth,
        ImageHeight = settings.ImageHeight,
        ContextCharLimit = settings.ContextCharLimit
    };
}

internal sealed class StoryCoverWorkerState
{
    private readonly object _lock = new();
    private bool _enabled = true;
    private bool _processing;
    private StoryCoverJobDto? _currentJob;

    public bool IsEnabled => _enabled;
    public bool IsProcessing => _processing;
    public StoryCoverJobDto? CurrentJob => _currentJob;

    public void SetEnabled(bool enabled)
    {
        lock (_lock)
            _enabled = enabled;
    }

    public void SetProcessing(StoryCoverJobDto? job, bool processing)
    {
        lock (_lock)
        {
            _currentJob = job;
            _processing = processing;
        }
    }
}

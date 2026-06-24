using System.Text.Json;
using CherryBox.Plugins.Abstractions;

namespace CherryBox.StoryTts.Plugin;

internal sealed class StoryTtsSettings
{
    public string? ApiKey { get; set; }
    public string Model { get; set; } = "tts-kokoro";
    public string Voice { get; set; } = "af_sky";
    public string ResponseFormat { get; set; } = "mp3";
    public double Speed { get; set; } = 1.0;
    public int MaxCharsPerRequest { get; set; } = 4000;
    public Guid? AudioLibraryFolderId { get; set; }
    public bool BackgroundWorkerEnabled { get; set; } = true;
    public bool AutoLinkOnComplete { get; set; } = true;
}

internal sealed class StoryTtsSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _path;
    private readonly object _lock = new();
    private StoryTtsSettings _settings;

    public StoryTtsSettingsStore(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        _path = Path.Combine(dataDirectory, "settings.json");
        _settings = Load();
    }

    public StoryTtsSettings Get()
    {
        lock (_lock)
            return Clone(_settings);
    }

    public StoryTtsSettings Update(Action<StoryTtsSettings> mutate)
    {
        lock (_lock)
        {
            mutate(_settings);
            File.WriteAllText(_path, JsonSerializer.Serialize(_settings, JsonOptions));
            return Clone(_settings);
        }
    }

    private StoryTtsSettings Load()
    {
        if (!File.Exists(_path))
            return new StoryTtsSettings();

        try
        {
            return JsonSerializer.Deserialize<StoryTtsSettings>(File.ReadAllText(_path), JsonOptions)
                ?? new StoryTtsSettings();
        }
        catch
        {
            return new StoryTtsSettings();
        }
    }

    private static StoryTtsSettings Clone(StoryTtsSettings settings) => new()
    {
        ApiKey = settings.ApiKey,
        Model = settings.Model,
        Voice = settings.Voice,
        ResponseFormat = settings.ResponseFormat,
        Speed = settings.Speed,
        MaxCharsPerRequest = settings.MaxCharsPerRequest,
        AudioLibraryFolderId = settings.AudioLibraryFolderId,
        BackgroundWorkerEnabled = settings.BackgroundWorkerEnabled,
        AutoLinkOnComplete = settings.AutoLinkOnComplete
    };
}

internal sealed class StoryTtsWorkerState
{
    private volatile bool _enabled;
    private StoryTtsJobDto? _currentJob;
    private volatile bool _processing;

    public bool IsEnabled => _enabled;
    public bool IsProcessing => _processing;
    public StoryTtsJobDto? CurrentJob => _currentJob;

    public void SetEnabled(bool enabled) => _enabled = enabled;

    public void SetProcessing(StoryTtsJobDto? job, bool processing)
    {
        _currentJob = job;
        _processing = processing;
    }
}

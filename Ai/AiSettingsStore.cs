using System.Text.Json;
using CherryBox.Plugins.Abstractions;

namespace CherryBox.Ai.Plugin;

internal sealed class AiSettings
{
    public string? ApiKey { get; set; }
    public string Model { get; set; } = "tts-kokoro";
    public string ChatModel { get; set; } = "venice-uncensored";
    public string ImageModel { get; set; } = "venice-sd35";
    public string Voice { get; set; } = "af_sky";
    public string ResponseFormat { get; set; } = "mp3";
    public double Speed { get; set; } = 1.0;
    public int MaxCharsPerRequest { get; set; } = 4000;
}

internal sealed class AiSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _path;
    private readonly object _lock = new();
    private AiSettings _settings;

    public AiSettingsStore(IPluginContext context)
    {
        _path = context.GetConfigFilePath("settings.json");
        _settings = Load();
    }

    public void ReloadFromDisk()
    {
        lock (_lock)
            _settings = Load();
    }

    public AiSettings Get()
    {
        lock (_lock)
            return Clone(_settings);
    }

    public AiSettings Update(Action<AiSettings> mutate)
    {
        lock (_lock)
        {
            mutate(_settings);
            SaveToDisk(_settings);
            return Clone(_settings);
        }
    }

    public bool HasApiKey
    {
        get
        {
            lock (_lock)
                return !string.IsNullOrWhiteSpace(_settings.ApiKey);
        }
    }

    private AiSettings Load()
    {
        if (!File.Exists(_path))
            return new AiSettings();

        try
        {
            return JsonSerializer.Deserialize<AiSettings>(File.ReadAllText(_path), JsonOptions)
                ?? new AiSettings();
        }
        catch
        {
            return new AiSettings();
        }
    }

    private void SaveToDisk(AiSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        var tempPath = _path + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _path, overwrite: true);
    }

    private static AiSettings Clone(AiSettings settings) => new()
    {
        ApiKey = settings.ApiKey,
        Model = settings.Model,
        ChatModel = settings.ChatModel,
        ImageModel = settings.ImageModel,
        Voice = settings.Voice,
        ResponseFormat = settings.ResponseFormat,
        Speed = settings.Speed,
        MaxCharsPerRequest = settings.MaxCharsPerRequest
    };
}

using System.Text.Json;
using CherryBox.Plugins.Abstractions;

namespace CherryBox.GoogleTagImages.Plugin;

internal sealed class GoogleTagImageSettings
{
    public string? ApiKey { get; set; }
    public string? SearchEngineId { get; set; }
    public string SearchQuerySuffix { get; set; } = string.Empty;
    public int MaxTagsPerRun { get; set; } = 50;
    public int RequestDelayMs { get; set; } = 500;
}

internal sealed class GoogleTagImageSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _path;
    private readonly object _lock = new();
    private GoogleTagImageSettings _settings;

    public GoogleTagImageSettingsStore(IPluginContext context)
    {
        _path = context.GetConfigFilePath("settings.json");
        _settings = Load();
    }

    public GoogleTagImageSettings Get()
    {
        lock (_lock)
            return Clone(_settings);
    }

    public GoogleTagImageSettings Update(Action<GoogleTagImageSettings> mutate)
    {
        lock (_lock)
        {
            mutate(_settings);
            SaveToDisk(_settings);
            return Clone(_settings);
        }
    }

    private GoogleTagImageSettings Load()
    {
        if (!File.Exists(_path))
            return new GoogleTagImageSettings();

        try
        {
            return JsonSerializer.Deserialize<GoogleTagImageSettings>(File.ReadAllText(_path), JsonOptions)
                ?? new GoogleTagImageSettings();
        }
        catch
        {
            return new GoogleTagImageSettings();
        }
    }

    private void SaveToDisk(GoogleTagImageSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        var tempPath = _path + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _path, overwrite: true);
    }

    private static GoogleTagImageSettings Clone(GoogleTagImageSettings settings) => new()
    {
        ApiKey = settings.ApiKey,
        SearchEngineId = settings.SearchEngineId,
        SearchQuerySuffix = settings.SearchQuerySuffix,
        MaxTagsPerRun = settings.MaxTagsPerRun,
        RequestDelayMs = settings.RequestDelayMs
    };
}

using System.Text.Json;
using CherryBox.Plugins.Abstractions;

namespace CherryBox.Newsletter.Plugin;

internal sealed class NewsletterSettings
{
    public bool WelcomeEnabled { get; set; } = true;
    public bool WeeklyEnabled { get; set; }
    public string WeeklyDay { get; set; } = "Sunday";
    public string WeeklyTime { get; set; } = "09:00";
    public string PublicBaseUrl { get; set; } = "http://localhost:8787";
    public string? LastWeeklySentAt { get; set; }
    public string? LastWeeklyPreparedWeekKey { get; set; }
}

internal sealed class NewsletterSettingsStore
{
    private readonly string _path;
    private readonly object _lock = new();
    private NewsletterSettings _current;

    public NewsletterSettingsStore(IPluginContext context)
    {
        _path = context.GetConfigFilePath("settings.json");
        _current = Load();
    }

    public NewsletterSettings Get() => Clone(_current);

    public void Save(NewsletterSettings settings)
    {
        lock (_lock)
        {
            _current = Clone(settings);
            File.WriteAllText(_path, JsonSerializer.Serialize(_current, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private NewsletterSettings Load()
    {
        if (!File.Exists(_path))
            return new NewsletterSettings();

        try
        {
            return JsonSerializer.Deserialize<NewsletterSettings>(File.ReadAllText(_path), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new NewsletterSettings();
        }
        catch
        {
            return new NewsletterSettings();
        }
    }

    private static NewsletterSettings Clone(NewsletterSettings settings) => new()
    {
        WelcomeEnabled = settings.WelcomeEnabled,
        WeeklyEnabled = settings.WeeklyEnabled,
        WeeklyDay = settings.WeeklyDay,
        WeeklyTime = settings.WeeklyTime,
        PublicBaseUrl = settings.PublicBaseUrl,
        LastWeeklySentAt = settings.LastWeeklySentAt,
        LastWeeklyPreparedWeekKey = settings.LastWeeklyPreparedWeekKey
    };
}

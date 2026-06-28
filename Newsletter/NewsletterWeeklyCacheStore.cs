using System.Text.Json;
using CherryBox.Plugins.Abstractions;

namespace CherryBox.Newsletter.Plugin;

internal sealed class WeeklyDigestVoiceVersion
{
    public string? AiIntro { get; set; }
}

internal sealed class WeeklyDigestCache
{
    public string WeekKey { get; set; } = string.Empty;
    public DateTimeOffset Since { get; set; }
    public DateTimeOffset GeneratedAt { get; set; }
    public List<NewsletterDigestItem> Items { get; set; } = [];
    public List<CachedEmbeddedImage> EmbeddedImages { get; set; } = [];
    public Dictionary<string, WeeklyDigestVoiceVersion> Versions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class CachedEmbeddedImage
{
    public string ContentId { get; set; } = string.Empty;
    public string MimeType { get; set; } = "image/jpeg";
    public string? FileName { get; set; }
    public string DataBase64 { get; set; } = string.Empty;

    public EmailEmbeddedImage ToEmbeddedImage() => new(
        ContentId,
        Convert.FromBase64String(DataBase64),
        MimeType,
        FileName);

    public static CachedEmbeddedImage From(EmailEmbeddedImage image) => new()
    {
        ContentId = image.ContentId,
        MimeType = image.MimeType,
        FileName = image.FileName,
        DataBase64 = Convert.ToBase64String(image.Data)
    };
}

internal sealed class NewsletterWeeklyCacheStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _cachePath;
    private readonly object _lock = new();

    public NewsletterWeeklyCacheStore(IPluginContext context)
    {
        _cachePath = context.GetConfigFilePath("weekly-digest-cache.json");
    }

    public WeeklyDigestCache? Load() =>
        !File.Exists(_cachePath)
            ? null
            : JsonSerializer.Deserialize<WeeklyDigestCache>(File.ReadAllText(_cachePath), JsonOptions);

    public WeeklyDigestCache? GetForWeek(string weekKey)
    {
        var cache = Load();
        return cache is not null && string.Equals(cache.WeekKey, weekKey, StringComparison.Ordinal)
            ? cache
            : null;
    }

    public bool IsReadyForWeek(string weekKey)
    {
        var cache = GetForWeek(weekKey);
        if (cache is null)
            return false;

        foreach (var (voice, audience, orientation) in NewsletterAiVariant.All())
        {
            if (!cache.Versions.ContainsKey(NewsletterAiVariant.CacheKey(voice, audience, orientation)))
                return false;
        }

        return true;
    }

    public void Save(WeeklyDigestCache cache)
    {
        lock (_lock)
        {
            File.WriteAllText(_cachePath, JsonSerializer.Serialize(cache, JsonOptions));
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            if (File.Exists(_cachePath))
                File.Delete(_cachePath);
        }
    }

}

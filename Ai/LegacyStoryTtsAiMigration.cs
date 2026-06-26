using System.Text.Json;
using CherryBox.Plugins.Abstractions;

namespace CherryBox.Ai.Plugin;

internal static class LegacyStoryTtsAiMigration
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static void TryImportFromStoryTts(string aiDataDirectory, AiSettingsStore store)
    {
        if (store.HasApiKey)
            return;

        var parent = Directory.GetParent(aiDataDirectory)?.FullName;
        if (parent is null)
            return;

        var legacyPath = Path.Combine(parent, "story-tts", "settings.json");
        if (!File.Exists(legacyPath))
            return;

        LegacyStoryTtsSettings? legacy;
        try
        {
            legacy = JsonSerializer.Deserialize<LegacyStoryTtsSettings>(File.ReadAllText(legacyPath), JsonOptions);
        }
        catch
        {
            return;
        }

        if (legacy is null || string.IsNullOrWhiteSpace(legacy.ApiKey))
            return;

        store.Update(settings =>
        {
            settings.ApiKey = legacy.ApiKey;
            if (!string.IsNullOrWhiteSpace(legacy.Model))
                settings.Model = legacy.Model.Trim();
            if (!string.IsNullOrWhiteSpace(legacy.Voice))
                settings.Voice = legacy.Voice.Trim();
            if (!string.IsNullOrWhiteSpace(legacy.ResponseFormat))
                settings.ResponseFormat = legacy.ResponseFormat.Trim();
            if (legacy.Speed > 0)
                settings.Speed = legacy.Speed;
            if (legacy.MaxCharsPerRequest > 0)
                settings.MaxCharsPerRequest = legacy.MaxCharsPerRequest;
        });
    }

    private sealed class LegacyStoryTtsSettings
    {
        public string? ApiKey { get; set; }
        public string? Model { get; set; }
        public string? Voice { get; set; }
        public string? ResponseFormat { get; set; }
        public double Speed { get; set; }
        public int MaxCharsPerRequest { get; set; }
    }
}

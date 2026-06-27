using System.Text.Json;
using CherryBox.Plugins.Abstractions;

namespace CherryBox.Ai.Plugin;

internal static class LegacyStoryTtsAiMigration
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static void TryImportFromStoryTts(string aiInstallDirectory, AiSettingsStore store)
    {
        if (store.HasApiKey)
            return;

        foreach (var legacyPath in ResolveStoryTtsSettingsPaths(aiInstallDirectory))
        {
            if (!File.Exists(legacyPath))
                continue;

            if (TryImportFromSettingsFile(legacyPath, store))
                return;
        }
    }

    private static IEnumerable<string> ResolveStoryTtsSettingsPaths(string aiInstallDirectory)
    {
        var pluginsRoot = Directory.GetParent(aiInstallDirectory)?.FullName;
        if (pluginsRoot is not null)
            yield return Path.Combine(pluginsRoot, "story-tts", "settings.json");

        var programDataRoot = Directory.GetParent(pluginsRoot ?? string.Empty)?.FullName;
        if (programDataRoot is null)
            yield break;

        yield return Path.Combine(programDataRoot, "config", "story-tts-settings.json");
        yield return Path.Combine(programDataRoot, "config", "story-tts", "settings.json");
    }

    private static bool TryImportFromSettingsFile(string legacyPath, AiSettingsStore store)
    {
        LegacyStoryTtsSettings? legacy;
        try
        {
            legacy = JsonSerializer.Deserialize<LegacyStoryTtsSettings>(File.ReadAllText(legacyPath), JsonOptions);
        }
        catch
        {
            return false;
        }

        if (legacy is null || string.IsNullOrWhiteSpace(legacy.ApiKey))
            return false;

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
        return true;
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

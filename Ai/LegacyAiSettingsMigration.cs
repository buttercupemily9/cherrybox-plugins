using System.Text.Json;
using CherryBox.Plugins.Abstractions;

namespace CherryBox.Ai.Plugin;

internal static class LegacyAiSettingsMigration
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static void EnsureSettingsFile(IPluginContext context)
    {
        var targetPath = context.GetSettingsFilePath();

        foreach (var sourcePath in EnumerateLegacySettingsPaths(context))
        {
            if (!File.Exists(sourcePath))
                continue;

            if (!File.Exists(targetPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                File.Copy(sourcePath, targetPath);
                return;
            }

            TryMergeApiKey(sourcePath, targetPath);
        }
    }

    private static IEnumerable<string> EnumerateLegacySettingsPaths(IPluginContext context)
    {
        yield return Path.Combine(context.InstallDirectory, "settings.json");

        var pluginsRoot = Directory.GetParent(context.InstallDirectory)?.FullName;
        if (pluginsRoot is null)
            yield break;

        foreach (var dir in Directory.EnumerateDirectories(pluginsRoot))
        {
            var name = Path.GetFileName(dir);
            if (!name.StartsWith("ai.replaced.", StringComparison.OrdinalIgnoreCase))
                continue;

            yield return Path.Combine(dir, "settings.json");
        }

        var programDataRoot = context.Storage.ProgramDataDirectory;
        yield return Path.Combine(programDataRoot, "plugin-data", "ai", "settings.json");
        yield return Path.Combine(programDataRoot, "config", "ai", "settings.json");
        yield return Path.Combine(programDataRoot, "config", "ai-settings.json");
        yield return Path.Combine(pluginsRoot, "story-tts", "settings.json");
        yield return Path.Combine(programDataRoot, "plugin-data", "story-tts", "settings.json");
        yield return Path.Combine(programDataRoot, "config", "story-tts", "settings.json");
        yield return Path.Combine(programDataRoot, "config", "story-tts-settings.json");
    }

    private static void TryMergeApiKey(string sourcePath, string targetPath)
    {
        try
        {
            using var sourceDoc = JsonDocument.Parse(File.ReadAllText(sourcePath));
            using var targetDoc = JsonDocument.Parse(File.ReadAllText(targetPath));

            if (!TryGetNonEmptyString(sourceDoc.RootElement, "apiKey", out var apiKey))
                return;

            if (TryGetNonEmptyString(targetDoc.RootElement, "apiKey", out _))
                return;

            var merged = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(targetPath), JsonOptions)
                ?? new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            merged["apiKey"] = JsonSerializer.SerializeToElement(apiKey);
            File.WriteAllText(targetPath, JsonSerializer.Serialize(merged, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Best-effort recovery only.
        }
    }

    private static bool TryGetNonEmptyString(JsonElement root, string propertyName, out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(propertyName, out var property))
            return false;

        if (property.ValueKind != JsonValueKind.String)
            return false;

        value = property.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }
}

namespace CherryBox.Plugins.Abstractions;

/// <summary>
/// Resolves persistent plugin storage under the CherryBox data root (next to cherrybox.db).
/// </summary>
public sealed class PluginStoragePaths
{
    public const string DefaultConfigDirectoryName = "config";

    public PluginStoragePaths(string programDataDirectory, string pluginId, string configDirectoryName = DefaultConfigDirectoryName)
    {
        if (string.IsNullOrWhiteSpace(programDataDirectory))
            throw new ArgumentException("Program data directory is required.", nameof(programDataDirectory));
        if (string.IsNullOrWhiteSpace(pluginId))
            throw new ArgumentException("Plugin id is required.", nameof(pluginId));

        ProgramDataDirectory = Path.GetFullPath(programDataDirectory);
        PluginId = pluginId.Trim();
        ConfigDirectoryName = string.IsNullOrWhiteSpace(configDirectoryName)
            ? DefaultConfigDirectoryName
            : configDirectoryName.Trim();
    }

    public string ProgramDataDirectory { get; }
    public string PluginId { get; }
    public string ConfigDirectoryName { get; }

    /// <summary>Shared config folder: {ProgramData}/config/</summary>
    public string ConfigDirectory =>
        Path.Combine(ProgramDataDirectory, ConfigDirectoryName);

    public string GetSettingsFilePath() => GetConfigFilePath("settings.json");

    public string GetConfigFilePath(string fileName)
    {
        Directory.CreateDirectory(ConfigDirectory);
        return Path.Combine(ConfigDirectory, ResolveConfigFileName(PluginId, fileName));
    }

    public string GetConfigSubdirectory(string directoryName)
    {
        if (string.IsNullOrWhiteSpace(directoryName))
            throw new ArgumentException("Directory name is required.", nameof(directoryName));

        var path = Path.Combine(ConfigDirectory, $"{PluginId}-{directoryName.Trim()}");
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>SQLite databases live next to cherrybox.db: {ProgramData}/{pluginId}-{name}.db</summary>
    public string GetDatabasePath(string databaseName)
    {
        if (string.IsNullOrWhiteSpace(databaseName))
            throw new ArgumentException("Database name is required.", nameof(databaseName));

        var normalized = databaseName.Trim();
        if (normalized.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[..^3];

        var prefixed = $"{PluginId}-{normalized}";
        if (normalized.StartsWith($"{PluginId}-", StringComparison.OrdinalIgnoreCase))
            prefixed = normalized;

        Directory.CreateDirectory(ProgramDataDirectory);
        return Path.Combine(ProgramDataDirectory, prefixed + ".db");
    }

    public static string ResolveConfigFileName(string pluginId, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("File name is required.", nameof(fileName));

        var trimmed = fileName.Trim();
        var extension = Path.GetExtension(trimmed);
        var baseName = Path.GetFileNameWithoutExtension(trimmed);

        if (baseName.Equals("settings", StringComparison.OrdinalIgnoreCase))
            return $"{pluginId}-settings.json";

        if (trimmed.Equals($"{pluginId}-settings.json", StringComparison.OrdinalIgnoreCase))
            return trimmed;

        if (baseName.StartsWith($"{pluginId}-", StringComparison.OrdinalIgnoreCase))
            return trimmed;

        return string.IsNullOrEmpty(extension)
            ? $"{pluginId}-{baseName}"
            : $"{pluginId}-{baseName}{extension}";
    }

    public static string ResolveLegacyDatabaseTarget(string programDataDirectory, string pluginId, string fileName)
    {
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        if (baseName.StartsWith($"{pluginId}-", StringComparison.OrdinalIgnoreCase))
            return Path.Combine(programDataDirectory, baseName + ".db");

        return Path.Combine(programDataDirectory, $"{pluginId}-{baseName}.db");
    }
}

using System.Text.Json;
using CherryBox.Plugins.Abstractions;

namespace CherryBox.PasswordReset.Plugin;

internal sealed class PasswordResetSettings
{
    public bool Enabled { get; set; }
    public string PublicBaseUrl { get; set; } = "http://localhost:8787";
    public int TokenLifetimeMinutes { get; set; } = 60;
}

internal sealed class PasswordResetSettingsStore
{
    private readonly string _path;
    private readonly object _lock = new();
    private PasswordResetSettings _current;

    public PasswordResetSettingsStore(IPluginContext context)
    {
        _path = context.GetConfigFilePath("settings.json");
        _current = Load();
    }

    public PasswordResetSettings Get() => Clone(_current);

    public void Save(PasswordResetSettings settings)
    {
        lock (_lock)
        {
            _current = Clone(settings);
            File.WriteAllText(_path, JsonSerializer.Serialize(_current, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private PasswordResetSettings Load()
    {
        if (!File.Exists(_path))
            return new PasswordResetSettings();

        try
        {
            return JsonSerializer.Deserialize<PasswordResetSettings>(File.ReadAllText(_path), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new PasswordResetSettings();
        }
        catch
        {
            return new PasswordResetSettings();
        }
    }

    private static PasswordResetSettings Clone(PasswordResetSettings settings) => new()
    {
        Enabled = settings.Enabled,
        PublicBaseUrl = settings.PublicBaseUrl,
        TokenLifetimeMinutes = settings.TokenLifetimeMinutes
    };
}

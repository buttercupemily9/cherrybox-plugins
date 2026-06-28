using System.Text.Json;
using CherryBox.Plugins.Abstractions;

namespace CherryBox.Email.Plugin;

internal sealed class EmailSettings
{
    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 587;
    public bool UseTls { get; set; } = true;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string FromAddress { get; set; } = string.Empty;
    public string FromDisplayName { get; set; } = "CherryBox";
}

internal sealed class EmailSettingsStore
{
    private readonly string _path;
    private readonly object _lock = new();
    private EmailSettings _current;

    public EmailSettingsStore(IPluginContext context)
    {
        _path = context.GetConfigFilePath("settings.json");
        _current = Load();
    }

    public EmailSettings Get() => Clone(_current);

    public void Save(EmailSettings settings)
    {
        lock (_lock)
        {
            _current = Clone(settings);
            File.WriteAllText(_path, JsonSerializer.Serialize(_current, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    public bool IsEmpty() =>
        string.IsNullOrWhiteSpace(_current.SmtpHost) && string.IsNullOrWhiteSpace(_current.FromAddress);

    private EmailSettings Load()
    {
        if (!File.Exists(_path))
            return new EmailSettings();

        try
        {
            return JsonSerializer.Deserialize<EmailSettings>(File.ReadAllText(_path), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new EmailSettings();
        }
        catch
        {
            return new EmailSettings();
        }
    }

    private static EmailSettings Clone(EmailSettings settings) => new()
    {
        SmtpHost = settings.SmtpHost,
        SmtpPort = settings.SmtpPort,
        UseTls = settings.UseTls,
        Username = settings.Username,
        Password = settings.Password,
        FromAddress = settings.FromAddress,
        FromDisplayName = settings.FromDisplayName
    };
}

internal sealed class LegacyPasswordResetSettings
{
    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 587;
    public bool UseTls { get; set; } = true;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string FromAddress { get; set; } = string.Empty;
    public string FromDisplayName { get; set; } = "CherryBox";
}

internal static class LegacyEmailMigration
{
    public static bool TryImportSmtpFromPasswordReset(string pluginDirectory, EmailSettingsStore store)
    {
        if (!store.IsEmpty())
            return false;

        var legacyPath = Path.Combine(Path.GetDirectoryName(pluginDirectory)!, "password-reset", "settings.json");
        if (!File.Exists(legacyPath))
            return false;

        try
        {
            var legacy = JsonSerializer.Deserialize<LegacyPasswordResetSettings>(
                File.ReadAllText(legacyPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (legacy is null || string.IsNullOrWhiteSpace(legacy.SmtpHost))
                return false;

            store.Save(new EmailSettings
            {
                SmtpHost = legacy.SmtpHost,
                SmtpPort = legacy.SmtpPort,
                UseTls = legacy.UseTls,
                Username = legacy.Username,
                Password = legacy.Password,
                FromAddress = legacy.FromAddress,
                FromDisplayName = legacy.FromDisplayName
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void TryImportUserEmailsDb(string pluginInstallDirectory, IPluginContext context, UserEmailStore emailStore)
    {
        var legacyDb = Path.Combine(Path.GetDirectoryName(pluginInstallDirectory)!, "password-reset", "user-emails.db");
        var targetDb = context.GetDatabasePath("user-emails");
        if (!File.Exists(legacyDb) || File.Exists(targetDb))
            return;

        try
        {
            File.Copy(legacyDb, targetDb);
            emailStore.EnsureSchema();
        }
        catch
        {
            // best-effort migration
        }
    }
}

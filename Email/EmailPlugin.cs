using CherryBox.Data;
using CherryBox.Plugins.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CherryBox.Email.Plugin;

public sealed class EmailPlugin : ICherryBoxPlugin, IPluginServiceContributor
{
    private UserEmailStore? _emailStore;
    private EmailSettingsStore? _settingsStore;
    private string? _pluginDirectory;

    public string Id => "email";
    public string Name => "Email";
    public string Version => "1.0.0";

    public Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public void RegisterServices(IPluginServiceRegistry registry, IPluginContext context)
    {
        _pluginDirectory = context.DataDirectory;
        _settingsStore = new EmailSettingsStore(context.DataDirectory);
        _emailStore = new UserEmailStore(context.DataDirectory);
        var emailSender = new SmtpEmailSender();

        registry.RegisterSingleton(_settingsStore);
        registry.RegisterSingleton(_emailStore);
        registry.RegisterSingleton(emailSender);
        registry.RegisterScoped<IEmailService>(sp => new EmailService(_settingsStore, _emailStore, emailSender));
    }

    public async Task StartAsync(IPluginContext context, CancellationToken cancellationToken = default)
    {
        if (_emailStore is null || _settingsStore is null || string.IsNullOrWhiteSpace(_pluginDirectory))
            return;

        LegacyEmailMigration.TryImportSmtpFromPasswordReset(_pluginDirectory, _settingsStore);
        LegacyEmailMigration.TryImportUserEmailsDb(_pluginDirectory, _emailStore);

        await using var scope = context.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CherryBoxDbContext>();
        if (!_emailStore.HasAnyEmails())
            await _emailStore.ImportLegacyEmailsAsync(db, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

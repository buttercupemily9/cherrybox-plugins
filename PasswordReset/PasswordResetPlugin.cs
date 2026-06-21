using CherryBox.Data;
using CherryBox.Plugins.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CherryBox.PasswordReset.Plugin;

public sealed class PasswordResetPlugin : ICherryBoxPlugin, IPluginServiceContributor
{
    private UserEmailStore? _emailStore;

    public string Id => "password-reset";
    public string Name => "Email password reset";
    public string Version => "1.0.0";

    public Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public void RegisterServices(IPluginServiceRegistry registry, IPluginContext context)
    {
        var settingsStore = new PasswordResetSettingsStore(context.DataDirectory);
        var tokenStore = new ResetTokenStore(context.DataDirectory);
        _emailStore = new UserEmailStore(context.DataDirectory);
        var emailSender = new SmtpEmailSender();

        registry.RegisterSingleton(settingsStore);
        registry.RegisterSingleton(tokenStore);
        registry.RegisterSingleton(_emailStore);
        registry.RegisterSingleton(emailSender);
        registry.RegisterScoped<IPasswordResetService>(sp => new PasswordResetService(
            sp.GetRequiredService<CherryBoxDbContext>(),
            sp.GetRequiredService<CherryBox.Auth.IAuthService>(),
            settingsStore,
            tokenStore,
            _emailStore,
            emailSender,
            sp.GetRequiredService<ILogger<PasswordResetService>>()));
    }

    public async Task StartAsync(IPluginContext context, CancellationToken cancellationToken = default)
    {
        if (_emailStore is null)
            return;

        await using var scope = context.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CherryBoxDbContext>();
        await _emailStore.ImportLegacyEmailsAsync(db, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

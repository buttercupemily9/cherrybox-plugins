using CherryBox.Plugins.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CherryBox.PasswordReset.Plugin;

public sealed class PasswordResetPlugin : ICherryBoxPlugin, IPluginServiceContributor
{
    public string Id => "password-reset";
    public string Name => "Email password reset";
    public string Version => "1.0.0";

    public Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public void RegisterServices(IPluginServiceRegistry registry, IPluginContext context)
    {
        var services = context.Services;
        var settingsStore = new PasswordResetSettingsStore(context.DataDirectory);
        var tokenStore = new ResetTokenStore(context.DataDirectory);
        var emailSender = new SmtpEmailSender();

        registry.RegisterSingleton(settingsStore);
        registry.RegisterSingleton(tokenStore);
        registry.RegisterSingleton(emailSender);
        registry.RegisterScoped<IPasswordResetService>(sp => new PasswordResetService(
            sp.GetRequiredService<CherryBox.Data.CherryBoxDbContext>(),
            sp.GetRequiredService<CherryBox.Auth.IAuthService>(),
            settingsStore,
            tokenStore,
            emailSender,
            sp.GetRequiredService<ILogger<PasswordResetService>>()));
    }

    public Task StartAsync(IPluginContext context, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

using CherryBox.Plugins.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CherryBox.PasswordReset.Plugin;

public sealed class PasswordResetPlugin : ICherryBoxPlugin, IPluginServiceContributor, IPluginSchemaContributor
{
    public string Id => "password-reset";
    public string Name => "Password reset";
    public string Version => "1.1.0";

    public Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public void RegisterServices(IPluginServiceRegistry registry, IPluginContext context)
    {
        var settingsStore = new PasswordResetSettingsStore(context);
        var tokenStore = new ResetTokenStore(context);

        registry.RegisterSingleton(settingsStore);
        registry.RegisterSingleton(tokenStore);
        registry.RegisterScoped<IPasswordResetService>(sp =>
        {
            var email = sp.GetRequiredService<IPluginServiceRegistry>().Resolve<IEmailService>(sp)
                ?? throw new InvalidOperationException("Email plugin is required for password reset.");
            return new PasswordResetService(
                sp.GetRequiredService<CherryBox.Data.CherryBoxDbContext>(),
                sp.GetRequiredService<CherryBox.Auth.IAuthService>(),
                sp.GetRequiredService<CherryBox.Core.Configuration.IConfigManager>(),
                settingsStore,
                tokenStore,
                email,
                sp.GetRequiredService<ILogger<PasswordResetService>>());
        });
    }

    public Task StartAsync(IPluginContext context, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public IReadOnlyList<PluginDatabaseSchema> GetDatabaseSchemas() => [ResetTokenDatabase.Schema];
}

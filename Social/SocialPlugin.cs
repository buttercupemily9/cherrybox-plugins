using CherryBox.Data;
using CherryBox.Plugins.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace CherryBox.Social.Plugin;

public sealed class SocialPlugin : ICherryBoxPlugin, IPluginServiceContributor
{
    public string Id => "social";
    public string Name => "Social";
    public string Version => "1.0.0";

    public Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public void RegisterServices(IPluginServiceRegistry registry, IPluginContext context)
    {
        var store = new SocialStore(context);
        registry.RegisterSingleton(store);
        registry.RegisterScoped<ISocialService>(sp => new SocialService(
            sp.GetRequiredService<CherryBoxDbContext>(),
            store));
    }

    public Task StartAsync(IPluginContext context, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

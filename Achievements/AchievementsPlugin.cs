using CherryBox.Data;
using CherryBox.Plugins.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace CherryBox.Achievements.Plugin;

public sealed class AchievementsPlugin : ICherryBoxPlugin, IPluginServiceContributor
{
    public string Id => "achievements";
    public string Name => "Achievements";
    public string Version => "1.0.0";

    public Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public void RegisterServices(IPluginServiceRegistry registry, IPluginContext context)
    {
        var store = new AchievementStore(context);
        registry.RegisterSingleton(store);
        registry.RegisterScoped<IAchievementService>(sp => new AchievementService(
            sp.GetRequiredService<CherryBoxDbContext>(),
            store));
    }

    public Task StartAsync(IPluginContext context, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

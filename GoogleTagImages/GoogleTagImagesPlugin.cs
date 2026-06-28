using CherryBox.Plugins.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace CherryBox.GoogleTagImages.Plugin;

public sealed class GoogleTagImagesPlugin : ICherryBoxPlugin, IPluginServiceContributor
{
    public string Id => "google-tag-images";
    public string Name => "Google tag images";
    public string Version => "1.1.0";

    public Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public void RegisterServices(IPluginServiceRegistry registry, IPluginContext context)
    {
        var settingsStore = new GoogleTagImageSettingsStore(context);
        var googleSearch = new GoogleCustomSearchClient(new HttpClient { Timeout = TimeSpan.FromSeconds(30) });
        var serperSearch = new SerperImageSearchClient(new HttpClient { Timeout = TimeSpan.FromSeconds(30) });
        var service = new GoogleTagImageService(settingsStore, googleSearch, serperSearch);

        registry.RegisterSingleton(settingsStore);
        registry.RegisterSingleton(googleSearch);
        registry.RegisterSingleton(serperSearch);
        registry.RegisterSingleton(service);
        registry.RegisterScoped<IGoogleTagImageService>(sp =>
            sp.GetRequiredService<IPluginServiceRegistry>().Resolve<GoogleTagImageService>(sp)!);
    }

    public Task StartAsync(IPluginContext context, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

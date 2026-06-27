using CherryBox.Plugins.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace CherryBox.Ai.Plugin;

public sealed class AiPlugin : ICherryBoxPlugin, IPluginServiceContributor
{
    private AiSettingsStore? _settingsStore;

    public string Id => "ai";
    public string Name => "AI";
    public string Version => "1.2.2";

    public Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public void RegisterServices(IPluginServiceRegistry registry, IPluginContext context)
    {
        LegacyAiSettingsMigration.EnsureSettingsFile(context);
        _settingsStore = new AiSettingsStore(context);
        var venice = new VeniceTtsClient(new HttpClient { Timeout = TimeSpan.FromMinutes(10) });
        var chat = new VeniceChatClient(new HttpClient { Timeout = TimeSpan.FromMinutes(2) });
        var images = new VeniceImageClient(new HttpClient { Timeout = TimeSpan.FromMinutes(5) });
        var aiService = new AiService(_settingsStore, venice, chat, images);

        registry.RegisterSingleton(_settingsStore);
        registry.RegisterSingleton(venice);
        registry.RegisterSingleton(chat);
        registry.RegisterSingleton(images);
        registry.RegisterSingleton(aiService);
        registry.RegisterScoped<IAiService>(sp =>
            sp.GetRequiredService<IPluginServiceRegistry>().Resolve<AiService>(sp)!);
        registry.RegisterScoped<IAiImageService>(sp =>
            sp.GetRequiredService<IPluginServiceRegistry>().Resolve<AiService>(sp)!);
    }

    public Task StartAsync(IPluginContext context, CancellationToken cancellationToken = default)
    {
        if (_settingsStore is null)
            return Task.CompletedTask;

        LegacyAiSettingsMigration.EnsureSettingsFile(context);
        _settingsStore.ReloadFromDisk();
        LegacyStoryTtsAiMigration.TryImportFromStoryTts(context.InstallDirectory, _settingsStore);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

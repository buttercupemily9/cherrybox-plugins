using CherryBox.Plugins.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CherryBox.StoryTts.Plugin;

public sealed class StoryTtsPlugin : ICherryBoxPlugin, IPluginServiceContributor
{
    private Task? _workerTask;
    private CancellationTokenSource? _workerCts;

    public string Id => "story-tts";
    public string Name => "Story text-to-speech";
    public string Version => "1.1.0";

    public Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public void RegisterServices(IPluginServiceRegistry registry, IPluginContext context)
    {
        var services = context.Services;
        var dataDirectory = context.DataDirectory;
        var settingsStore = new StoryTtsSettingsStore(dataDirectory);
        var jobStore = new StoryTtsJobStore(dataDirectory);
        var workerState = new StoryTtsWorkerState();
        workerState.SetEnabled(settingsStore.Get().BackgroundWorkerEnabled);

        var plugins = services.GetRequiredService<IPluginServiceRegistry>();
        var executor = new StoryTtsExecutor(
            services.GetRequiredService<IServiceScopeFactory>(),
            settingsStore,
            plugins,
            services.GetRequiredService<ILogger<StoryTtsExecutor>>());
        var storyTtsService = new StoryTtsService(
            services.GetRequiredService<IServiceScopeFactory>(),
            settingsStore,
            jobStore,
            workerState,
            services.GetRequiredService<ILogger<StoryTtsService>>());
        var worker = new StoryTtsWorker(
            jobStore,
            executor,
            workerState,
            settingsStore,
            services.GetRequiredService<ILogger<StoryTtsWorker>>());

        registry.RegisterSingleton(settingsStore);
        registry.RegisterSingleton(jobStore);
        registry.RegisterSingleton(workerState);
        registry.RegisterSingleton(executor);
        registry.RegisterSingleton(storyTtsService);
        registry.RegisterSingleton(worker);
        registry.RegisterScoped<IStoryTtsService>(sp =>
            sp.GetRequiredService<IPluginServiceRegistry>().Resolve<StoryTtsService>(sp)!);
    }

    public Task StartAsync(IPluginContext context, CancellationToken cancellationToken = default)
    {
        var registry = context.Services.GetRequiredService<IPluginServiceRegistry>();
        var worker = registry.Resolve<StoryTtsWorker>(context.Services);
        if (worker is null) return Task.CompletedTask;

        _workerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        registry.RegisterStopCallback(async ct =>
        {
            if (_workerCts is null) return;
            await _workerCts.CancelAsync();
            if (_workerTask is not null)
            {
                try { await _workerTask.WaitAsync(TimeSpan.FromSeconds(30), ct); }
                catch { /* best effort */ }
            }
        });
        _workerTask = Task.Run(() => worker.RunAsync(_workerCts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_workerCts is not null)
        {
            await _workerCts.CancelAsync();
            if (_workerTask is not null)
            {
                try { await _workerTask.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken); }
                catch { /* best effort */ }
            }
        }
    }
}

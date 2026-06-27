using CherryBox.Core.Enums;
using CherryBox.Data;
using CherryBox.Plugins.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CherryBox.StoryCovers.Plugin;

public sealed class StoryCoversPlugin : ICherryBoxPlugin, IPluginServiceContributor, ILibraryHook
{
    private Task? _workerTask;
    private CancellationTokenSource? _workerCts;
    private IServiceScopeFactory? _scopeFactory;
    private StoryCoverSettingsStore? _settingsStore;
    private StoryCoverExecutor? _executor;

    public string Id => "story-covers";
    public string Name => "Story covers";
    public string Version => "1.0.0";

    public Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public void RegisterServices(IPluginServiceRegistry registry, IPluginContext context)
    {
        var services = context.Services;
        var dataDirectory = context.DataDirectory;
        var settingsStore = new StoryCoverSettingsStore(dataDirectory);
        var jobStore = new StoryCoverJobStore(dataDirectory);
        var workerState = new StoryCoverWorkerState();
        workerState.SetEnabled(settingsStore.Get().BackgroundWorkerEnabled);

        var plugins = services.GetRequiredService<IPluginServiceRegistry>();
        var executor = new StoryCoverExecutor(
            services.GetRequiredService<IServiceScopeFactory>(),
            settingsStore,
            plugins,
            services.GetRequiredService<ILogger<StoryCoverExecutor>>());
        var coverService = new StoryCoverService(
            services.GetRequiredService<IServiceScopeFactory>(),
            settingsStore,
            jobStore,
            workerState,
            services.GetRequiredService<ILogger<StoryCoverService>>());
        var worker = new StoryCoverWorker(
            jobStore,
            executor,
            workerState,
            settingsStore,
            services.GetRequiredService<ILogger<StoryCoverWorker>>());

        registry.RegisterSingleton(settingsStore);
        registry.RegisterSingleton(jobStore);
        registry.RegisterSingleton(workerState);
        registry.RegisterSingleton(executor);
        registry.RegisterSingleton(coverService);
        registry.RegisterSingleton(worker);
        registry.RegisterScoped<IStoryCoverService>(sp =>
            sp.GetRequiredService<IPluginServiceRegistry>().Resolve<StoryCoverService>(sp)!);
    }

    public Task StartAsync(IPluginContext context, CancellationToken cancellationToken = default)
    {
        _scopeFactory = context.Services.GetRequiredService<IServiceScopeFactory>();
        var registry = context.Services.GetRequiredService<IPluginServiceRegistry>();
        _settingsStore = registry.Resolve<StoryCoverSettingsStore>(context.Services);
        _executor = registry.Resolve<StoryCoverExecutor>(context.Services);

        var worker = registry.Resolve<StoryCoverWorker>(context.Services);
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

    public async Task OnMediaIndexedAsync(Guid mediaItemId, CancellationToken cancellationToken = default)
    {
        if (_scopeFactory is null || _settingsStore is null || _executor is null)
            return;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CherryBoxDbContext>();
        var item = await db.MediaItems.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == mediaItemId, cancellationToken);
        if (item is null)
            return;

        if (item.MediaType == MediaType.Audio)
        {
            await _executor.SyncCoverFromLinkedStoryAsync(mediaItemId, cancellationToken);
            return;
        }

        if (item.MediaType != MediaType.Story || !_settingsStore.Get().AutoGenerateOnIndex)
            return;

        var service = scope.ServiceProvider.GetRequiredService<IPluginServiceRegistry>()
            .Resolve<StoryCoverService>(scope.ServiceProvider);
        if (service is null)
            return;

        try
        {
            await service.EnqueueAsync(new EnqueueStoryCoverRequest(mediaItemId), cancellationToken);
        }
        catch
        {
            // Ignore duplicate queue attempts during indexing.
        }
    }
}

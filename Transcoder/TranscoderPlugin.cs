using CherryBox.Plugins.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CherryBox.Transcoder.Plugin;

public sealed class TranscoderPlugin : ICherryBoxPlugin, IPluginServiceContributor, ILibraryHook
{
    private Task? _workerTask;
    private CancellationTokenSource? _workerCts;
    private TranscodeService? _service;

    public string Id => "transcoder";
    public string Name => "Media transcoder";
    public string Version => "1.0.0";

    public Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public void RegisterServices(IPluginServiceRegistry registry, IPluginContext context)
    {
        var services = context.Services;
        var profileStore = new TranscodeProfileStore(context);
        var assignmentsStore = new TranscodeAssignmentsStore(context);
        var jobStore = new TranscodeJobStore(context);
        var workerState = new TranscodeWorkerState();
        workerState.SetEnabled(assignmentsStore.Get().BackgroundWorkerEnabled);

        var executor = new TranscodeExecutor(
            services.GetRequiredService<IServiceScopeFactory>(),
            profileStore,
            services.GetRequiredService<ILogger<TranscodeExecutor>>());
        var transcodeService = new TranscodeService(
            services.GetRequiredService<IServiceScopeFactory>(),
            profileStore,
            assignmentsStore,
            jobStore,
            workerState,
            services.GetRequiredService<ILogger<TranscodeService>>());
        var worker = new TranscodeWorker(
            services.GetRequiredService<IServiceScopeFactory>(),
            jobStore,
            executor,
            workerState,
            assignmentsStore,
            services.GetRequiredService<ILogger<TranscodeWorker>>());

        registry.RegisterSingleton(profileStore);
        registry.RegisterSingleton(assignmentsStore);
        registry.RegisterSingleton(jobStore);
        registry.RegisterSingleton(workerState);
        registry.RegisterSingleton(executor);
        registry.RegisterSingleton(transcodeService);
        registry.RegisterSingleton(worker);
        registry.RegisterScoped<ITranscodeService>(sp => sp.GetRequiredService<IPluginServiceRegistry>().Resolve<TranscodeService>(sp)!);
    }

    public Task StartAsync(IPluginContext context, CancellationToken cancellationToken = default)
    {
        var registry = context.Services.GetRequiredService<IPluginServiceRegistry>();
        _service = registry.Resolve<TranscodeService>(context.Services);

        var worker = registry.Resolve<TranscodeWorker>(context.Services);
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

    public Task OnMediaIndexedAsync(Guid mediaItemId, CancellationToken cancellationToken = default) =>
        _service?.EnqueueForMediaAsync(mediaItemId, cancellationToken) ?? Task.CompletedTask;
}

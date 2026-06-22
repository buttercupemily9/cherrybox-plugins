using CherryBox.Plugins.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CherryBox.Download.Plugin;

public sealed class DownloadPlugin : ICherryBoxPlugin, IPluginServiceContributor
{
    private Task? _workerTask;
    private CancellationTokenSource? _workerCts;

    public string Id => "download";
    public string Name => "Video downloader";
    public string Version => "1.0.0";

    public Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public void RegisterServices(IPluginServiceRegistry registry, IPluginContext context)
    {
        var services = context.Services;
        var jobTracker = new DownloadJobTracker();
        var history = new DownloadHistoryStore(
            services.GetRequiredService<CherryBox.Core.Platform.IPlatformPaths>(),
            services.GetRequiredService<CherryBox.Core.Configuration.IConfigManager>(),
            services.GetRequiredService<ILogger<DownloadHistoryStore>>());
        var ytDlpInstaller = new YtDlpToolInstaller(
            services.GetRequiredService<IHttpClientFactory>(),
            services.GetRequiredService<CherryBox.Core.Platform.IPlatformPaths>(),
            services.GetRequiredService<CherryBox.Core.SupportApps.ISupportAppManifestStore>(),
            services.GetRequiredService<ILogger<YtDlpToolInstaller>>());

        registry.RegisterSingleton(jobTracker);
        registry.RegisterSingleton(history);
        registry.RegisterSingleton(ytDlpInstaller);
        registry.RegisterScoped<IDownloadLimitService>(sp => new DownloadLimitService(
            sp.GetRequiredService<CherryBox.Data.CherryBoxDbContext>(),
            sp.GetRequiredService<CherryBox.Core.Configuration.IConfigManager>()));
        registry.RegisterScoped<IDownloadService>(sp => new DownloadService(
            sp.GetRequiredService<CherryBox.Data.CherryBoxDbContext>(),
            history,
            sp.GetRequiredService<CherryBox.Core.Configuration.IConfigManager>(),
            jobTracker,
            sp.GetRequiredService<CherryBox.Core.Platform.IPlatformPaths>(),
            registry.Resolve<IDownloadLimitService>(sp)!));
        registry.RegisterSupportAppUpdater(ytDlpInstaller);
    }

    public async Task StartAsync(IPluginContext context, CancellationToken cancellationToken = default)
    {
        var registry = context.Services.GetRequiredService<IPluginServiceRegistry>();
        var scopeFactory = context.Services.GetRequiredService<IServiceScopeFactory>();
        var paths = context.Services.GetRequiredService<CherryBox.Core.Platform.IPlatformPaths>();
        var config = context.Services.GetRequiredService<CherryBox.Core.Configuration.IConfigManager>();
        var jobTracker = registry.Resolve<DownloadJobTracker>(context.Services)!;
        var logger = context.Services.GetRequiredService<ILogger<DownloadWorker>>();

        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CherryBox.Data.CherryBoxDbContext>();
            await DownloadPaths.EnsureDefaultDownloadFolderAsync(db, paths, cancellationToken);
        }

        var ytDlp = registry.Resolve<YtDlpToolInstaller>(context.Services);

        _workerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var worker = new DownloadWorker(scopeFactory, paths, config, jobTracker, ytDlp, logger);
        _workerTask = worker.RunAsync(_workerCts.Token);

        if (ytDlp is not null)
            _ = EnsureYtDlpInBackgroundAsync(ytDlp, logger, _workerCts.Token);

        registry.RegisterStopCallback(async ct =>
        {
            _workerCts?.Cancel();
            if (_workerTask is not null)
            {
                try { await _workerTask; } catch (OperationCanceledException) { }
            }
            _workerCts?.Dispose();
            _workerCts = null;
            _workerTask = null;
        });
    }

    private static async Task EnsureYtDlpInBackgroundAsync(
        YtDlpToolInstaller ytDlp,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            await ytDlp.EnsureInstalledAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Background yt-dlp installation failed; downloads will retry once yt-dlp is available.");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _workerCts?.Cancel();
        if (_workerTask is not null)
        {
            try { await _workerTask; } catch (OperationCanceledException) { }
        }
        _workerCts?.Dispose();
        _workerCts = null;
        _workerTask = null;
    }
}

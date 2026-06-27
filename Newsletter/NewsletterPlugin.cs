using CherryBox.Plugins.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CherryBox.Newsletter.Plugin;

public sealed class NewsletterPlugin : ICherryBoxPlugin, IPluginServiceContributor
{
    private Task? _workerTask;
    private CancellationTokenSource? _workerCts;

    public string Id => "newsletter";
    public string Name => "Newsletter";
    public string Version => "1.4.0";

    public Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public void RegisterServices(IPluginServiceRegistry registry, IPluginContext context)
    {
        var settingsStore = new NewsletterSettingsStore(context.DataDirectory);
        var subscriptionStore = new NewsletterSubscriptionStore(context.DataDirectory);
        var cacheStore = new NewsletterWeeklyCacheStore(context.DataDirectory);

        registry.RegisterSingleton(settingsStore);
        registry.RegisterSingleton(subscriptionStore);
        registry.RegisterSingleton(cacheStore);
        registry.RegisterScoped<INewsletterService>(sp =>
        {
            var email = sp.GetRequiredService<IPluginServiceRegistry>().Resolve<IEmailService>(sp)
                ?? throw new InvalidOperationException("Email plugin is required for the newsletter plugin.");
            return new NewsletterService(
                sp.GetRequiredService<CherryBox.Data.CherryBoxDbContext>(),
                sp.GetRequiredService<CherryBox.Core.Configuration.IConfigManager>(),
                settingsStore,
                subscriptionStore,
                cacheStore,
                email,
                sp.GetRequiredService<IPluginServiceRegistry>(),
                sp,
                sp.GetRequiredService<ILogger<NewsletterService>>());
        });
    }

    public Task StartAsync(IPluginContext context, CancellationToken cancellationToken = default)
    {
        var scopeFactory = context.Services.GetRequiredService<IServiceScopeFactory>();
        var logger = context.Services.GetRequiredService<ILogger<NewsletterWorker>>();
        var registry = context.Services.GetRequiredService<IPluginServiceRegistry>();

        _workerCts = new CancellationTokenSource();
        var worker = new NewsletterWorker(scopeFactory, logger);
        _workerTask = worker.RunAsync(_workerCts.Token);

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

        return Task.CompletedTask;
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

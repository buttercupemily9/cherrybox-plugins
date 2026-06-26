using CherryBox.Plugins.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CherryBox.Newsletter.Plugin;

internal sealed class NewsletterWorker
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NewsletterWorker> _logger;

    public NewsletterWorker(IServiceScopeFactory scopeFactory, ILogger<NewsletterWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var registry = scope.ServiceProvider.GetRequiredService<IPluginServiceRegistry>();
                var newsletter = registry.Resolve<INewsletterService>(scope.ServiceProvider);
                if (newsletter is null)
                {
                    _logger.LogDebug("Newsletter service is not available; skipping weekly check.");
                }
                else if (newsletter.ShouldSendWeeklyDigestNow(DateTimeOffset.UtcNow))
                {
                    _logger.LogInformation("Weekly newsletter schedule matched; sending digest.");
                    await newsletter.SendWeeklyDigestAsync(cancellationToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Newsletter worker iteration failed.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}

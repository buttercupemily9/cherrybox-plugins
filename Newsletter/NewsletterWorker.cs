using System.Reflection;
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
                else
                {
                    var utcNow = DateTimeOffset.UtcNow;
                    if (ShouldPrepareWeeklyDigest(newsletter, utcNow))
                    {
                        _logger.LogInformation("Weekly newsletter send day; pre-generating male and female digest versions.");
                        await InvokePrepareWeeklyDigestAsync(newsletter, cancellationToken);
                    }

                    if (ShouldSendWeeklyDigest(newsletter, utcNow))
                    {
                        _logger.LogInformation("Weekly newsletter schedule matched; sending digest.");
                        await newsletter.SendWeeklyDigestAsync(cancellationToken);
                    }
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

    private static bool ShouldSendWeeklyDigest(INewsletterService newsletter, DateTimeOffset utcNow) =>
        InvokeBoolMethod(newsletter, "ShouldSendWeeklyDigestNow", utcNow)
        || InvokeBoolMethod(newsletter, "ShouldSendWeeklyNow", utcNow);

    private static bool ShouldPrepareWeeklyDigest(INewsletterService newsletter, DateTimeOffset utcNow) =>
        InvokeBoolMethod(newsletter, "ShouldPrepareWeeklyDigestNow", utcNow);

    private static async Task InvokePrepareWeeklyDigestAsync(INewsletterService newsletter, CancellationToken cancellationToken)
    {
        var type = newsletter.GetType();
        var method = type.GetMethod(
            "PrepareWeeklyDigestAsync",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (method is null)
            return;

        try
        {
            var task = (Task)method.Invoke(newsletter, [cancellationToken])!;
            await task;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to prepare weekly digest cache.", ex);
        }
    }

    private static bool InvokeBoolMethod(INewsletterService newsletter, string methodName, DateTimeOffset? utcNow = null)
    {
        var type = newsletter.GetType();
        var method = type.GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (method is null || method.ReturnType != typeof(bool))
            return false;

        try
        {
            var args = utcNow.HasValue ? new object[] { utcNow.Value } : Array.Empty<object>();
            return method.Invoke(newsletter, args) is true;
        }
        catch
        {
            return false;
        }
    }
}

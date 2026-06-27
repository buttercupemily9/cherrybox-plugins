using CherryBox.Plugins.Abstractions;
using Microsoft.Extensions.Logging;

namespace CherryBox.StoryCovers.Plugin;

internal sealed class StoryCoverWorker
{
    private readonly StoryCoverJobStore _jobs;
    private readonly StoryCoverExecutor _executor;
    private readonly StoryCoverWorkerState _state;
    private readonly StoryCoverSettingsStore _settings;
    private readonly ILogger<StoryCoverWorker> _logger;

    public StoryCoverWorker(
        StoryCoverJobStore jobs,
        StoryCoverExecutor executor,
        StoryCoverWorkerState state,
        StoryCoverSettingsStore settings,
        ILogger<StoryCoverWorker> logger)
    {
        _jobs = jobs;
        _executor = executor;
        _state = state;
        _settings = settings;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (!_state.IsEnabled || !_settings.Get().BackgroundWorkerEnabled)
            {
                await DelayAsync(cancellationToken);
                continue;
            }

            var job = _jobs.ClaimNextPending();
            if (job is null)
            {
                await DelayAsync(cancellationToken);
                continue;
            }

            _state.SetProcessing(job, true);
            try
            {
                var result = await _executor.ExecuteAsync(job, cancellationToken);
                _jobs.Update(result);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Story cover worker failed on job {JobId}", job.Id);
                _jobs.Update(job with
                {
                    Status = StoryCoverJobStatus.Failed,
                    ErrorMessage = ex.Message,
                    CompletedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                });
            }
            finally
            {
                _state.SetProcessing(null, false);
            }
        }
    }

    private static async Task DelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
    }
}

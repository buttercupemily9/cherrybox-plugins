using CherryBox.Plugins.Abstractions;
using Microsoft.Extensions.Logging;

namespace CherryBox.StoryTts.Plugin;

internal sealed class StoryTtsWorker
{
    private readonly StoryTtsJobStore _jobs;
    private readonly StoryTtsExecutor _executor;
    private readonly StoryTtsWorkerState _state;
    private readonly StoryTtsSettingsStore _settings;
    private readonly ILogger<StoryTtsWorker> _logger;

    public StoryTtsWorker(
        StoryTtsJobStore jobs,
        StoryTtsExecutor executor,
        StoryTtsWorkerState state,
        StoryTtsSettingsStore settings,
        ILogger<StoryTtsWorker> logger)
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
            try
            {
                if (!_state.IsEnabled || !_settings.Get().BackgroundWorkerEnabled)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                    continue;
                }

                var job = _jobs.ClaimNextPending();
                if (job is null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                    continue;
                }

                _state.SetProcessing(job, true);
                try
                {
                    var result = await _executor.ExecuteAsync(job, cancellationToken);
                    _jobs.Update(result);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Story TTS job {JobId} crashed", job.Id);
                    _jobs.Update(job with
                    {
                        Status = StoryTtsJobStatus.Failed,
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
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Story TTS worker loop error");
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
        }
    }
}

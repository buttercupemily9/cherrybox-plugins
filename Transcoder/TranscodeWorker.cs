using CherryBox.Plugins.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CherryBox.Transcoder.Plugin;

internal sealed class TranscodeWorker
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TranscodeJobStore _jobs;
    private readonly TranscodeExecutor _executor;
    private readonly TranscodeWorkerState _state;
    private readonly TranscodeAssignmentsStore _assignments;
    private readonly ILogger<TranscodeWorker> _logger;

    public TranscodeWorker(
        IServiceScopeFactory scopeFactory,
        TranscodeJobStore jobs,
        TranscodeExecutor executor,
        TranscodeWorkerState state,
        TranscodeAssignmentsStore assignments,
        ILogger<TranscodeWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _jobs = jobs;
        _executor = executor;
        _state = state;
        _assignments = assignments;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Transcode worker started.");
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (!_state.IsEnabled || !_assignments.Get().BackgroundWorkerEnabled)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                    continue;
                }

                var claimed = _jobs.ClaimNextPending();
                if (claimed is null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                    continue;
                }

                _state.SetProcessing(claimed);
                var result = await _executor.ExecuteAsync(claimed, cancellationToken);
                _jobs.Update(result);
                _state.SetProcessing(null);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Transcode worker loop error");
                _state.SetProcessing(null);
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
        }

        _logger.LogInformation("Transcode worker stopped.");
    }
}

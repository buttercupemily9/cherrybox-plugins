using CherryBox.Core.Enums;
using CherryBox.Data;
using CherryBox.Media;
using CherryBox.Plugins.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CherryBox.StoryCovers.Plugin;

internal sealed class StoryCoverService : IStoryCoverService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly StoryCoverSettingsStore _settings;
    private readonly StoryCoverJobStore _jobs;
    private readonly StoryCoverWorkerState _workerState;
    private readonly ILogger<StoryCoverService> _logger;

    public StoryCoverService(
        IServiceScopeFactory scopeFactory,
        StoryCoverSettingsStore settings,
        StoryCoverJobStore jobs,
        StoryCoverWorkerState workerState,
        ILogger<StoryCoverService> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings;
        _jobs = jobs;
        _workerState = workerState;
        _logger = logger;
    }

    public Task<StoryCoverSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(ToDto(_settings.Get()));

    public Task<StoryCoverSettingsDto> UpdateSettingsAsync(
        UpdateStoryCoverSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        var updated = _settings.Update(settings =>
        {
            settings.BackgroundWorkerEnabled = request.BackgroundWorkerEnabled;
            settings.AutoGenerateOnIndex = request.AutoGenerateOnIndex;
            settings.SkipWhenCoverExists = request.SkipWhenCoverExists;
            settings.UseChatPromptRefinement = request.UseChatPromptRefinement;
            settings.ImageWidth = Math.Clamp(request.ImageWidth, 256, 2048);
            settings.ImageHeight = Math.Clamp(request.ImageHeight, 256, 2048);
            settings.ContextCharLimit = Math.Clamp(request.ContextCharLimit, 500, 8000);
        });

        _workerState.SetEnabled(updated.BackgroundWorkerEnabled);
        return Task.FromResult(ToDto(updated));
    }

    public Task<IReadOnlyList<StoryCoverJobDto>> ListJobsAsync(int limit, CancellationToken cancellationToken = default) =>
        Task.FromResult(_jobs.List(limit));

    public async Task<EnqueueStoryCoverResult> EnqueueAsync(
        EnqueueStoryCoverRequest request,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CherryBoxDbContext>();
        var story = await db.MediaItems.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == request.StoryMediaItemId && m.MediaType == MediaType.Story, cancellationToken);
        if (story is null)
            throw new InvalidOperationException("Story was not found.");

        if (_jobs.HasActiveJobForStory(story.Id))
        {
            return new EnqueueStoryCoverResult(
                Guid.Empty,
                true,
                "A cover job is already queued for this story.");
        }

        var pluginSettings = _settings.Get();
        if (pluginSettings.SkipWhenCoverExists)
        {
            var blobs = scope.ServiceProvider.GetRequiredService<IMediaBlobService>();
            if (await blobs.ExistsAsync(story.Id, MediaBlobKind.PrimaryImage, cancellationToken))
            {
                return new EnqueueStoryCoverResult(
                    Guid.Empty,
                    false,
                    "This story already has cover art.");
            }
        }

        var job = _jobs.Add(story.Id, story.Title ?? story.FileName);
        _logger.LogInformation("Queued story cover job {JobId} for story {StoryId}", job.Id, story.Id);
        return new EnqueueStoryCoverResult(job.Id, false, "Story cover generation queued.");
    }

    public async Task<EnqueueAllStoryCoversResult> EnqueueAllMissingAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CherryBoxDbContext>();
        var blobs = scope.ServiceProvider.GetRequiredService<IMediaBlobService>();
        var pluginSettings = _settings.Get();

        var stories = await db.MediaItems.AsNoTracking()
            .Where(m => m.MediaType == MediaType.Story)
            .OrderBy(m => m.FileName)
            .ToListAsync(cancellationToken);

        var enqueued = 0;
        var skippedExisting = 0;
        var skippedQueued = 0;

        foreach (var story in stories)
        {
            if (pluginSettings.SkipWhenCoverExists
                && await blobs.ExistsAsync(story.Id, MediaBlobKind.PrimaryImage, cancellationToken))
            {
                skippedExisting++;
                continue;
            }

            if (_jobs.HasActiveJobForStory(story.Id))
            {
                skippedQueued++;
                continue;
            }

            try
            {
                _jobs.Add(story.Id, story.Title ?? story.FileName);
                enqueued++;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Skipped story cover enqueue for {StoryId}", story.Id);
                skippedQueued++;
            }
        }

        var message = enqueued == 0
            ? "No stories were queued for cover generation."
            : $"Queued {enqueued} story/stories for cover generation.";

        return new EnqueueAllStoryCoversResult(enqueued, skippedExisting, skippedQueued, message);
    }

    public Task<bool> CancelJobAsync(Guid jobId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_jobs.Cancel(jobId));

    public Task<StoryCoverWorkerStatusDto> GetWorkerStatusAsync(CancellationToken cancellationToken = default)
    {
        var settings = _settings.Get();
        return Task.FromResult(new StoryCoverWorkerStatusDto(
            settings.BackgroundWorkerEnabled,
            _workerState.IsProcessing,
            _workerState.CurrentJob,
            _jobs.CountPending(),
            _jobs.CountFailed()));
    }

    public Task SetBackgroundWorkerEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        _settings.Update(settings => settings.BackgroundWorkerEnabled = enabled);
        _workerState.SetEnabled(enabled);
        return Task.CompletedTask;
    }

    private static StoryCoverSettingsDto ToDto(StoryCoverSettings settings) => new(
        settings.BackgroundWorkerEnabled,
        settings.AutoGenerateOnIndex,
        settings.SkipWhenCoverExists,
        settings.UseChatPromptRefinement,
        settings.ImageWidth,
        settings.ImageHeight,
        settings.ContextCharLimit);
}

using CherryBox.Core.Enums;
using CherryBox.Data;
using CherryBox.Plugins.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CherryBox.StoryTts.Plugin;

internal sealed class StoryTtsService : IStoryTtsService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly StoryTtsSettingsStore _settings;
    private readonly StoryTtsJobStore _jobs;
    private readonly StoryTtsWorkerState _workerState;
    private readonly VeniceTtsClient _venice;
    private readonly ILogger<StoryTtsService> _logger;

    public StoryTtsService(
        IServiceScopeFactory scopeFactory,
        StoryTtsSettingsStore settings,
        StoryTtsJobStore jobs,
        StoryTtsWorkerState workerState,
        VeniceTtsClient venice,
        ILogger<StoryTtsService> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings;
        _jobs = jobs;
        _workerState = workerState;
        _venice = venice;
        _logger = logger;
    }

    public Task<StoryTtsSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        var settings = _settings.Get();
        return Task.FromResult(ToDto(settings));
    }

    public Task<StoryTtsSettingsDto> UpdateSettingsAsync(UpdateStoryTtsSettingsRequest request, CancellationToken cancellationToken = default)
    {
        var updated = _settings.Update(settings =>
        {
            if (request.ClearApiKey)
                settings.ApiKey = null;
            else if (!string.IsNullOrWhiteSpace(request.ApiKey))
                settings.ApiKey = request.ApiKey.Trim();

            settings.Model = string.IsNullOrWhiteSpace(request.Model) ? "tts-kokoro" : request.Model.Trim();
            settings.Voice = string.IsNullOrWhiteSpace(request.Voice) ? "af_sky" : request.Voice.Trim();
            settings.ResponseFormat = string.IsNullOrWhiteSpace(request.ResponseFormat) ? "mp3" : request.ResponseFormat.Trim();
            settings.Speed = Math.Clamp(request.Speed, 0.25, 4.0);
            settings.MaxCharsPerRequest = Math.Clamp(request.MaxCharsPerRequest, 500, 12000);
            settings.AudioLibraryFolderId = request.AudioLibraryFolderId;
            settings.BackgroundWorkerEnabled = request.BackgroundWorkerEnabled;
            settings.AutoLinkOnComplete = request.AutoLinkOnComplete;
        });

        _workerState.SetEnabled(updated.BackgroundWorkerEnabled);
        return Task.FromResult(ToDto(updated));
    }

    public Task<IReadOnlyList<StoryTtsJobDto>> ListJobsAsync(int limit, CancellationToken cancellationToken = default) =>
        Task.FromResult(_jobs.List(limit));

    public async Task<EnqueueStoryTtsResult> EnqueueAsync(EnqueueStoryTtsRequest request, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CherryBoxDbContext>();
        var stories = await QueryStoriesAsync(db, request, cancellationToken);

        var enqueued = 0;
        var skipped = 0;
        foreach (var story in stories)
        {
            try
            {
                if (_jobs.HasActiveJobForStory(story.Id))
                {
                    skipped++;
                    continue;
                }

                _jobs.Add(story.Id, story.Title ?? story.FileName);
                enqueued++;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Skipped story TTS enqueue for {StoryId}", story.Id);
                skipped++;
            }
        }

        return new EnqueueStoryTtsResult(
            enqueued,
            skipped,
            enqueued == 0 ? "No stories were queued." : $"Queued {enqueued} story/stories for text-to-speech.");
    }

    public Task<bool> CancelJobAsync(Guid jobId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_jobs.Cancel(jobId));

    public Task<StoryTtsWorkerStatusDto> GetWorkerStatusAsync(CancellationToken cancellationToken = default)
    {
        var settings = _settings.Get();
        return Task.FromResult(new StoryTtsWorkerStatusDto(
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

    public async Task<StoryTtsTestResult> TestConnectionAsync(StoryTtsTestRequest request, CancellationToken cancellationToken = default)
    {
        var settings = _settings.Get();
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            return new StoryTtsTestResult(false, "Venice API key is not configured.");

        try
        {
            var sample = string.IsNullOrWhiteSpace(request.SampleText)
                ? "CherryBox story text to speech test."
                : request.SampleText.Trim();
            await _venice.SynthesizeAsync(settings.ApiKey, sample, settings, cancellationToken);
            return new StoryTtsTestResult(true, "Venice text-to-speech connection succeeded.");
        }
        catch (Exception ex)
        {
            return new StoryTtsTestResult(false, ex.Message);
        }
    }

    private static async Task<List<Core.Entities.MediaItem>> QueryStoriesAsync(
        CherryBoxDbContext db,
        EnqueueStoryTtsRequest request,
        CancellationToken cancellationToken)
    {
        if (request.StoryMediaItemId is { } storyId)
        {
            var one = await db.MediaItems.AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == storyId && m.MediaType == MediaType.Story, cancellationToken);
            return one is null ? [] : [one];
        }

        if (!request.AllUnlinkedStories)
            throw new InvalidOperationException("Specify a story or set allUnlinkedStories to true.");

        var linkedStoryIds = await db.StoryLinks.AsNoTracking()
            .Select(l => l.StoryMediaItemId)
            .ToListAsync(cancellationToken);

        return await db.MediaItems.AsNoTracking()
            .Where(m => m.MediaType == MediaType.Story && !linkedStoryIds.Contains(m.Id))
            .OrderBy(m => m.FileName)
            .ToListAsync(cancellationToken);
    }

    private static StoryTtsSettingsDto ToDto(StoryTtsSettings settings) => new(
        !string.IsNullOrWhiteSpace(settings.ApiKey),
        settings.Model,
        settings.Voice,
        settings.ResponseFormat,
        settings.Speed,
        settings.MaxCharsPerRequest,
        settings.AudioLibraryFolderId,
        settings.BackgroundWorkerEnabled,
        settings.AutoLinkOnComplete);
}

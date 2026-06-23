using CherryBox.Core.Enums;
using CherryBox.Data;
using CherryBox.Plugins.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CherryBox.Transcoder.Plugin;

internal sealed class TranscodeService : ITranscodeService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TranscodeProfileStore _profiles;
    private readonly TranscodeAssignmentsStore _assignments;
    private readonly TranscodeJobStore _jobs;
    private readonly TranscodeAssignmentResolver _resolver;
    private readonly TranscodeWorkerState _workerState;
    private readonly ILogger<TranscodeService> _logger;

    public TranscodeService(
        IServiceScopeFactory scopeFactory,
        TranscodeProfileStore profiles,
        TranscodeAssignmentsStore assignments,
        TranscodeJobStore jobs,
        TranscodeWorkerState workerState,
        ILogger<TranscodeService> logger)
    {
        _scopeFactory = scopeFactory;
        _profiles = profiles;
        _assignments = assignments;
        _jobs = jobs;
        _resolver = new TranscodeAssignmentResolver();
        _workerState = workerState;
        _logger = logger;
    }

    public Task<IReadOnlyList<TranscodeProfileDto>> ListProfilesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_profiles.List());

    public Task<TranscodeProfileDto?> GetProfileAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_profiles.Get(id));

    public Task<TranscodeProfileDto> CreateProfileAsync(UpsertTranscodeProfileRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(_profiles.Create(request));

    public Task<TranscodeProfileDto?> UpdateProfileAsync(Guid id, UpsertTranscodeProfileRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(_profiles.Update(id, request));

    public Task<bool> DeleteProfileAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_profiles.Delete(id));

    public Task<TranscodeProfileDto> ImportProfileAsync(string json, CancellationToken cancellationToken = default) =>
        Task.FromResult(_profiles.Import(json));

    public Task<string> ExportProfileAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_profiles.Export(id));

    public Task<TranscodeAssignmentsDto> GetAssignmentsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_assignments.Get());

    public Task<TranscodeAssignmentsDto> UpdateAssignmentsAsync(UpdateTranscodeAssignmentsRequest request, CancellationToken cancellationToken = default)
    {
        var updated = _assignments.Update(request);
        if (request.BackgroundWorkerEnabled != updated.BackgroundWorkerEnabled)
            _workerState.SetEnabled(updated.BackgroundWorkerEnabled);
        return Task.FromResult(updated);
    }

    public Task<IReadOnlyList<TranscodeJobDto>> ListJobsAsync(int limit, CancellationToken cancellationToken = default) =>
        Task.FromResult(_jobs.List(limit));

    public async Task<EnqueueTranscodeResult> EnqueueAsync(EnqueueTranscodeRequest request, CancellationToken cancellationToken = default)
    {
        if (request.RetryFailed)
            _jobs.ResetFailedToPending();

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CherryBoxDbContext>();
        var assignments = _assignments.Get();
        var videos = await QueryVideosAsync(db, request, cancellationToken);

        var enqueued = 0;
        var skipped = 0;
        foreach (var video in videos)
        {
            if (video.LibraryFolderId is null) { skipped++; continue; }
            var profileId = _resolver.ResolveProfileId(video.LibraryFolderId.Value, assignments, _profiles);
            if (profileId is null) { skipped++; continue; }

            try
            {
                if (_jobs.HasActiveJobForMedia(video.Id)) { skipped++; continue; }
                _jobs.Add(video.Id, profileId.Value, video.Title ?? video.FileName, video.FilePath);
                enqueued++;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Skipped enqueue for {MediaId}", video.Id);
                skipped++;
            }
        }

        return new EnqueueTranscodeResult(
            enqueued,
            skipped,
            enqueued == 0 ? "No videos were queued." : $"Queued {enqueued} video(s).");
    }

    public Task<bool> CancelJobAsync(Guid jobId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_jobs.Cancel(jobId));

    public Task<TranscodeWorkerStatusDto> GetWorkerStatusAsync(CancellationToken cancellationToken = default)
    {
        var assignments = _assignments.Get();
        return Task.FromResult(new TranscodeWorkerStatusDto(
            assignments.BackgroundWorkerEnabled,
            _workerState.IsProcessing,
            _workerState.CurrentJob,
            _jobs.CountPending(),
            _jobs.CountFailed()));
    }

    public Task SetBackgroundWorkerEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        _assignments.SetBackgroundWorkerEnabled(enabled);
        _workerState.SetEnabled(enabled);
        return Task.CompletedTask;
    }

    public Task<int> EnqueueBatchForAdminTaskAsync(CancellationToken cancellationToken = default) =>
        EnqueueAsync(new EnqueueTranscodeRequest(null, null, true, false), cancellationToken)
            .ContinueWith(t => t.Result.Enqueued, cancellationToken);

    public async Task EnqueueForMediaAsync(Guid mediaItemId, CancellationToken cancellationToken = default)
    {
        var assignments = _assignments.Get();
        if (!assignments.GlobalEnabled || !assignments.AutoEnqueueOnScan) return;
        await EnqueueMediaCoreAsync(mediaItemId, cancellationToken);
    }

    public async Task EnqueueForDownloadAsync(Guid mediaItemId, CancellationToken cancellationToken = default)
    {
        var assignments = _assignments.Get();
        if (!assignments.GlobalEnabled) return;
        await EnqueueMediaCoreAsync(mediaItemId, cancellationToken);
    }

    private async Task EnqueueMediaCoreAsync(Guid mediaItemId, CancellationToken cancellationToken)
    {
        var assignments = _assignments.Get();
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CherryBoxDbContext>();
        var media = await db.MediaItems.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == mediaItemId && m.MediaType == MediaType.Video, cancellationToken);
        if (media?.LibraryFolderId is null || !File.Exists(media.FilePath)) return;

        var profileId = _resolver.ResolveProfileId(media.LibraryFolderId.Value, assignments, _profiles);
        if (profileId is null) return;
        if (_jobs.HasActiveJobForMedia(media.Id)) return;

        _jobs.Add(media.Id, profileId.Value, media.Title ?? media.FileName, media.FilePath);
    }

    private static async Task<List<CherryBox.Core.Entities.MediaItem>> QueryVideosAsync(
        CherryBoxDbContext db,
        EnqueueTranscodeRequest request,
        CancellationToken cancellationToken)
    {
        if (request.MediaItemId is { } mediaId)
        {
            var one = await db.MediaItems.AsNoTracking()
                .Where(m => m.Id == mediaId && m.MediaType == MediaType.Video)
                .ToListAsync(cancellationToken);
            return one;
        }

        var query = db.MediaItems.AsNoTracking().Where(m => m.MediaType == MediaType.Video);
        if (request.LibraryFolderId is { } folderId)
            query = query.Where(m => m.LibraryFolderId == folderId);

        return await query.OrderBy(m => m.FileName).ToListAsync(cancellationToken);
    }
}

internal sealed class TranscodeWorkerState
{
    private volatile bool _enabled = true;
    public bool IsProcessing { get; private set; }
    public TranscodeJobDto? CurrentJob { get; private set; }

    public bool IsEnabled => _enabled;

    public void SetEnabled(bool enabled) => _enabled = enabled;

    public void SetProcessing(TranscodeJobDto? job)
    {
        IsProcessing = job is not null;
        CurrentJob = job;
    }
}

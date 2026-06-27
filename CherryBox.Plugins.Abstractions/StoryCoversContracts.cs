namespace CherryBox.Plugins.Abstractions;

public enum StoryCoverJobStatus
{
    Pending = 0,
    Processing = 1,
    Completed = 2,
    Failed = 3,
    Cancelled = 4
}

public sealed record StoryCoverSettingsDto(
    bool BackgroundWorkerEnabled,
    bool AutoGenerateOnIndex,
    bool SkipWhenCoverExists,
    bool UseChatPromptRefinement,
    int ImageWidth,
    int ImageHeight,
    int ContextCharLimit);

public sealed record UpdateStoryCoverSettingsRequest(
    bool BackgroundWorkerEnabled,
    bool AutoGenerateOnIndex,
    bool SkipWhenCoverExists,
    bool UseChatPromptRefinement,
    int ImageWidth,
    int ImageHeight,
    int ContextCharLimit);

public sealed record StoryCoverJobDto(
    Guid Id,
    Guid StoryMediaItemId,
    StoryCoverJobStatus Status,
    string? StoryTitle,
    string? ErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt);

public sealed record EnqueueStoryCoverRequest(Guid StoryMediaItemId);

public sealed record EnqueueStoryCoverResult(
    Guid JobId,
    bool AlreadyQueued,
    string Message);

public sealed record EnqueueAllStoryCoversResult(
    int Enqueued,
    int SkippedExisting,
    int SkippedQueued,
    string Message);

public sealed record StoryCoverWorkerStatusDto(
    bool BackgroundWorkerEnabled,
    bool Processing,
    StoryCoverJobDto? CurrentJob,
    int PendingCount,
    int FailedCount);

public interface IStoryCoverService
{
    Task<StoryCoverSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default);
    Task<StoryCoverSettingsDto> UpdateSettingsAsync(UpdateStoryCoverSettingsRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StoryCoverJobDto>> ListJobsAsync(int limit, CancellationToken cancellationToken = default);
    Task<EnqueueStoryCoverResult> EnqueueAsync(EnqueueStoryCoverRequest request, CancellationToken cancellationToken = default);
    Task<EnqueueAllStoryCoversResult> EnqueueAllMissingAsync(CancellationToken cancellationToken = default);
    Task<bool> CancelJobAsync(Guid jobId, CancellationToken cancellationToken = default);
    Task<StoryCoverWorkerStatusDto> GetWorkerStatusAsync(CancellationToken cancellationToken = default);
    Task SetBackgroundWorkerEnabledAsync(bool enabled, CancellationToken cancellationToken = default);
}

using CherryBox.Core.Enums;

namespace CherryBox.Plugins.Abstractions;

public sealed record TranscodeVideoSettingsDto(
    TranscodeVideoCodec Codec,
    TranscodeRateControl RateControl,
    int? Crf,
    int? BitrateKbps,
    int? MaxWidth,
    int? MaxHeight);

public sealed record TranscodeAudioSettingsDto(
    TranscodeAudioCodec Codec,
    int Channels,
    int BitrateKbps,
    int SampleRateHz);

public sealed record TranscodeProfileDto(
    Guid Id,
    string Name,
    int Version,
    TranscodeContainer Container,
    TranscodeVideoSettingsDto Video,
    TranscodeAudioSettingsDto Audio,
    double? FileSizeTargetMB,
    bool SkipIfCompatible,
    DateTimeOffset UpdatedAt);

public sealed record UpsertTranscodeProfileRequest(
    string Name,
    TranscodeContainer Container,
    TranscodeVideoSettingsDto Video,
    TranscodeAudioSettingsDto Audio,
    double? FileSizeTargetMB,
    bool SkipIfCompatible);

public sealed record TranscodeLibraryOverrideDto(
    Guid LibraryFolderId,
    Guid ProfileId,
    bool Enabled);

public sealed record TranscodeProfileLibraryBindingDto(
    Guid ProfileId,
    IReadOnlyList<Guid> LibraryFolderIds);

public sealed record TranscodeAssignmentsDto(
    Guid? GlobalDefaultProfileId,
    bool GlobalEnabled,
    bool BackgroundWorkerEnabled,
    bool AutoEnqueueOnScan,
    IReadOnlyList<TranscodeLibraryOverrideDto> LibraryOverrides,
    IReadOnlyList<TranscodeProfileLibraryBindingDto> ProfileLibraryBindings);

public sealed record UpdateTranscodeAssignmentsRequest(
    Guid? GlobalDefaultProfileId,
    bool GlobalEnabled,
    bool BackgroundWorkerEnabled,
    bool AutoEnqueueOnScan,
    IReadOnlyList<TranscodeLibraryOverrideDto>? LibraryOverrides,
    IReadOnlyList<TranscodeProfileLibraryBindingDto>? ProfileLibraryBindings);

public sealed record TranscodeJobDto(
    Guid Id,
    Guid MediaItemId,
    Guid ProfileId,
    TranscodeJobStatus Status,
    string? MediaTitle,
    string? SourcePath,
    string? OutputPath,
    string? ErrorMessage,
    long? BytesBefore,
    long? BytesAfter,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt);

public sealed record EnqueueTranscodeRequest(
    Guid? MediaItemId,
    Guid? LibraryFolderId,
    bool AllVideos,
    bool RetryFailed);

public sealed record EnqueueTranscodeResult(int Enqueued, int Skipped, string Message);

public sealed record TranscodeWorkerStatusDto(
    bool BackgroundWorkerEnabled,
    bool IsProcessing,
    TranscodeJobDto? CurrentJob,
    int PendingCount,
    int FailedCount);

public interface ITranscodeService
{
    Task<IReadOnlyList<TranscodeProfileDto>> ListProfilesAsync(CancellationToken cancellationToken = default);
    Task<TranscodeProfileDto?> GetProfileAsync(Guid id, CancellationToken cancellationToken = default);
    Task<TranscodeProfileDto> CreateProfileAsync(UpsertTranscodeProfileRequest request, CancellationToken cancellationToken = default);
    Task<TranscodeProfileDto?> UpdateProfileAsync(Guid id, UpsertTranscodeProfileRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteProfileAsync(Guid id, CancellationToken cancellationToken = default);
    Task<TranscodeProfileDto> ImportProfileAsync(string json, CancellationToken cancellationToken = default);
    Task<string> ExportProfileAsync(Guid id, CancellationToken cancellationToken = default);
    Task<TranscodeAssignmentsDto> GetAssignmentsAsync(CancellationToken cancellationToken = default);
    Task<TranscodeAssignmentsDto> UpdateAssignmentsAsync(UpdateTranscodeAssignmentsRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TranscodeJobDto>> ListJobsAsync(int limit, CancellationToken cancellationToken = default);
    Task<EnqueueTranscodeResult> EnqueueAsync(EnqueueTranscodeRequest request, CancellationToken cancellationToken = default);
    Task<bool> CancelJobAsync(Guid jobId, CancellationToken cancellationToken = default);
    Task<TranscodeWorkerStatusDto> GetWorkerStatusAsync(CancellationToken cancellationToken = default);
    Task SetBackgroundWorkerEnabledAsync(bool enabled, CancellationToken cancellationToken = default);
    Task<int> EnqueueBatchForAdminTaskAsync(CancellationToken cancellationToken = default);
    Task EnqueueForDownloadAsync(Guid mediaItemId, CancellationToken cancellationToken = default);
}

public interface IVideoMediaRefreshService
{
    Task RefreshAfterFileChangeAsync(Guid mediaItemId, CancellationToken cancellationToken = default);
}

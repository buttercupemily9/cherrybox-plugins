using CherryBox.Core.Enums;

namespace CherryBox.Plugins.Abstractions;

public sealed record DownloadRequest(string Url, Guid? TargetFolderId);

public sealed record DownloadJobDto(
    Guid Id,
    string Url,
    DownloadJobStatus Status,
    string? OutputPath,
    string? ErrorMessage,
    string? BlockReason,
    Guid? ExistingMediaItemId,
    string? ExistingMediaTitle,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? RetryAfterAt,
    int RetryCount);

public sealed record EnqueueDownloadResult(
    bool Accepted,
    DownloadJobDto? Job,
    string? BlockReason,
    Guid? ExistingMediaItemId,
    string? ExistingMediaTitle);

public sealed record DownloadSettingsDto(
    bool AllowNonAdminUsers,
    bool AutoRetryFailedDownloads,
    int AutoRetryDelayMinutes,
    string HistoryDatabaseFileName);

public sealed record UpdateDownloadSettingsRequest(
    bool AllowNonAdminUsers,
    bool AutoRetryFailedDownloads,
    int AutoRetryDelayMinutes);

public sealed record DownloadSiteAuthDto(
    string SiteKey,
    string AuthMode,
    string? Username,
    bool HasPassword,
    bool HasCookiesFile,
    string? TestUrl);

public sealed record UpsertDownloadSiteAuthRequest(
    string SiteKey,
    string AuthMode,
    string? Username,
    string? Password,
    string? TestUrl);

public sealed record TestDownloadSiteAuthRequest(
    string SiteKey,
    string? TestUrl,
    string? AuthMode,
    string? Username,
    string? Password);

public sealed record TestDownloadSiteAuthResult(
    bool Success,
    string Message,
    string? Extractor);

public sealed record DownloadHistoryEntry(
    string NormalizedUrl,
    string OriginalUrl,
    string? Title,
    string? FilePath,
    Guid? MediaItemId,
    DateTimeOffset DownloadedAt);

public interface IDownloadService
{
    Task<EnqueueDownloadResult> EnqueueAsync(DownloadRequest request, Guid? userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DownloadJobDto>> ListAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DownloadJobDto>> ListActiveAsync(CancellationToken cancellationToken = default);
    Task<DownloadJobDto?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<DownloadJobDto?> RetryAsync(Guid id, CancellationToken cancellationToken = default);
    Task<DownloadJobDto?> CancelAsync(Guid id, CancellationToken cancellationToken = default);
    Task<DownloadSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default);
    Task<DownloadSettingsDto> UpdateSettingsAsync(UpdateDownloadSettingsRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DownloadHistoryEntry>> ListHistoryAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DownloadSiteAuthDto>> ListSiteAuthAsync(CancellationToken cancellationToken = default);
    Task<DownloadSiteAuthDto> UpsertSiteAuthAsync(UpsertDownloadSiteAuthRequest request, CancellationToken cancellationToken = default);
    Task RemoveSiteAuthAsync(string siteKey, CancellationToken cancellationToken = default);
    Task<TestDownloadSiteAuthResult> TestSiteAuthAsync(TestDownloadSiteAuthRequest request, CancellationToken cancellationToken = default);
    Task<DownloadSiteAuthDto> UploadSiteCookiesAsync(string siteKey, Stream cookiesFile, CancellationToken cancellationToken = default);
}

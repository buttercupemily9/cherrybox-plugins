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

public sealed record AdminDownloadJobDto(
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
    int RetryCount,
    Guid? CreatedByUserId,
    string? CreatedByUsername);

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
    string HistoryDatabaseFileName,
    int DefaultDownloadLimitMax,
    DownloadLimitPeriod DefaultDownloadLimitPeriod);

public sealed record UpdateDownloadSettingsRequest(
    bool AllowNonAdminUsers,
    bool AutoRetryFailedDownloads,
    int AutoRetryDelayMinutes,
    int? DefaultDownloadLimitMax = null,
    DownloadLimitPeriod? DefaultDownloadLimitPeriod = null);

public sealed record DownloadLimitUsageDto(
    bool IsLimited,
    int LimitMax,
    DownloadLimitPeriod Period,
    int UsedCount,
    int InFlightCount,
    int BonusCount,
    int RemainingCount,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodResetsAt,
    bool HasPendingRequest);

public sealed record DownloadLimitPolicyDto(
    int DefaultDownloadLimitMax,
    DownloadLimitPeriod DefaultDownloadLimitPeriod);

public sealed record UpdateDownloadLimitPolicyRequest(
    int DefaultDownloadLimitMax,
    DownloadLimitPeriod DefaultDownloadLimitPeriod);

public sealed record DownloadLimitUserDto(
    Guid Id,
    string Username,
    UserRole Role,
    int? DownloadLimitMax,
    DownloadLimitPeriod? DownloadLimitPeriod,
    int EffectiveLimitMax,
    DownloadLimitPeriod EffectivePeriod,
    int UsedCount,
    int InFlightCount,
    int BonusCount,
    int RemainingCount);

public sealed record UpdateDownloadLimitUserRequest(
    int? DownloadLimitMax,
    DownloadLimitPeriod? DownloadLimitPeriod,
    bool UseDefaultLimit = false);

public sealed record DownloadLimitRequestDto(
    Guid Id,
    Guid UserId,
    string Username,
    int? RequestedCount,
    string? Message,
    ViewTimeRequestStatus Status,
    int? GrantedCount,
    string? AdminNote,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt);

public sealed record CreateDownloadLimitRequestRequest(
    int? RequestedCount,
    string? Message);

public sealed record ResolveDownloadLimitRequestRequest(
    bool Approve,
    int? GrantedCount,
    string? AdminNote);

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
    Task<IReadOnlyList<DownloadJobDto>> ListAsync(Guid? forUserId = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DownloadJobDto>> ListActiveAsync(Guid? forUserId = null, CancellationToken cancellationToken = default);
    Task<int> CountActiveAsync(Guid? forUserId = null, CancellationToken cancellationToken = default);
    Task<DownloadJobDto?> GetAsync(Guid id, Guid? forUserId = null, CancellationToken cancellationToken = default);
    Task<DownloadJobDto?> RetryAsync(Guid id, Guid? forUserId = null, CancellationToken cancellationToken = default);
    Task<DownloadJobDto?> CancelAsync(Guid id, Guid? forUserId = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AdminDownloadJobDto>> ListAllAsync(CancellationToken cancellationToken = default);
    Task<DownloadSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default);
    Task<DownloadSettingsDto> UpdateSettingsAsync(UpdateDownloadSettingsRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DownloadHistoryEntry>> ListHistoryAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DownloadSiteAuthDto>> ListSiteAuthAsync(CancellationToken cancellationToken = default);
    Task<DownloadSiteAuthDto> UpsertSiteAuthAsync(UpsertDownloadSiteAuthRequest request, CancellationToken cancellationToken = default);
    Task RemoveSiteAuthAsync(string siteKey, CancellationToken cancellationToken = default);
    Task<TestDownloadSiteAuthResult> TestSiteAuthAsync(TestDownloadSiteAuthRequest request, CancellationToken cancellationToken = default);
    Task<DownloadSiteAuthDto> UploadSiteCookiesAsync(string siteKey, Stream cookiesFile, CancellationToken cancellationToken = default);
}

public interface IDownloadLimitService
{
    Task<DownloadLimitUsageDto?> GetUsageAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<(bool Allowed, string? BlockReason)> CanEnqueueAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<DownloadLimitRequestDto?> CreateRequestAsync(Guid userId, CreateDownloadLimitRequestRequest request, CancellationToken cancellationToken = default);
    Task<DownloadLimitPolicyDto> GetPolicyAsync(CancellationToken cancellationToken = default);
    Task<DownloadLimitPolicyDto> UpdatePolicyAsync(UpdateDownloadLimitPolicyRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DownloadLimitUserDto>> ListUsersAsync(CancellationToken cancellationToken = default);
    Task<DownloadLimitUserDto?> UpdateUserAsync(Guid userId, UpdateDownloadLimitUserRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DownloadLimitRequestDto>> ListRequestsAsync(CancellationToken cancellationToken = default);
    Task<DownloadLimitRequestDto?> ResolveRequestAsync(Guid id, Guid adminUserId, ResolveDownloadLimitRequestRequest request, CancellationToken cancellationToken = default);
}

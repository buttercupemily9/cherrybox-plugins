using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CherryBox.Core.Configuration;
using CherryBox.Core.Entities;
using CherryBox.Core.Enums;
using CherryBox.Core.Platform;
using CherryBox.Data;
using CherryBox.Encoding;
using CherryBox.Media;
using CherryBox.Metadata.StashBox;
using CherryBox.Plugins.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CherryBox.Download.Plugin;

public sealed class DownloadService : IDownloadService
{
    private readonly CherryBoxDbContext _db;
    private readonly IDownloadHistoryStore _history;
    private readonly IConfigManager _config;
    private readonly IDownloadJobTracker _jobTracker;
    private readonly IPlatformPaths _paths;
    private readonly IDownloadLimitService _limits;
    private readonly DownloadJobEnrichmentService _enrichment;

    public DownloadService(
        CherryBoxDbContext db,
        IDownloadHistoryStore history,
        IConfigManager config,
        IDownloadJobTracker jobTracker,
        IPlatformPaths paths,
        IDownloadLimitService limits,
        DownloadJobEnrichmentService enrichment)
    {
        _db = db;
        _history = history;
        _config = config;
        _jobTracker = jobTracker;
        _paths = paths;
        _limits = limits;
        _enrichment = enrichment;
    }

    public async Task<EnqueueDownloadResult> EnqueueAsync(
        DownloadRequest request,
        Guid? userId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Url))
            throw new InvalidOperationException("URL is required.");

        var url = request.Url.Trim();
        var normalized = DownloadUrlNormalizer.Normalize(url);

        var historyHit = await _history.FindByUrlAsync(normalized, cancellationToken);
        if (historyHit is not null && !historyHit.Failed)
        {
            var media = await ResolveMediaAsync(historyHit.MediaItemId, historyHit.FilePath, cancellationToken);
            return await CreateBlockedResultAsync(
                url,
                normalized,
                request.TargetFolderId,
                userId,
                $"This URL was already downloaded on {historyHit.DownloadedAt.ToLocalTime():g}.",
                media?.Id ?? historyHit.MediaItemId,
                media?.Title ?? historyHit.Title,
                cancellationToken);
        }

        var activeDuplicate = await _db.DownloadJobs.AnyAsync(
            j => j.NormalizedUrl == normalized &&
                 (j.Status == DownloadJobStatus.Pending || j.Status == DownloadJobStatus.Running),
            cancellationToken);
        if (activeDuplicate)
        {
            return await CreateBlockedResultAsync(
                url,
                normalized,
                request.TargetFolderId,
                userId,
                "This URL is already in the download queue.",
                null,
                null,
                cancellationToken);
        }

        if (userId.HasValue)
        {
            var (allowed, blockReason) = await _limits.CanEnqueueAsync(userId.Value, cancellationToken);
            if (!allowed)
            {
                return await CreateBlockedResultAsync(
                    url,
                    normalized,
                    request.TargetFolderId,
                    userId,
                    blockReason ?? "Download limit reached.",
                    null,
                    null,
                    cancellationToken);
            }
        }

        var job = new DownloadJob
        {
            Url = url,
            NormalizedUrl = normalized,
            TargetFolderId = await ResolveTargetFolderIdAsync(request.TargetFolderId, cancellationToken),
            CreatedByUserId = userId,
            Status = DownloadJobStatus.Pending,
            NotifyUser = true,
            SourceSite = DownloadSiteNames.FromUrl(url)
        };
        _db.DownloadJobs.Add(job);
        await _db.SaveChangesAsync(cancellationToken);
        _enrichment.EnqueuePreviewFetch(job.Id, url);
        return new EnqueueDownloadResult(true, await ToDtoAsync(job, cancellationToken), null, null, null);
    }

    public async Task<IReadOnlyList<DownloadJobDto>> ListAsync(
        Guid? forUserId = null,
        CancellationToken cancellationToken = default)
    {
        var jobs = (await ApplyUserScope(_db.DownloadJobs, forUserId)
            .ToListAsync(cancellationToken))
            .OrderByDescending(j => j.CreatedAt)
            .ToList();
        var dtos = new List<DownloadJobDto>();
        foreach (var job in jobs)
            dtos.Add(await ToDtoAsync(job, cancellationToken));
        return dtos;
    }

    public async Task<IReadOnlyList<DownloadJobDto>> ListActiveAsync(
        Guid? forUserId = null,
        CancellationToken cancellationToken = default)
    {
        var jobs = (await ApplyUserScope(_db.DownloadJobs, forUserId)
            .Where(j => j.Status == DownloadJobStatus.Pending || j.Status == DownloadJobStatus.Running)
            .ToListAsync(cancellationToken))
            .OrderBy(j => j.CreatedAt)
            .ToList();
        var dtos = new List<DownloadJobDto>();
        foreach (var job in jobs)
            dtos.Add(await ToDtoAsync(job, cancellationToken));
        return dtos;
    }

    public Task<int> CountActiveAsync(Guid? forUserId = null, CancellationToken cancellationToken = default) =>
        ApplyUserScope(_db.DownloadJobs, forUserId)
            .CountAsync(
                j => j.Status == DownloadJobStatus.Pending || j.Status == DownloadJobStatus.Running,
                cancellationToken);

    public async Task<DownloadJobDto?> GetAsync(
        Guid id,
        Guid? forUserId = null,
        CancellationToken cancellationToken = default)
    {
        var job = await FindOwnedJobAsync(id, forUserId, cancellationToken);
        return job is null ? null : await ToDtoAsync(job, cancellationToken);
    }

    public async Task<DownloadJobDto?> RetryAsync(
        Guid id,
        Guid? forUserId = null,
        CancellationToken cancellationToken = default)
    {
        var job = await FindOwnedJobAsync(id, forUserId, cancellationToken);
        if (job is null)
            return null;

        if (job.Status is not (DownloadJobStatus.Failed or DownloadJobStatus.Cancelled or DownloadJobStatus.Blocked))
            throw new InvalidOperationException("Only failed, cancelled, or blocked downloads can be retried.");

        job.Status = DownloadJobStatus.Pending;
        job.ErrorMessage = null;
        job.BlockReason = null;
        job.OutputPath = null;
        job.MetadataJson = null;
        job.ExistingMediaItemId = null;
        job.RetryAfterAt = null;
        job.ProgressPercent = null;
        job.NotifyUser = true;
        job.RetryCount = 0;
        job.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        _enrichment.EnqueuePreviewFetch(job.Id, job.Url);
        return await ToDtoAsync(job, cancellationToken);
    }

    public async Task<DownloadJobDto?> CancelAsync(
        Guid id,
        Guid? forUserId = null,
        CancellationToken cancellationToken = default)
    {
        var job = await FindOwnedJobAsync(id, forUserId, cancellationToken);
        if (job is null)
            return null;

        if (job.Status is not (DownloadJobStatus.Pending or DownloadJobStatus.Running))
            throw new InvalidOperationException("Only pending or running downloads can be cancelled.");

        if (job.Status == DownloadJobStatus.Running)
            _jobTracker.TryCancel(id);

        job.Status = DownloadJobStatus.Cancelled;
        job.ErrorMessage = null;
        job.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return await ToDtoAsync(job, cancellationToken);
    }

    public async Task<bool> DeleteFromQueueAsync(
        Guid id,
        Guid? forUserId = null,
        CancellationToken cancellationToken = default)
    {
        var job = await FindOwnedJobAsync(id, forUserId, cancellationToken);
        if (job is null)
            return false;

        if (job.Status == DownloadJobStatus.Running)
            _jobTracker.TryCancel(id);

        DownloadJobFileCleanup.RemoveCover(job);
        _db.DownloadJobs.Remove(job);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteFromHistoryAsync(string url, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new InvalidOperationException("URL is required.");

        var normalized = DownloadUrlNormalizer.Normalize(url.Trim());
        return await _history.DeleteAsync(normalized, cancellationToken);
    }

    public async Task<IReadOnlyList<AdminDownloadJobDto>> ListAllAsync(CancellationToken cancellationToken = default)
    {
        var jobs = (await _db.DownloadJobs
            .ToListAsync(cancellationToken))
            .OrderByDescending(j => j.CreatedAt)
            .ToList();

        var userIds = jobs
            .Where(j => j.CreatedByUserId.HasValue)
            .Select(j => j.CreatedByUserId!.Value)
            .Distinct()
            .ToList();

        var usernames = userIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.Users.AsNoTracking()
                .Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.Username, cancellationToken);

        var dtos = new List<AdminDownloadJobDto>();
        foreach (var job in jobs)
            dtos.Add(await ToAdminDtoAsync(job, usernames, cancellationToken));
        return dtos;
    }

    public Task<DownloadSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        var download = _config.Current.Download;
        return Task.FromResult(new DownloadSettingsDto(
            download.AllowNonAdminUsers,
            download.AutoRetryFailedDownloads,
            download.AutoRetryDelayMinutes,
            download.HistoryDatabaseFileName,
            download.DefaultDownloadLimitMax,
            download.DefaultDownloadLimitPeriod));
    }

    public async Task<DownloadSettingsDto> UpdateSettingsAsync(
        UpdateDownloadSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.AutoRetryDelayMinutes < 1)
            throw new InvalidOperationException("Auto-retry delay must be at least 1 minute.");

        _config.Current.Download.AllowNonAdminUsers = request.AllowNonAdminUsers;
        _config.Current.Download.AutoRetryFailedDownloads = request.AutoRetryFailedDownloads;
        _config.Current.Download.AutoRetryDelayMinutes = request.AutoRetryDelayMinutes;
        if (request.DefaultDownloadLimitMax.HasValue)
            _config.Current.Download.DefaultDownloadLimitMax = Math.Max(0, request.DefaultDownloadLimitMax.Value);
        if (request.DefaultDownloadLimitPeriod.HasValue)
            _config.Current.Download.DefaultDownloadLimitPeriod = request.DefaultDownloadLimitPeriod.Value;
        await _config.SaveAsync(cancellationToken);
        return await GetSettingsAsync(cancellationToken);
    }

    public Task<IReadOnlyList<DownloadHistoryEntry>> ListHistoryAsync(CancellationToken cancellationToken = default) =>
        _history.ListAsync(cancellationToken: cancellationToken);

    public Task<IReadOnlyList<DownloadSiteAuthDto>> ListSiteAuthAsync(CancellationToken cancellationToken = default)
    {
        var entries = _config.Current.Download.SiteAuth
            .OrderBy(e => e.SiteKey, StringComparer.OrdinalIgnoreCase)
            .Select(ToSiteAuthDto)
            .ToList();
        return Task.FromResult<IReadOnlyList<DownloadSiteAuthDto>>(entries);
    }

    public async Task<DownloadSiteAuthDto> UpsertSiteAuthAsync(
        UpsertDownloadSiteAuthRequest request,
        CancellationToken cancellationToken = default)
    {
        var siteKey = YtDlpAuthHelper.NormalizeSiteKey(request.SiteKey);
        if (string.IsNullOrWhiteSpace(siteKey))
            throw new InvalidOperationException("Site name is required.");

        var authMode = NormalizeAuthMode(request.AuthMode);
        var existing = _config.Current.Download.SiteAuth
            .FirstOrDefault(e => string.Equals(e.SiteKey, siteKey, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            existing = new DownloadSiteAuthConfig { SiteKey = siteKey };
            _config.Current.Download.SiteAuth.Add(existing);
        }

        existing.AuthMode = authMode;
        existing.Username = string.IsNullOrWhiteSpace(request.Username) ? null : request.Username.Trim();
        existing.TestUrl = string.IsNullOrWhiteSpace(request.TestUrl) ? null : request.TestUrl.Trim();

        if (request.Password is not null)
            existing.Password = string.IsNullOrWhiteSpace(request.Password) ? null : request.Password;

        await _config.SaveAsync(cancellationToken);
        return ToSiteAuthDto(existing);
    }

    public async Task RemoveSiteAuthAsync(string siteKey, CancellationToken cancellationToken = default)
    {
        var normalized = YtDlpAuthHelper.NormalizeSiteKey(siteKey);
        if (string.IsNullOrWhiteSpace(normalized))
            throw new InvalidOperationException("Site name is required.");

        var removed = _config.Current.Download.SiteAuth
            .FirstOrDefault(e => string.Equals(e.SiteKey, normalized, StringComparison.OrdinalIgnoreCase));
        if (removed is null)
            throw new InvalidOperationException($"Site login for \"{normalized}\" was not found.");

        _config.Current.Download.SiteAuth.Remove(removed);
        await _config.SaveAsync(cancellationToken);

        var cookiesDir = Path.GetDirectoryName(YtDlpAuthHelper.GetCookiesFilePath(_paths, normalized));
        if (!string.IsNullOrWhiteSpace(cookiesDir) && Directory.Exists(cookiesDir))
        {
            try
            {
                Directory.Delete(cookiesDir, recursive: true);
            }
            catch
            {
                // Best effort cleanup.
            }
        }
    }

    public async Task<TestDownloadSiteAuthResult> TestSiteAuthAsync(
        TestDownloadSiteAuthRequest request,
        CancellationToken cancellationToken = default)
    {
        var siteKey = YtDlpAuthHelper.NormalizeSiteKey(request.SiteKey);
        if (string.IsNullOrWhiteSpace(siteKey))
            throw new InvalidOperationException("Site name is required.");

        var saved = _config.Current.Download.SiteAuth
            .FirstOrDefault(e => string.Equals(e.SiteKey, siteKey, StringComparison.OrdinalIgnoreCase));
        var authMode = NormalizeAuthMode(request.AuthMode ?? saved?.AuthMode ?? DownloadSiteAuthModes.Credentials);
        var entry = new DownloadSiteAuthConfig
        {
            SiteKey = siteKey,
            AuthMode = authMode,
            Username = string.IsNullOrWhiteSpace(request.Username) ? saved?.Username : request.Username.Trim(),
            Password = request.Password ?? saved?.Password,
            TestUrl = string.IsNullOrWhiteSpace(request.TestUrl) ? saved?.TestUrl : request.TestUrl.Trim()
        };

        var testUrl = entry.TestUrl;
        if (string.IsNullOrWhiteSpace(testUrl))
            throw new InvalidOperationException("A test URL is required. Enter a page that requires login on this site.");

        try
        {
            var ytDlp = ResolveYtDlp();
            var args = new List<string> { "--simulate", "--no-warnings", "--print", "extractor" };
            YtDlpAuthHelper.AppendAuthArguments(args, entry, _paths);
            args.Add(testUrl);

            var result = await YtDlpAuthHelper.RunAsync(ytDlp, args, cancellationToken);
            if (result.ExitCode != 0)
            {
                return new TestDownloadSiteAuthResult(
                    false,
                    string.IsNullOrWhiteSpace(result.CombinedOutput)
                        ? "yt-dlp could not access the test URL with the provided login."
                        : result.CombinedOutput,
                    null);
            }

            var extractor = result.StdOut.Trim();
            return new TestDownloadSiteAuthResult(
                true,
                string.IsNullOrWhiteSpace(extractor)
                    ? "Login test succeeded."
                    : $"Login test succeeded (extractor: {extractor}).",
                string.IsNullOrWhiteSpace(extractor) ? null : extractor);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new TestDownloadSiteAuthResult(false, ex.Message, null);
        }
    }

    public async Task<DownloadSiteAuthDto> UploadSiteCookiesAsync(
        string siteKey,
        Stream cookiesFile,
        CancellationToken cancellationToken = default)
    {
        var normalized = YtDlpAuthHelper.NormalizeSiteKey(siteKey);
        if (string.IsNullOrWhiteSpace(normalized))
            throw new InvalidOperationException("Site name is required.");

        if (cookiesFile is null || !cookiesFile.CanRead)
            throw new InvalidOperationException("Cookie file is required.");

        var cookiesPath = YtDlpAuthHelper.GetCookiesFilePath(_paths, normalized);
        Directory.CreateDirectory(Path.GetDirectoryName(cookiesPath)!);

        await using var output = File.Create(cookiesPath);
        await cookiesFile.CopyToAsync(output, cancellationToken);

        var existing = _config.Current.Download.SiteAuth
            .FirstOrDefault(e => string.Equals(e.SiteKey, normalized, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            existing = new DownloadSiteAuthConfig
            {
                SiteKey = normalized,
                AuthMode = DownloadSiteAuthModes.Cookies
            };
            _config.Current.Download.SiteAuth.Add(existing);
        }
        else
        {
            existing.AuthMode = DownloadSiteAuthModes.Cookies;
        }

        await _config.SaveAsync(cancellationToken);
        return ToSiteAuthDto(existing);
    }

    private async Task<Guid> ResolveTargetFolderIdAsync(Guid? folderId, CancellationToken cancellationToken)
    {
        _ = folderId;
        return (await DownloadPaths.EnsureDefaultDownloadFolderAsync(_db, _paths, cancellationToken)).Id;
    }

    private DownloadSiteAuthDto ToSiteAuthDto(DownloadSiteAuthConfig entry)
    {
        var cookiesPath = YtDlpAuthHelper.GetCookiesFilePath(_paths, entry.SiteKey);
        return new DownloadSiteAuthDto(
            entry.SiteKey,
            entry.AuthMode,
            entry.Username,
            !string.IsNullOrWhiteSpace(entry.Password),
            File.Exists(cookiesPath),
            entry.TestUrl);
    }

    private static string NormalizeAuthMode(string authMode)
    {
        if (string.Equals(authMode, DownloadSiteAuthModes.Cookies, StringComparison.OrdinalIgnoreCase))
            return DownloadSiteAuthModes.Cookies;

        return DownloadSiteAuthModes.Credentials;
    }

    private string ResolveYtDlp()
    {
        var configured = _config.Current.YtDlpPath;
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return configured;

        var bundled = _paths.GetToolPath("yt-dlp");
        if (File.Exists(bundled))
            return bundled;

        return "yt-dlp";
    }

    private async Task<EnqueueDownloadResult> CreateBlockedResultAsync(
        string url,
        string normalized,
        Guid? targetFolderId,
        Guid? userId,
        string reason,
        Guid? existingMediaItemId,
        string? existingMediaTitle,
        CancellationToken cancellationToken)
    {
        var job = new DownloadJob
        {
            Url = url,
            NormalizedUrl = normalized,
            TargetFolderId = targetFolderId,
            CreatedByUserId = userId,
            Status = DownloadJobStatus.Blocked,
            BlockReason = reason,
            ExistingMediaItemId = existingMediaItemId,
            ErrorMessage = reason,
            SourceSite = DownloadSiteNames.FromUrl(url)
        };
        _db.DownloadJobs.Add(job);
        await _db.SaveChangesAsync(cancellationToken);

        return new EnqueueDownloadResult(
            false,
            await ToDtoAsync(job, cancellationToken),
            reason,
            existingMediaItemId,
            existingMediaTitle);
    }

    private async Task<MediaItem?> ResolveMediaAsync(
        Guid? mediaItemId,
        string? filePath,
        CancellationToken cancellationToken)
    {
        if (mediaItemId.HasValue)
        {
            var byId = await _db.MediaItems.FindAsync([mediaItemId.Value], cancellationToken);
            if (byId is not null)
                return byId;
        }

        if (!string.IsNullOrWhiteSpace(filePath))
        {
            return await _db.MediaItems.FirstOrDefaultAsync(
                m => m.FilePath == filePath,
                cancellationToken);
        }

        return null;
    }

    private async Task<DownloadJobDto> ToDtoAsync(DownloadJob job, CancellationToken cancellationToken)
    {
        string? existingTitle = null;
        var mediaId = job.ExistingMediaItemId;
        if (mediaId.HasValue)
        {
            var media = await _db.MediaItems.FindAsync([mediaId.Value], cancellationToken);
            existingTitle = media?.Title ?? media?.FileName;
        }

        return new DownloadJobDto(
            job.Id,
            job.Url,
            job.Status,
            job.Title,
            ResolveSiteName(job),
            job.OutputPath,
            job.ErrorMessage,
            job.BlockReason,
            mediaId,
            existingTitle,
            HasCover(job),
            job.ProgressPercent,
            job.NotifyUser,
            job.CreatedAt,
            job.UpdatedAt,
            job.RetryAfterAt,
            job.RetryCount);
    }

    private async Task<AdminDownloadJobDto> ToAdminDtoAsync(
        DownloadJob job,
        IReadOnlyDictionary<Guid, string> usernames,
        CancellationToken cancellationToken)
    {
        string? existingTitle = null;
        var mediaId = job.ExistingMediaItemId;
        if (mediaId.HasValue)
        {
            var media = await _db.MediaItems.FindAsync([mediaId.Value], cancellationToken);
            existingTitle = media?.Title ?? media?.FileName;
        }

        string? username = null;
        if (job.CreatedByUserId is Guid userId && usernames.TryGetValue(userId, out var resolved))
            username = resolved;

        return new AdminDownloadJobDto(
            job.Id,
            job.Url,
            job.Status,
            job.Title,
            ResolveSiteName(job),
            job.OutputPath,
            job.ErrorMessage,
            job.BlockReason,
            mediaId,
            existingTitle,
            HasCover(job),
            job.ProgressPercent,
            job.NotifyUser,
            job.CreatedAt,
            job.UpdatedAt,
            job.RetryAfterAt,
            job.RetryCount,
            job.CreatedByUserId,
            username);
    }

    private static IQueryable<DownloadJob> ApplyUserScope(IQueryable<DownloadJob> query, Guid? forUserId)
    {
        if (!forUserId.HasValue)
            return query;

        return query.Where(j => j.CreatedByUserId == forUserId);
    }

    private async Task<DownloadJob?> FindOwnedJobAsync(
        Guid id,
        Guid? forUserId,
        CancellationToken cancellationToken)
    {
        var job = await _db.DownloadJobs.FindAsync([id], cancellationToken);
        if (job is null)
            return null;

        if (forUserId.HasValue && job.CreatedByUserId != forUserId)
            return null;

        return job;
    }

    private static bool HasCover(DownloadJob job) =>
        !string.IsNullOrWhiteSpace(job.CoverImagePath) && File.Exists(job.CoverImagePath);

    private static string ResolveSiteName(DownloadJob job) =>
        !string.IsNullOrWhiteSpace(job.SourceSite)
            ? job.SourceSite
            : DownloadSiteNames.FromUrl(job.Url);
}

public sealed class DownloadWorker
{
    private const int MaxAutoRetryAttempts = 10;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IPlatformPaths _paths;
    private readonly IConfigManager _configManager;
    private readonly IDownloadJobTracker _jobTracker;
    private readonly IDownloadHistoryStore _history;
    private readonly YtDlpToolInstaller? _ytDlpInstaller;
    private readonly IImageDownloadHandlerRegistry? _imageHandlerRegistry;
    private readonly ILogger<DownloadWorker> _logger;

    public DownloadWorker(
        IServiceScopeFactory scopeFactory,
        IPlatformPaths paths,
        IConfigManager configManager,
        IDownloadJobTracker jobTracker,
        IDownloadHistoryStore history,
        YtDlpToolInstaller? ytDlpInstaller,
        IImageDownloadHandlerRegistry? imageHandlerRegistry,
        ILogger<DownloadWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _paths = paths;
        _configManager = configManager;
        _jobTracker = jobTracker;
        _history = history;
        _ytDlpInstaller = ytDlpInstaller;
        _imageHandlerRegistry = imageHandlerRegistry;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        var cleanedCompletedOnStart = false;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<CherryBoxDbContext>();
                if (!cleanedCompletedOnStart)
                {
                    await CleanupCompletedJobsAsync(db, stoppingToken);
                    cleanedCompletedOnStart = true;
                }

                try
                {
                    await RequeueDueRetriesAsync(db, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Failed to requeue failed downloads for auto-retry.");
                }

                await RecoverInterruptedJobsAsync(db, stoppingToken);

                var pending = await db.DownloadJobs
                    .Where(j => j.Status == DownloadJobStatus.Pending)
                    .ToListAsync(stoppingToken);
                var job = pending.Count == 0 ? null : pending.MinBy(j => j.CreatedAt);

                if (job is null)
                {
                    await Task.Delay(3000, stoppingToken);
                    continue;
                }

                await ProcessJobAsync(scope, db, job, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Download worker error; retrying in 30s.");
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
    }

    private async Task RequeueDueRetriesAsync(CherryBoxDbContext db, CancellationToken stoppingToken)
    {
        if (!_configManager.Current.Download.AutoRetryFailedDownloads)
            return;

        var now = DateTimeOffset.UtcNow;
        var candidates = await db.DownloadJobs
            .Where(j => j.Status == DownloadJobStatus.Failed && j.RetryAfterAt != null)
            .ToListAsync(stoppingToken);

        var due = candidates.Where(j => j.RetryAfterAt <= now).ToList();

        foreach (var job in due)
        {
            if (job.RetryCount >= MaxAutoRetryAttempts)
            {
                await ArchiveAndRemoveFailedJobAsync(db, job, stoppingToken);
                continue;
            }

            job.Status = DownloadJobStatus.Pending;
            job.RetryAfterAt = null;
            job.ErrorMessage = null;
            job.ProgressPercent = null;
            job.NotifyUser = false;
            job.RetryCount++;
            job.UpdatedAt = now;
            _logger.LogInformation("Auto-retry queued download {JobId} (attempt {Attempt})", job.Id, job.RetryCount);
        }

        if (due.Count > 0)
            await db.SaveChangesAsync(stoppingToken);
    }

    private async Task RecoverInterruptedJobsAsync(CherryBoxDbContext db, CancellationToken stoppingToken)
    {
        var running = await db.DownloadJobs
            .Where(j => j.Status == DownloadJobStatus.Running)
            .ToListAsync(stoppingToken);
        if (running.Count == 0)
            return;

        var now = DateTimeOffset.UtcNow;
        var recovered = 0;
        foreach (var job in running)
        {
            if (_jobTracker.IsTracked(job.Id))
                continue;

            job.Status = DownloadJobStatus.Pending;
            job.ErrorMessage = null;
            job.ProgressPercent = null;
            job.NotifyUser = false;
            job.UpdatedAt = now;
            recovered++;
            _logger.LogWarning("Re-queued interrupted download {JobId}", job.Id);
        }

        if (recovered > 0)
            await db.SaveChangesAsync(stoppingToken);
    }

    private async Task ProcessJobAsync(
        IServiceScope scope,
        CherryBoxDbContext db,
        DownloadJob job,
        CancellationToken stoppingToken)
    {
        job.Status = DownloadJobStatus.Running;
        job.ProgressPercent = 0;
        job.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(stoppingToken);

        try
        {
            Uri? jobUri = Uri.TryCreate(job.Url, UriKind.Absolute, out var parsedJobUri) ? parsedJobUri : null;

            if (jobUri is not null && PornhubGifDownloadHelper.IsGifUrl(jobUri))
            {
                await ProcessImageJobAsync(scope, db, job, BuiltinPornhubGifHandler.Instance, stoppingToken);
                return;
            }

            var registry = _imageHandlerRegistry
                ?? scope.ServiceProvider.GetService<IImageDownloadHandlerRegistry>();
            if (registry is not null &&
                jobUri is not null &&
                registry.FindHandler(jobUri) is { } imageHandler)
            {
                await ProcessImageJobAsync(scope, db, job, imageHandler, stoppingToken);
                return;
            }

            if (jobUri is not null && IsImageOnlyHost(jobUri))
            {
                throw new InvalidOperationException(
                    "This image site requires the Image downloader plugin. Open Settings → Plugins, confirm Image downloader is loaded, click Reload plugins, then retry.");
            }

            var download = await RunYtDlpAsync(job, stoppingToken);
            await db.Entry(job).ReloadAsync(stoppingToken);
            if (job.Status == DownloadJobStatus.Cancelled)
                return;

            var (outputPath, folderId) = await FinalizeYtDlpDownloadAsync(db, job, download, stoppingToken);

            job.OutputPath = outputPath;
            job.TargetFolderId = folderId;
            job.Status = DownloadJobStatus.Completed;
            job.ProgressPercent = 100;
            job.UpdatedAt = DateTimeOffset.UtcNow;
            job.MetadataJson = download.MetadataJson ?? job.MetadataJson;
            job.Title = TryExtractTitle(job.MetadataJson) ?? job.Title;
            job.ErrorMessage = null;
            job.BlockReason = null;
            job.RetryAfterAt = null;
            await db.SaveChangesAsync(stoppingToken);

            var scanner = scope.ServiceProvider.GetRequiredService<ILibraryScanner>();
            await scanner.ScanFolderAsync(folderId, stoppingToken);

            var mediaItem = await db.MediaItems.FirstOrDefaultAsync(
                m => m.FilePath == outputPath,
                stoppingToken);
            if (mediaItem is not null)
                job.ExistingMediaItemId = mediaItem.Id;

            var metadataJson = await ResolveCompletedMetadataJsonAsync(scope, job, outputPath, mediaItem, stoppingToken);
            if (!string.IsNullOrWhiteSpace(metadataJson))
            {
                job.MetadataJson = metadataJson;
                job.Title = TryExtractTitle(metadataJson) ?? job.Title;
            }

            await db.SaveChangesAsync(stoppingToken);

            if (mediaItem is not null)
            {
                var processor = scope.ServiceProvider.GetService<IDownloadMediaProcessor>();
                if (processor is not null)
                {
                    await processor.ProcessCompletedDownloadAsync(
                        mediaItem.Id,
                        job.Url,
                        job.MetadataJson,
                        stoppingToken);
                }
                else
                {
                    var library = scope.ServiceProvider.GetService<ILibraryService>();
                    if (library is not null)
                    {
                        await library.UpdateMetadataAsync(
                            mediaItem.Id,
                            new UpdateMediaMetadataRequest(null, null, job.Url, null),
                            stoppingToken);
                    }
                }
            }

            var title = job.Title ?? TryExtractTitle(job.MetadataJson) ?? Path.GetFileNameWithoutExtension(outputPath);
            await _history.RecordAsync(
                job.NormalizedUrl,
                job.Url,
                title,
                outputPath,
                mediaItem?.Id,
                stoppingToken,
                createdByUserId: job.CreatedByUserId);
            await RemoveCompletedJobFromQueueAsync(db, job, stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await db.Entry(job).ReloadAsync(stoppingToken);
            if (job.Status == DownloadJobStatus.Cancelled)
                return;

            job.Status = DownloadJobStatus.Failed;
            job.ErrorMessage = ex.Message;
            job.ProgressPercent = null;
            job.UpdatedAt = DateTimeOffset.UtcNow;
            if (_configManager.Current.Download.AutoRetryFailedDownloads &&
                job.RetryCount < MaxAutoRetryAttempts)
            {
                job.RetryAfterAt = DateTimeOffset.UtcNow.AddMinutes(
                    Math.Max(1, _configManager.Current.Download.AutoRetryDelayMinutes));
                await db.SaveChangesAsync(stoppingToken);
            }
            else
            {
                await db.SaveChangesAsync(stoppingToken);
                if (job.RetryCount >= MaxAutoRetryAttempts &&
                    _configManager.Current.Download.AutoRetryFailedDownloads)
                {
                    await ArchiveAndRemoveFailedJobAsync(db, job, stoppingToken);
                }
            }

            _logger.LogError(ex, "Download failed for {Url}", job.Url);
        }
    }

    private async Task ArchiveAndRemoveFailedJobAsync(
        CherryBoxDbContext db,
        DownloadJob job,
        CancellationToken stoppingToken)
    {
        var title = job.Title ?? job.Url;
        await _history.RecordAsync(
            job.NormalizedUrl,
            job.Url,
            title,
            null,
            null,
            stoppingToken,
            job.ErrorMessage,
            failed: true,
            createdByUserId: job.CreatedByUserId);

        DownloadJobFileCleanup.RemoveCover(job);
        db.DownloadJobs.Remove(job);
        await db.SaveChangesAsync(stoppingToken);
        _logger.LogWarning(
            "Download removed from queue after {MaxRetries} auto-retries: {Url}",
            MaxAutoRetryAttempts,
            job.Url);
    }

    private static async Task RemoveCompletedJobFromQueueAsync(
        CherryBoxDbContext db,
        DownloadJob job,
        CancellationToken stoppingToken)
    {
        DownloadJobFileCleanup.RemoveCover(job);
        db.DownloadJobs.Remove(job);
        await db.SaveChangesAsync(stoppingToken);
    }

    private static async Task CleanupCompletedJobsAsync(
        CherryBoxDbContext db,
        CancellationToken stoppingToken)
    {
        var completed = await db.DownloadJobs
            .Where(j => j.Status == DownloadJobStatus.Completed)
            .ToListAsync(stoppingToken);
        if (completed.Count == 0)
            return;

        foreach (var job in completed)
            DownloadJobFileCleanup.RemoveCover(job);

        db.DownloadJobs.RemoveRange(completed);
        await db.SaveChangesAsync(stoppingToken);
    }

    private async Task ProcessImageJobAsync(
        IServiceScope scope,
        CherryBoxDbContext db,
        DownloadJob job,
        IImageDownloadHandler handler,
        CancellationToken stoppingToken)
    {
        var plan = await handler.BuildPlanAsync(job.Url, stoppingToken);
        if (plan is null || plan.Images.Count == 0)
            throw new InvalidOperationException($"Could not resolve images for {handler.SiteName} URL.");

        var executor = new ImageDownloadExecutor(
            db,
            _paths,
            _configManager,
            _jobTracker,
            _ytDlpInstaller);

        var result = await executor.ExecuteAsync(job, plan, stoppingToken);
        await db.Entry(job).ReloadAsync(stoppingToken);
        if (job.Status == DownloadJobStatus.Cancelled)
            return;

        job.OutputPath = result.OutputPath;
        job.TargetFolderId = result.FolderId;
        job.Status = DownloadJobStatus.Completed;
        job.ProgressPercent = 100;
        job.MetadataJson = result.MetadataJson;
        job.Title = result.Title ?? plan.Title;
        job.SourceSite = plan.SourceSite;
        job.ErrorMessage = null;
        job.BlockReason = null;
        job.RetryAfterAt = null;
        job.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(stoppingToken);

        var scanner = scope.ServiceProvider.GetRequiredService<ILibraryScanner>();
        await scanner.ScanFolderAsync(result.FolderId, stoppingToken);

        var mediaItem = await db.MediaItems.FirstOrDefaultAsync(
            m => m.FilePath == result.OutputPath,
            stoppingToken);
        if (mediaItem is not null)
            job.ExistingMediaItemId = mediaItem.Id;

        await db.SaveChangesAsync(stoppingToken);

        if (mediaItem is not null)
        {
            var processor = scope.ServiceProvider.GetService<IDownloadMediaProcessor>();
            if (processor is not null)
            {
                await processor.ProcessCompletedDownloadAsync(
                    mediaItem.Id,
                    job.Url,
                    job.MetadataJson,
                    stoppingToken);
            }
            else
            {
                var library = scope.ServiceProvider.GetService<ILibraryService>();
                if (library is not null)
                {
                    await library.UpdateMetadataAsync(
                        mediaItem.Id,
                        new UpdateMediaMetadataRequest(job.Title, plan.Description, job.Url, job.MetadataJson),
                        stoppingToken);
                }
            }
        }

        var title = job.Title ?? Path.GetFileNameWithoutExtension(result.OutputPath);
        await _history.RecordAsync(
            job.NormalizedUrl,
            job.Url,
            title,
            result.OutputPath,
            mediaItem?.Id,
            stoppingToken,
            createdByUserId: job.CreatedByUserId);
        await RemoveCompletedJobFromQueueAsync(db, job, stoppingToken);
    }

    private static readonly Regex DownloadProgressRegex =
        new(@"\[download\]\s+(\d+(?:\.\d+)?)%", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private async Task<string?> ResolveCompletedMetadataJsonAsync(
        IServiceScope scope,
        DownloadJob job,
        string outputPath,
        MediaItem? mediaItem,
        CancellationToken cancellationToken)
    {
        var processor = scope.ServiceProvider.GetService<IDownloadMediaProcessor>();
        if (processor is not null)
        {
            var lookup = await processor.LookupAndSerializeMetadataAsync(job.Url, cancellationToken);
            if (!string.IsNullOrWhiteSpace(lookup))
                return lookup;
        }

        if (!string.IsNullOrWhiteSpace(job.MetadataJson))
            return job.MetadataJson;

        var metadata = scope.ServiceProvider.GetRequiredService<StashBoxMetadataService>();
        var encoder = scope.ServiceProvider.GetRequiredService<IMediaEncoder>();
        var fingerprints = scope.ServiceProvider.GetRequiredService<IVideoFingerprintService>();

        var probe = await encoder.ProbeAsync(outputPath, cancellationToken);
        var computed = await fingerprints.ComputeAsync(outputPath, probe?.DurationSeconds, cancellationToken);
        var scene = await metadata.LookupByFingerprintsAsync(
            computed.Md5Hash,
            computed.Phash,
            probe?.DurationSeconds,
            cancellationToken);
        scene ??= await metadata.LookupByUrlAsync(job.Url, cancellationToken);
        return scene is null ? null : JsonSerializer.Serialize(scene);
    }

    private async Task<YtDlpDownloadResult> RunYtDlpAsync(DownloadJob job, CancellationToken cancellationToken)
    {
        await EnsureYtDlpAvailableAsync(cancellationToken);
        var ytDlp = ResolveYtDlp();

        var tempDir = Path.Combine(Path.GetTempPath(), "cherrybox-ytdlp", job.Id.ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var outputTemplate = Path.Combine(tempDir, "%(title)s [%(id)s].%(ext)s");
            var args = new List<string>
            {
                "-o",
                outputTemplate,
                "--write-info-json",
                "--progress",
                "--newline"
            };

            var ffmpegDir = ResolveFfmpegDirectory();
            if (!string.IsNullOrWhiteSpace(ffmpegDir))
            {
                args.Add("--ffmpeg-location");
                args.Add(ffmpegDir);
            }

            var siteAuth = YtDlpAuthHelper.MatchSiteAuth(job.Url, _configManager.Current.Download.SiteAuth);
            if (siteAuth is not null)
                YtDlpAuthHelper.AppendAuthArguments(args, siteAuth, _paths);

            args.Add(job.Url);

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ytDlp,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            foreach (var argument in args)
                process.StartInfo.ArgumentList.Add(argument);

            process.Start();
            _jobTracker.Register(job.Id, process);
            try
            {
                var stderrBuilder = new StringBuilder();
                var progressTask = TrackDownloadProgressAsync(process.StandardError, job.Id, stderrBuilder, cancellationToken);
                var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
                await Task.WhenAll(progressTask, stdoutTask);
                await process.WaitForExitAsync(cancellationToken);

                var stderr = stderrBuilder.ToString();
                if (process.ExitCode != 0)
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr)
                        ? "yt-dlp exited with non-zero code"
                        : stderr.Trim());
            }
            finally
            {
                _jobTracker.Unregister(job.Id);
            }

            var files = Directory.GetFiles(tempDir)
                .Where(f => !f.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (files.Count == 0)
                throw new InvalidOperationException("No file downloaded");

            var metadataJson = await TryLoadInfoJsonFromDownloadAsync(files[0], cancellationToken);
            return new YtDlpDownloadResult(files, metadataJson);
        }
        catch
        {
            TryDeleteDownloadTempDirectory(tempDir);
            throw;
        }
    }

    private async Task<(string OutputPath, Guid FolderId)> FinalizeYtDlpDownloadAsync(
        CherryBoxDbContext db,
        DownloadJob job,
        YtDlpDownloadResult download,
        CancellationToken cancellationToken)
    {
        var tempDir = download.Files.Count > 0 ? Path.GetDirectoryName(download.Files[0]) : null;
        try
        {
            if (ImageDownloadExecutor.AreImageFiles(download.Files))
            {
                var title = job.Title
                    ?? TryExtractTitle(download.MetadataJson)
                    ?? Path.GetFileNameWithoutExtension(download.Files[0]);
                var executor = new ImageDownloadExecutor(db, _paths, _configManager, _jobTracker, _ytDlpInstaller);
                return await executor.RouteDownloadedImagesAsync(download.Files, title, cancellationToken);
            }

            var folderId = job.TargetFolderId
                ?? (await DownloadPaths.EnsureDefaultDownloadFolderAsync(db, _paths, cancellationToken)).Id;
            var folder = await GetOutputFolderAsync(folderId, cancellationToken);
            Directory.CreateDirectory(folder);

            var movedFiles = new List<string>();
            foreach (var file in download.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var dest = UniqueDownloadPath(Path.Combine(folder, Path.GetFileName(file)));
                File.Move(file, dest, overwrite: false);
                movedFiles.Add(dest);

                var infoJson = Path.ChangeExtension(file, ".info.json");
                if (File.Exists(infoJson))
                {
                    var infoDest = Path.ChangeExtension(dest, ".info.json");
                    if (!File.Exists(infoDest))
                        File.Move(infoJson, infoDest, overwrite: false);
                }
            }

            var outputPath = movedFiles
                .OrderByDescending(f => new FileInfo(f).LastWriteTimeUtc)
                .First();

            return (outputPath, folderId);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(tempDir))
                TryDeleteDownloadTempDirectory(tempDir);
        }
    }

    private sealed record YtDlpDownloadResult(IReadOnlyList<string> Files, string? MetadataJson);

    private async Task TrackDownloadProgressAsync(
        StreamReader stderr,
        Guid jobId,
        StringBuilder stderrBuilder,
        CancellationToken cancellationToken)
    {
        var lastPersist = DateTimeOffset.MinValue;
        string? line;
        while ((line = await stderr.ReadLineAsync(cancellationToken)) is not null)
        {
            stderrBuilder.AppendLine(line);
            var match = DownloadProgressRegex.Match(line);
            if (!match.Success)
                continue;

            if (!double.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var percent))
                continue;

            var now = DateTimeOffset.UtcNow;
            if ((now - lastPersist).TotalMilliseconds < 900)
                continue;

            lastPersist = now;
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<CherryBoxDbContext>();
                var tracked = await db.DownloadJobs.FindAsync([jobId], cancellationToken);
                if (tracked is null || tracked.Status != DownloadJobStatus.Running)
                    continue;

                tracked.ProgressPercent = Math.Clamp(percent, 0, 100);
                tracked.UpdatedAt = now;
                await db.SaveChangesAsync(cancellationToken);
            }
            catch
            {
                // Best effort progress updates.
            }
        }
    }

    private async Task<string> GetOutputFolderAsync(Guid? folderId, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CherryBoxDbContext>();
        if (folderId.HasValue)
        {
            var folder = await db.LibraryFolders.FindAsync([folderId.Value], cancellationToken);
            if (folder is not null) return folder.Path;
        }

        return (await DownloadPaths.EnsureDefaultDownloadFolderAsync(db, _paths, cancellationToken)).Path;
    }

    private string ResolveYtDlp()
    {
        var configured = _configManager.Current.YtDlpPath;
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;
        var bundled = _paths.GetToolPath("yt-dlp");
        if (File.Exists(bundled)) return bundled;
        return OperatingSystem.IsWindows() ? "yt-dlp.exe" : "yt-dlp";
    }

    private async Task EnsureYtDlpAvailableAsync(CancellationToken cancellationToken)
    {
        var ytDlp = ResolveYtDlp();
        if (File.Exists(ytDlp))
            return;

        if (_ytDlpInstaller is not null)
        {
            try
            {
                if (await _ytDlpInstaller.EnsureInstalledAsync(cancellationToken) &&
                    File.Exists(ResolveYtDlp()))
                {
                    return;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Automatic yt-dlp installation failed.");
            }
        }

        EnsureYtDlpAvailable(ytDlp);
    }

    private static void EnsureYtDlpAvailable(string ytDlpPath)
    {
        if (File.Exists(ytDlpPath))
            return;

        if (Path.IsPathRooted(ytDlpPath))
            throw new InvalidOperationException(
                $"yt-dlp was not found at '{ytDlpPath}'. Install it from Settings → Updating → Support apps.");

        throw new InvalidOperationException(
            "yt-dlp is not installed. Install it from Settings → Updating → Support apps, or set YtDlpPath in config.");
    }

    private string? ResolveFfmpegDirectory()
    {
        var configured = _configManager.Current.FfmpegPath;
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return Path.GetDirectoryName(configured);

        var bundled = _paths.GetToolPath("ffmpeg");
        if (File.Exists(bundled))
            return Path.GetDirectoryName(bundled);

        return null;
    }

    private static async Task<string?> TryLoadInfoJsonFromDownloadAsync(string outputPath, CancellationToken cancellationToken)
    {
        var jsonPath = Path.ChangeExtension(outputPath, ".info.json");
        if (!File.Exists(jsonPath))
            return null;

        return await File.ReadAllTextAsync(jsonPath, cancellationToken);
    }

    private static string UniqueDownloadPath(string path)
    {
        if (!File.Exists(path))
            return path;

        var dir = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (var i = 2; i < 1000; i++)
        {
            var candidate = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(candidate))
                return candidate;
        }

        return Path.Combine(dir, $"{name}-{Guid.NewGuid():N}{ext}");
    }

    private static void TryDeleteDownloadTempDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }

    private static async Task<string?> TryLoadInfoJsonAsync(string outputPath, CancellationToken cancellationToken)
    {
        var jsonPath = Path.ChangeExtension(outputPath, ".info.json");
        if (!File.Exists(jsonPath)) return null;
        return await File.ReadAllTextAsync(jsonPath, cancellationToken);
    }

    private static string? TryExtractTitle(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(metadataJson);
            if (doc.RootElement.TryGetProperty("title", out var title))
                return title.GetString();
        }
        catch
        {
        }

        return null;
    }

    private static bool IsImageOnlyHost(Uri url)
    {
        var host = url.Host;
        return host.Contains("imagefap.com", StringComparison.OrdinalIgnoreCase)
            || host.Contains("imagefap.net", StringComparison.OrdinalIgnoreCase);
    }
}

internal static class DownloadPaths
{
    public static string GetDefaultDownloadDirectory(IPlatformPaths paths) =>
        Path.Combine(paths.ProgramDataDirectory, "downloads");

    public static async Task<LibraryFolder> EnsureDefaultDownloadFolderAsync(
        CherryBoxDbContext db,
        IPlatformPaths paths,
        CancellationToken cancellationToken)
    {
        var configured = await db.LibraryFolders
            .Where(f => f.Enabled && f.ContentKind == ContentKind.Downloads)
            .OrderBy(f => f.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (configured is not null)
            return configured;

        var downloadPath = Path.GetFullPath(GetDefaultDownloadDirectory(paths));
        Directory.CreateDirectory(downloadPath);

        var folders = await db.LibraryFolders.ToListAsync(cancellationToken);
        var existing = folders.FirstOrDefault(f =>
            string.Equals(Path.GetFullPath(f.Path), downloadPath, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            if (existing.ContentKind != ContentKind.Downloads)
            {
                existing.ContentKind = ContentKind.Downloads;
                existing.UpdatedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
            }

            return existing;
        }

        var folder = new LibraryFolder
        {
            Path = downloadPath,
            DisplayName = "Downloads",
            ContentKind = ContentKind.Downloads,
            Enabled = true
        };
        db.LibraryFolders.Add(folder);
        await db.SaveChangesAsync(cancellationToken);
        return folder;
    }
}

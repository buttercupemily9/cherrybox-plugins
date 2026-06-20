using System.Diagnostics;
using System.Text.Json;
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

    public DownloadService(
        CherryBoxDbContext db,
        IDownloadHistoryStore history,
        IConfigManager config,
        IDownloadJobTracker jobTracker,
        IPlatformPaths paths)
    {
        _db = db;
        _history = history;
        _config = config;
        _jobTracker = jobTracker;
        _paths = paths;
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
        if (historyHit is not null)
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

        var job = new DownloadJob
        {
            Url = url,
            NormalizedUrl = normalized,
            TargetFolderId = request.TargetFolderId,
            CreatedByUserId = userId,
            Status = DownloadJobStatus.Pending
        };
        _db.DownloadJobs.Add(job);
        await _db.SaveChangesAsync(cancellationToken);
        return new EnqueueDownloadResult(true, await ToDtoAsync(job, cancellationToken), null, null, null);
    }

    public async Task<IReadOnlyList<DownloadJobDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var jobs = await _db.DownloadJobs.ToListAsync(cancellationToken);
        var dtos = new List<DownloadJobDto>();
        foreach (var job in jobs.OrderByDescending(j => j.CreatedAt))
            dtos.Add(await ToDtoAsync(job, cancellationToken));
        return dtos;
    }

    public async Task<IReadOnlyList<DownloadJobDto>> ListActiveAsync(CancellationToken cancellationToken = default)
    {
        var jobs = await _db.DownloadJobs
            .Where(j => j.Status == DownloadJobStatus.Pending || j.Status == DownloadJobStatus.Running)
            .ToListAsync(cancellationToken);
        var dtos = new List<DownloadJobDto>();
        foreach (var job in jobs.OrderBy(j => j.CreatedAt))
            dtos.Add(await ToDtoAsync(job, cancellationToken));
        return dtos;
    }

    public async Task<DownloadJobDto?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var job = await _db.DownloadJobs.FindAsync([id], cancellationToken);
        return job is null ? null : await ToDtoAsync(job, cancellationToken);
    }

    public async Task<DownloadJobDto?> RetryAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var job = await _db.DownloadJobs.FindAsync([id], cancellationToken);
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
        job.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return await ToDtoAsync(job, cancellationToken);
    }

    public async Task<DownloadJobDto?> CancelAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var job = await _db.DownloadJobs.FindAsync([id], cancellationToken);
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

    public Task<DownloadSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        var download = _config.Current.Download;
        return Task.FromResult(new DownloadSettingsDto(
            download.AutoRetryFailedDownloads,
            download.AutoRetryDelayMinutes,
            download.HistoryDatabaseFileName));
    }

    public async Task<DownloadSettingsDto> UpdateSettingsAsync(
        UpdateDownloadSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.AutoRetryDelayMinutes < 1)
            throw new InvalidOperationException("Auto-retry delay must be at least 1 minute.");

        _config.Current.Download.AutoRetryFailedDownloads = request.AutoRetryFailedDownloads;
        _config.Current.Download.AutoRetryDelayMinutes = request.AutoRetryDelayMinutes;
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
            ErrorMessage = reason
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
            job.OutputPath,
            job.ErrorMessage,
            job.BlockReason,
            mediaId,
            existingTitle,
            job.CreatedAt,
            job.UpdatedAt,
            job.RetryAfterAt,
            job.RetryCount);
    }
}

public sealed class DownloadWorker
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IPlatformPaths _paths;
    private readonly IConfigManager _configManager;
    private readonly IDownloadJobTracker _jobTracker;
    private readonly ILogger<DownloadWorker> _logger;

    public DownloadWorker(
        IServiceScopeFactory scopeFactory,
        IPlatformPaths paths,
        IConfigManager configManager,
        IDownloadJobTracker jobTracker,
        ILogger<DownloadWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _paths = paths;
        _configManager = configManager;
        _jobTracker = jobTracker;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<CherryBoxDbContext>();
                await RequeueDueRetriesAsync(db, stoppingToken);

                var pending = await db.DownloadJobs
                    .Where(j => j.Status == DownloadJobStatus.Pending)
                    .ToListAsync(stoppingToken);
                var job = pending.MinBy(j => j.CreatedAt);

                if (job is null)
                {
                    await Task.Delay(3000, stoppingToken);
                    continue;
                }

                await ProcessJobAsync(scope, db, job, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning("Download worker paused — database unavailable. Retrying in 30s.");
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
    }

    private async Task RequeueDueRetriesAsync(CherryBoxDbContext db, CancellationToken stoppingToken)
    {
        if (!_configManager.Current.Download.AutoRetryFailedDownloads)
            return;

        var now = DateTimeOffset.UtcNow;
        var due = await db.DownloadJobs
            .Where(j => j.Status == DownloadJobStatus.Failed &&
                        j.RetryAfterAt != null &&
                        j.RetryAfterAt <= now)
            .ToListAsync(stoppingToken);

        foreach (var job in due)
        {
            job.Status = DownloadJobStatus.Pending;
            job.RetryAfterAt = null;
            job.ErrorMessage = null;
            job.RetryCount++;
            job.UpdatedAt = now;
            _logger.LogInformation("Auto-retry queued download {JobId} (attempt {Attempt})", job.Id, job.RetryCount);
        }

        if (due.Count > 0)
            await db.SaveChangesAsync(stoppingToken);
    }

    private async Task ProcessJobAsync(
        IServiceScope scope,
        CherryBoxDbContext db,
        DownloadJob job,
        CancellationToken stoppingToken)
    {
        job.Status = DownloadJobStatus.Running;
        job.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(stoppingToken);

        try
        {
            var outputPath = await RunYtDlpAsync(job, stoppingToken);
            await db.Entry(job).ReloadAsync(stoppingToken);
            if (job.Status == DownloadJobStatus.Cancelled)
                return;

            job.OutputPath = outputPath;
            job.Status = DownloadJobStatus.Completed;
            job.MetadataJson = await TryLoadInfoJsonAsync(outputPath, stoppingToken);
            job.ErrorMessage = null;
            job.BlockReason = null;
            job.RetryAfterAt = null;
            await db.SaveChangesAsync(stoppingToken);

            var scanner = scope.ServiceProvider.GetRequiredService<ILibraryScanner>();
            if (job.TargetFolderId.HasValue)
                await scanner.ScanFolderAsync(job.TargetFolderId.Value, stoppingToken);

            var metadata = scope.ServiceProvider.GetRequiredService<StashBoxMetadataService>();
            var encoder = scope.ServiceProvider.GetRequiredService<IMediaEncoder>();
            var fingerprints = scope.ServiceProvider.GetRequiredService<IVideoFingerprintService>();
            var history = scope.ServiceProvider.GetRequiredService<IDownloadHistoryStore>();

            var probe = await encoder.ProbeAsync(outputPath, stoppingToken);
            var computed = await fingerprints.ComputeAsync(outputPath, probe?.DurationSeconds, stoppingToken);
            var scene = await metadata.LookupByFingerprintsAsync(
                computed.Md5Hash,
                computed.Phash,
                probe?.DurationSeconds,
                stoppingToken);
            if (scene is null)
                scene = await metadata.LookupByUrlAsync(job.Url, stoppingToken);
            if (scene is not null)
                job.MetadataJson = JsonSerializer.Serialize(scene);
            await db.SaveChangesAsync(stoppingToken);

            var mediaItem = await db.MediaItems.FirstOrDefaultAsync(
                m => m.FilePath == outputPath,
                stoppingToken);
            var title = TryExtractTitle(job.MetadataJson) ?? Path.GetFileNameWithoutExtension(outputPath);
            await history.RecordAsync(
                job.NormalizedUrl,
                job.Url,
                title,
                outputPath,
                mediaItem?.Id,
                stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await db.Entry(job).ReloadAsync(stoppingToken);
            if (job.Status == DownloadJobStatus.Cancelled)
                return;

            job.Status = DownloadJobStatus.Failed;
            job.ErrorMessage = ex.Message;
            job.UpdatedAt = DateTimeOffset.UtcNow;
            if (_configManager.Current.Download.AutoRetryFailedDownloads)
            {
                job.RetryAfterAt = DateTimeOffset.UtcNow.AddMinutes(
                    Math.Max(1, _configManager.Current.Download.AutoRetryDelayMinutes));
            }

            await db.SaveChangesAsync(stoppingToken);
            _logger.LogError(ex, "Download failed for {Url}", job.Url);
        }
    }

    private async Task<string> RunYtDlpAsync(DownloadJob job, CancellationToken cancellationToken)
    {
        var ytDlp = ResolveYtDlp();
        var folder = await GetOutputFolderAsync(job.TargetFolderId, cancellationToken);
        Directory.CreateDirectory(folder);

        var outputTemplate = Path.Combine(folder, "%(title)s [%(id)s].%(ext)s");
        var args = new List<string>
        {
            "-o",
            outputTemplate,
            "--write-info-json"
        };

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
            var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode != 0)
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr)
                    ? "yt-dlp exited with non-zero code"
                    : stderr.Trim());
        }
        finally
        {
            _jobTracker.Unregister(job.Id);
        }

        var downloaded = Directory.GetFiles(folder)
            .Where(f => !f.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => new FileInfo(f).LastWriteTimeUtc)
            .FirstOrDefault();

        if (downloaded is null)
            throw new InvalidOperationException("No file downloaded");

        return downloaded;
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

        var defaultPath = Path.Combine(_paths.ProgramDataDirectory, "downloads");
        Directory.CreateDirectory(defaultPath);
        return defaultPath;
    }

    private string ResolveYtDlp()
    {
        var configured = _configManager.Current.YtDlpPath;
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;
        var bundled = _paths.GetToolPath("yt-dlp");
        if (File.Exists(bundled)) return bundled;
        return "yt-dlp";
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
}

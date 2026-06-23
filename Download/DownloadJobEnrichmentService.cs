using System.Net.Http.Headers;
using System.Text.Json;
using CherryBox.Core.Configuration;
using CherryBox.Core.Entities;
using CherryBox.Core.Platform;
using CherryBox.Data;
using CherryBox.Plugins.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CherryBox.Download.Plugin;

public sealed class DownloadJobEnrichmentService
{
    private static readonly HttpClient ThumbnailClient = CreateThumbnailClient();

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IPlatformPaths _paths;
    private readonly IConfigManager _config;
    private readonly ILogger<DownloadJobEnrichmentService> _logger;

    public DownloadJobEnrichmentService(
        IServiceScopeFactory scopeFactory,
        IPlatformPaths paths,
        IConfigManager config,
        ILogger<DownloadJobEnrichmentService> logger)
    {
        _scopeFactory = scopeFactory;
        _paths = paths;
        _config = config;
        _logger = logger;
    }

    public void EnqueuePreviewFetch(Guid jobId, string url)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await FetchPreviewAsync(jobId, url, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Preview fetch failed for download {JobId}", jobId);
            }
        });
    }

    private async Task FetchPreviewAsync(Guid jobId, string url, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CherryBoxDbContext>();
        var job = await db.DownloadJobs.FindAsync([jobId], cancellationToken);
        if (job is null)
            return;

        if (string.IsNullOrWhiteSpace(job.SourceSite))
            job.SourceSite = DownloadSiteNames.FromUrl(url);

        var ytDlp = ResolveYtDlp();
        var coversDir = GetCoversDirectory();
        Directory.CreateDirectory(coversDir);
        var coverPath = Path.Combine(coversDir, $"{jobId}.jpg");

        await FetchMetadataAsync(scope, job, url, ytDlp, cancellationToken);
        ApplyMetadataFields(job, url);

        if (string.IsNullOrWhiteSpace(job.CoverImagePath) || !File.Exists(job.CoverImagePath))
        {
            var thumbnailUrl = TryExtractThumbnailUrl(job.MetadataJson);
            if (!string.IsNullOrWhiteSpace(thumbnailUrl))
            {
                await TryDownloadThumbnailFromUrlAsync(thumbnailUrl, coverPath, cancellationToken);
                if (File.Exists(coverPath))
                    job.CoverImagePath = coverPath;
            }
        }

        if (string.IsNullOrWhiteSpace(job.CoverImagePath) || !File.Exists(job.CoverImagePath))
            await FetchThumbnailViaYtDlpAsync(job, url, ytDlp, coversDir, coverPath, cancellationToken);

        job.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task FetchMetadataAsync(
        IServiceScope scope,
        DownloadJob job,
        string url,
        string ytDlp,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(job.MetadataJson))
            return;

        var processor = scope.ServiceProvider.GetService<IDownloadMediaProcessor>();
        if (processor is not null)
        {
            try
            {
                job.MetadataJson = await processor.LookupAndSerializeMetadataAsync(url, cancellationToken);
                if (!string.IsNullOrWhiteSpace(job.MetadataJson))
                    return;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Metadata lookup failed for {Url}", url);
            }
        }

        if (!File.Exists(ytDlp) && ytDlp is not ("yt-dlp" or "yt-dlp.exe"))
            return;

        try
        {
            var jsonArgs = BuildBaseArgs(url);
            jsonArgs.Add("--skip-download");
            jsonArgs.Add("--dump-single-json");
            jsonArgs.Add("--no-warnings");
            var jsonResult = await YtDlpAuthHelper.RunAsync(ytDlp, jsonArgs, cancellationToken);
            if (jsonResult.ExitCode == 0 && !string.IsNullOrWhiteSpace(jsonResult.StdOut))
                job.MetadataJson = jsonResult.StdOut.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "yt-dlp metadata fetch failed for {Url}", url);
        }
    }

    private void ApplyMetadataFields(DownloadJob job, string url)
    {
        if (string.IsNullOrWhiteSpace(job.Title) && !string.IsNullOrWhiteSpace(job.MetadataJson))
            job.Title = TryExtractTitle(job.MetadataJson);

        var extractor = TryExtractExtractor(job.MetadataJson);
        if (!string.IsNullOrWhiteSpace(extractor))
            job.SourceSite = extractor;
        else if (string.IsNullOrWhiteSpace(job.SourceSite))
            job.SourceSite = DownloadSiteNames.FromUrl(url);
    }

    private async Task FetchThumbnailViaYtDlpAsync(
        DownloadJob job,
        string url,
        string ytDlp,
        string coversDir,
        string coverPath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(ytDlp) && ytDlp is not ("yt-dlp" or "yt-dlp.exe"))
            return;

        var args = BuildBaseArgs(url);
        args.Add("--skip-download");
        args.Add("--write-thumbnail");
        args.Add("--convert-thumbnails");
        args.Add("jpg");
        args.Add("-o");
        args.Add(Path.Combine(coversDir, $"{job.Id}.%(ext)s"));

        try
        {
            var result = await YtDlpAuthHelper.RunAsync(ytDlp, args, cancellationToken);
            if (result.ExitCode != 0)
                return;

            var thumbnail = Directory.GetFiles(coversDir, $"{job.Id}.*")
                .FirstOrDefault(f => !f.EndsWith(".part", StringComparison.OrdinalIgnoreCase));
            if (thumbnail is null)
                return;

            if (!thumbnail.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
            {
                File.Move(thumbnail, coverPath, overwrite: true);
                thumbnail = coverPath;
            }

            job.CoverImagePath = thumbnail;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Thumbnail fetch failed for {Url}", url);
        }
    }

    private async Task TryDownloadThumbnailFromUrlAsync(
        string thumbnailUrl,
        string coverPath,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await ThumbnailClient.GetAsync(thumbnailUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return;

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var file = File.Create(coverPath);
            await stream.CopyToAsync(file, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to download thumbnail from {Url}", thumbnailUrl);
        }
    }

    private List<string> BuildBaseArgs(string url)
    {
        var args = new List<string> { "--no-playlist", "--no-warnings" };
        var ffmpegDir = ResolveFfmpegDirectory();
        if (!string.IsNullOrWhiteSpace(ffmpegDir))
        {
            args.Add("--ffmpeg-location");
            args.Add(ffmpegDir);
        }

        var siteAuth = YtDlpAuthHelper.MatchSiteAuth(url, _config.Current.Download.SiteAuth);
        if (siteAuth is not null)
            YtDlpAuthHelper.AppendAuthArguments(args, siteAuth, _paths);

        args.Add(url);
        return args;
    }

    private string GetCoversDirectory() =>
        Path.Combine(_paths.ProgramDataDirectory, "download-covers");

    private string ResolveYtDlp()
    {
        var configured = _config.Current.YtDlpPath;
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return configured;

        var bundled = _paths.GetToolPath("yt-dlp");
        if (File.Exists(bundled))
            return bundled;

        return OperatingSystem.IsWindows() ? "yt-dlp.exe" : "yt-dlp";
    }

    private string? ResolveFfmpegDirectory()
    {
        var configured = _config.Current.FfmpegPath;
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return Path.GetDirectoryName(configured);

        var bundled = _paths.GetToolPath("ffmpeg");
        if (File.Exists(bundled))
            return Path.GetDirectoryName(bundled);

        return null;
    }

    private static HttpClient CreateThumbnailClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; CherryBox/1.1)");
        client.Timeout = TimeSpan.FromSeconds(30);
        return client;
    }

    private static string? TryExtractTitle(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(metadataJson);
            var root = doc.RootElement;
            foreach (var property in new[] { "title", "fulltitle", "track", "alt_title" })
            {
                if (!root.TryGetProperty(property, out var el))
                    continue;

                var value = el.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }
        }
        catch
        {
        }

        return null;
    }

    private static string? TryExtractExtractor(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(metadataJson);
            var root = doc.RootElement;
            foreach (var property in new[] { "extractor_key", "ie_key", "extractor" })
            {
                if (!root.TryGetProperty(property, out var el))
                    continue;

                var formatted = DownloadSiteNames.FormatExtractor(el.GetString());
                if (!string.IsNullOrWhiteSpace(formatted))
                    return formatted;
            }
        }
        catch
        {
        }

        return null;
    }

    private static string? TryExtractThumbnailUrl(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(metadataJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("thumbnail", out var thumb) &&
                thumb.ValueKind == JsonValueKind.String)
            {
                var url = thumb.GetString();
                if (!string.IsNullOrWhiteSpace(url))
                    return url;
            }

            if (root.TryGetProperty("thumbnails", out var thumbs) &&
                thumbs.ValueKind == JsonValueKind.Array)
            {
                string? bestUrl = null;
                var bestWidth = -1;
                foreach (var entry in thumbs.EnumerateArray())
                {
                    if (!entry.TryGetProperty("url", out var urlEl))
                        continue;

                    var url = urlEl.GetString();
                    if (string.IsNullOrWhiteSpace(url))
                        continue;

                    var width = entry.TryGetProperty("width", out var widthEl) && widthEl.TryGetInt32(out var w)
                        ? w
                        : 0;
                    if (width >= bestWidth)
                    {
                        bestWidth = width;
                        bestUrl = url;
                    }
                }

                if (!string.IsNullOrWhiteSpace(bestUrl))
                    return bestUrl;
            }
        }
        catch
        {
        }

        return null;
    }
}

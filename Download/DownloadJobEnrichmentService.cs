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

        await FetchMetadataAsync(scope, job, url, ytDlp, cancellationToken);
        ApplyMetadataFields(job, url);

        var referer = TryExtractPageUrl(job.MetadataJson) ?? url;
        if (string.IsNullOrWhiteSpace(job.CoverImagePath) || !File.Exists(job.CoverImagePath))
        {
            var thumbnailUrl = TryExtractThumbnailUrl(job.MetadataJson);
            if (!string.IsNullOrWhiteSpace(thumbnailUrl))
            {
                var coverPath = DownloadCoverSupport.BuildCoverPath(coversDir, jobId, thumbnailUrl);
                await TryDownloadThumbnailFromUrlAsync(thumbnailUrl, coverPath, referer, cancellationToken);
                if (File.Exists(coverPath))
                    job.CoverImagePath = coverPath;
            }
        }

        if ((string.IsNullOrWhiteSpace(job.CoverImagePath) || !File.Exists(job.CoverImagePath)) &&
            IsImageDownloadUrl(url))
        {
            await TryFetchImageHandlerCoverAsync(scope, job, url, coversDir, cancellationToken);
        }

        if ((string.IsNullOrWhiteSpace(job.CoverImagePath) || !File.Exists(job.CoverImagePath)) &&
            !IsImageDownloadUrl(url))
        {
            var coverPath = DownloadCoverSupport.BuildCoverPath(coversDir, jobId, null);
            await FetchThumbnailViaYtDlpAsync(job, url, ytDlp, coversDir, coverPath, cancellationToken);
        }

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

        if (await TryEnrichFromImageHandlerAsync(scope, job, url, cancellationToken))
            return;

        if (IsImageDownloadUrl(url))
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

    private async Task TryFetchImageHandlerCoverAsync(
        IServiceScope scope,
        DownloadJob job,
        string url,
        string coversDir,
        CancellationToken cancellationToken)
    {
        var uri = TryParseJobUri(url);
        if (uri is null)
            return;

        IImageDownloadHandler? handler = null;
        if (PornhubGifDownloadHelper.IsGifUrl(uri))
            handler = BuiltinPornhubGifHandler.Instance;
        else
            handler = scope.ServiceProvider.GetService<IImageDownloadHandlerRegistry>()?.FindHandler(uri);

        if (handler is null)
            return;

        try
        {
            var plan = await handler.BuildPlanAsync(url, cancellationToken);
            var imageUrl = plan?.Images.FirstOrDefault()?.Url;
            if (string.IsNullOrWhiteSpace(imageUrl))
                return;

            var coverPath = DownloadCoverSupport.BuildCoverPath(coversDir, job.Id, imageUrl);
            await TryDownloadThumbnailFromUrlAsync(imageUrl, coverPath, plan!.SourceUrl, cancellationToken);
            if (File.Exists(coverPath))
                job.CoverImagePath = coverPath;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Image handler cover fetch failed for {Url}", url);
        }
    }

    private async Task<bool> TryEnrichFromImageHandlerAsync(
        IServiceScope scope,
        DownloadJob job,
        string url,
        CancellationToken cancellationToken)
    {
        var uri = TryParseJobUri(url);
        if (uri is null)
            return false;

        IImageDownloadHandler? handler = null;
        if (PornhubGifDownloadHelper.IsGifUrl(uri))
            handler = BuiltinPornhubGifHandler.Instance;
        else
            handler = scope.ServiceProvider.GetService<IImageDownloadHandlerRegistry>()?.FindHandler(uri);

        if (handler is null)
            return false;

        try
        {
            var plan = await handler.BuildPlanAsync(url, cancellationToken);
            if (plan is null || plan.Images.Count == 0)
                return false;

            job.Title = plan.Title;
            job.SourceSite = plan.SourceSite ?? job.SourceSite;
            job.MetadataJson = ImageDownloadMetadata.SerializePlan(plan);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Image handler preview failed for {Url}", url);
            return false;
        }
    }

    private static Uri? TryParseJobUri(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return uri;

        var normalized = url.Contains("://", StringComparison.Ordinal) ? url : "https://" + url;
        return Uri.TryCreate(normalized, UriKind.Absolute, out uri) ? uri : null;
    }

    private static bool IsImageDownloadUrl(string url)
    {
        var uri = TryParseJobUri(url);
        if (uri is null)
            return false;

        if (PornhubGifDownloadHelper.IsGifUrl(uri))
            return true;

        return uri.Host.Contains("sex.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Contains("imagefap", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Contains("motherless", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Contains("rule34", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Contains("xhamster", StringComparison.OrdinalIgnoreCase);
    }

    private async Task TryDownloadThumbnailFromUrlAsync(
        string thumbnailUrl,
        string coverPath,
        string? refererUrl,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, thumbnailUrl);
            if (!string.IsNullOrWhiteSpace(refererUrl) &&
                Uri.TryCreate(refererUrl, UriKind.Absolute, out var referer))
            {
                request.Headers.Referrer = referer;
            }

            using var response = await ThumbnailClient.SendAsync(request, cancellationToken);
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

        var siteAuth = YtDlpAuthHelper.MatchSiteAuth(url, _config.Current.SiteAuth);
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

    private static string? TryExtractPageUrl(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(metadataJson);
            var root = doc.RootElement;
            foreach (var property in new[] { "webpage_url", "url", "original_url" })
            {
                if (!root.TryGetProperty(property, out var el))
                    continue;

                var value = el.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
        }
        catch
        {
        }

        return null;
    }
}

internal static class ImageDownloadMetadata
{
    public static string SerializePlan(ImageDownloadPlan plan)
    {
        var payload = new Dictionary<string, object?>
        {
            ["title"] = plan.Title,
            ["url"] = plan.SourceUrl,
            ["webpage_url"] = plan.SourceUrl,
            ["sourceSite"] = plan.SourceSite,
            ["description"] = plan.Description,
            ["downloader"] = "image-download",
            ["imageCount"] = plan.Images.Count,
        };
        if (plan.Tags is { Count: > 0 })
            payload["tags"] = plan.Tags;

        return JsonSerializer.Serialize(payload);
    }
}

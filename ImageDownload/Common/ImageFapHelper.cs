using System.Text.RegularExpressions;
using CherryBox.Plugins.Abstractions;
using HtmlAgilityPack;

namespace ImageDownload.Plugin.Common;

internal static class ImageFapHelper
{
    private static readonly Regex GalleryIdRegex = new(@"/gallery(?:\.php)?/(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex GidQueryRegex = new(@"[?&]gid=(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PhotoIdRegex = new(@"/photo/(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex FullImageRegex = new(
        @"https://cdnc\.imagefap\.com/images/full/\d+/\d+/\d+\.(?:jpe?g|png|gif|webp)(?:\?[^""\s<>]*)?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    internal static async Task<ImageDownloadPlan?> BuildPlanAsync(string url, CancellationToken cancellationToken)
    {
        var pageUri = SiteHostMatcher.NormalizeUrl(url);
        var galleryId = TryExtractGalleryId(pageUri);
        if (galleryId is not null)
            return await BuildGalleryPlanAsync(pageUri, galleryId, cancellationToken);

        var photoId = TryExtractPhotoId(pageUri);
        if (photoId is not null)
            return await BuildSinglePhotoPlanAsync(pageUri, photoId, cancellationToken);

        return null;
    }

    private static async Task<ImageDownloadPlan?> BuildGalleryPlanAsync(
        Uri pageUri,
        string galleryId,
        CancellationToken cancellationToken)
    {
        var photoIds = new HashSet<string>(StringComparer.Ordinal);
        string? title = null;
        string? description = null;

        for (var page = 0; page < 200; page++)
        {
            var pageUrl = BuildGalleryPageUrl(pageUri, galleryId, page);
            var html = await ImageFetchHelper.GetStringAsync(pageUrl, cancellationToken);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            title ??= HtmlGalleryScraper.PageTitle(doc);
            description ??= HtmlGalleryScraper.MetaContent(doc, "og:description")
                ?? HtmlGalleryScraper.MetaContent(doc, "description");

            var foundOnPage = 0;
            foreach (Match match in PhotoIdRegex.Matches(html))
            {
                if (photoIds.Add(match.Groups[1].Value))
                    foundOnPage++;
            }

            if (foundOnPage == 0)
                break;
        }

        if (photoIds.Count == 0)
            return null;

        var items = new List<ImageDownloadItem>();
        foreach (var photoId in photoIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var imageUrl = await ResolveFullImageUrlAsync(photoId, galleryId, cancellationToken);
            if (string.IsNullOrWhiteSpace(imageUrl))
                continue;

            items.Add(new ImageDownloadItem(imageUrl, FileNameForPhoto(photoId, imageUrl)));
        }

        if (items.Count == 0)
            return null;

        return new ImageDownloadPlan(
            title ?? "ImageFap gallery",
            pageUri.ToString(),
            "ImageFap",
            ImageDownloadExecutionMode.DirectHttp,
            items,
            description,
            Tags: null);
    }

    private static async Task<ImageDownloadPlan?> BuildSinglePhotoPlanAsync(
        Uri pageUri,
        string photoId,
        CancellationToken cancellationToken)
    {
        var html = await ImageFetchHelper.GetStringAsync(pageUri.ToString(), cancellationToken);
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var imageUrl = ExtractFullImageUrl(html, photoId);
        if (string.IsNullOrWhiteSpace(imageUrl))
            return null;

        var title = HtmlGalleryScraper.PageTitle(doc) ?? $"ImageFap photo {photoId}";
        return new ImageDownloadPlan(
            title,
            pageUri.ToString(),
            "ImageFap",
            ImageDownloadExecutionMode.DirectHttp,
            [new ImageDownloadItem(imageUrl, FileNameForPhoto(photoId, imageUrl))],
            HtmlGalleryScraper.MetaContent(doc, "og:description"),
            Tags: null);
    }

    private static async Task<string?> ResolveFullImageUrlAsync(
        string photoId,
        string galleryId,
        CancellationToken cancellationToken)
    {
        var photoUrl = $"https://www.imagefap.com/photo/{photoId}/?gid={galleryId}";
        var html = await ImageFetchHelper.GetStringAsync(photoUrl, cancellationToken);
        return ExtractFullImageUrl(html, photoId);
    }

    private static string? ExtractFullImageUrl(string html, string photoId)
    {
        foreach (Match match in FullImageRegex.Matches(html))
        {
            if (match.Value.Contains($"/{photoId}.", StringComparison.Ordinal))
                return match.Value.TrimEnd('\'', '"', ']');
        }

        return null;
    }

    private static string? TryExtractGalleryId(Uri url)
    {
        var pathMatch = GalleryIdRegex.Match(url.AbsolutePath);
        if (pathMatch.Success)
            return pathMatch.Groups[1].Value;

        var queryMatch = GidQueryRegex.Match(url.Query);
        return queryMatch.Success ? queryMatch.Groups[1].Value : null;
    }

    private static string? TryExtractPhotoId(Uri url) =>
        PhotoIdRegex.Match(url.AbsolutePath) is { Success: true } match
            ? match.Groups[1].Value
            : null;

    private static string BuildGalleryPageUrl(Uri pageUri, string galleryId, int page)
    {
        if (pageUri.AbsolutePath.Contains("/gallery/", StringComparison.OrdinalIgnoreCase))
            return $"https://www.imagefap.com/gallery/{galleryId}?gid={galleryId}&page={page}&view=0";

        var builder = new UriBuilder(pageUri)
        {
            Query = $"gid={galleryId}&page={page}&view=0",
        };
        return builder.Uri.ToString();
    }

    private static string FileNameForPhoto(string photoId, string imageUrl)
    {
        try
        {
            var ext = Path.GetExtension(new Uri(imageUrl).AbsolutePath);
            return $"imagefap-{photoId}{(string.IsNullOrWhiteSpace(ext) ? ".jpg" : ext)}";
        }
        catch
        {
            return $"imagefap-{photoId}.jpg";
        }
    }
}

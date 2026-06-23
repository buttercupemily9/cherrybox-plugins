using System.Text.RegularExpressions;
using CherryBox.Plugins.Abstractions;
using HtmlAgilityPack;

namespace ImageDownload.Plugin.Common;

internal static class SexComHelper
{
    private static readonly Regex MediaImageRegex = new(
        @"https://imagex\d+\.sx\.cdn\.live/images/[^""'\s<>]+?\.(?:jpe?g|png|gif|webp)(?:\?[^""'\s<>]*)?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex SxcCdnImageRegex = new(
        @"https://images\.sxccdn\.com/[^""'\s<>]+?\.(?:jpe?g|png|gif|webp)(?:\?[^""'\s<>]*)?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex GifIdRegex = new(@"/gifs/(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex GifMediaRegex = new(
        @"https://[^""'\s<>]+?\.(?:gif|webp)(?:\?[^""'\s<>]*)?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    internal static async Task<ImageDownloadPlan?> BuildPlanAsync(string url, CancellationToken cancellationToken)
    {
        var pageUri = SiteHostMatcher.NormalizeUrl(url);
        if (IsGifPage(pageUri))
            return await BuildGifPlanAsync(pageUri, cancellationToken);

        var html = await ImageFetchHelper.GetStringAsync(pageUri.ToString(), cancellationToken);

        if (LooksLikeVideoPage(pageUri, html))
            return YtDlpPlanHelper.BuildYtDlpPlan(pageUri.ToString(), "Sex.com");

        var imageUrls = CollectMediaImageUrls(html, pageUri);
        if (imageUrls.Count == 0)
            return null;

        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var title = CleanTitle(HtmlGalleryScraper.PageTitle(doc)) ?? "Sex.com";
        var description = HtmlGalleryScraper.MetaContent(doc, "og:description")
            ?? HtmlGalleryScraper.MetaContent(doc, "description");
        var tags = ExtractTags(doc, description);

        var items = imageUrls
            .Select((imageUrl, index) => new ImageDownloadItem(
                imageUrl,
                BuildFileName(imageUrl, index)))
            .ToList();

        return new ImageDownloadPlan(
            title,
            pageUri.ToString(),
            "Sex.com",
            ImageDownloadExecutionMode.DirectHttp,
            items,
            description,
            Tags: tags);
    }

    private static bool IsGifPage(Uri pageUri) => GifIdRegex.IsMatch(pageUri.AbsolutePath);

    private static async Task<ImageDownloadPlan?> BuildGifPlanAsync(Uri pageUri, CancellationToken cancellationToken)
    {
        var html = await ImageFetchHelper.GetStringAsync(
            pageUri.ToString(),
            cancellationToken,
            referer: "https://www.sex.com/");

        var imageUrls = CollectGifMediaUrls(html, pageUri);
        if (imageUrls.Count == 0)
            return null;

        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var title = CleanTitle(HtmlGalleryScraper.PageTitle(doc)) ?? "Sex.com gif";
        var description = HtmlGalleryScraper.MetaContent(doc, "og:description")
            ?? HtmlGalleryScraper.MetaContent(doc, "description");
        var tags = ExtractTags(doc, description);

        var items = imageUrls
            .Select((imageUrl, index) => new ImageDownloadItem(
                imageUrl,
                BuildGifFileName(imageUrl, index)))
            .ToList();

        return new ImageDownloadPlan(
            title,
            pageUri.ToString(),
            "Sex.com",
            ImageDownloadExecutionMode.DirectHttp,
            items,
            description,
            Tags: tags);
    }

    private static List<string> CollectGifMediaUrls(string html, Uri pageUri)
    {
        var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in MediaImageRegex.Matches(html))
            TryAddGifUrl(urls, match.Value, pageUri);

        foreach (Match match in SxcCdnImageRegex.Matches(html))
            TryAddGifUrl(urls, match.Value, pageUri);

        foreach (Match match in GifMediaRegex.Matches(html))
            TryAddGifUrl(urls, match.Value, pageUri);

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        foreach (var node in doc.DocumentNode.SelectNodes("//link[@rel='preload' and (@as='image' or @as='video')]") ?? Enumerable.Empty<HtmlNode>())
            TryAddGifUrl(urls, node.GetAttributeValue("href", null), pageUri);

        foreach (var node in doc.DocumentNode.SelectNodes("//source[@src]|//video[@src]") ?? Enumerable.Empty<HtmlNode>())
            TryAddGifUrl(urls, node.GetAttributeValue("src", null), pageUri);

        var ogImage = HtmlGalleryScraper.MetaContent(doc, "og:image");
        if (!string.IsNullOrWhiteSpace(ogImage))
            TryAddGifUrl(urls, ogImage, pageUri);

        return urls
            .OrderBy(u => u, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void TryAddGifUrl(ISet<string> urls, string? candidate, Uri pageUri)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return;

        if (!IsGifMediaUrl(candidate))
            return;

        if (!HtmlGalleryScraper.TryNormalizeImageUrl(candidate, pageUri, out var absolute))
            return;

        if (IsUiAsset(absolute))
            return;

        urls.Add(StripResizeQuery(absolute));
    }

    private static bool IsGifMediaUrl(string url)
    {
        if (IsMediaImageUrl(url))
            return true;

        return url.Contains(".gif", StringComparison.OrdinalIgnoreCase) ||
               url.Contains(".webp", StringComparison.OrdinalIgnoreCase) ||
               (url.Contains("sx.cdn.live/", StringComparison.OrdinalIgnoreCase) &&
                url.Contains("/gifs/", StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildGifFileName(string imageUrl, int index)
    {
        var fileName = BuildFileName(imageUrl, index);
        if (fileName.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
            return Path.ChangeExtension(fileName, ".gif");

        if (!fileName.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) &&
            !Path.HasExtension(fileName))
            return $"gif-{index + 1}.gif";

        return fileName;
    }

    private static bool LooksLikeVideoPage(Uri pageUri, string html)
    {
        if (IsGifPage(pageUri))
            return false;

        if (pageUri.AbsolutePath.Contains("/videos/", StringComparison.OrdinalIgnoreCase) ||
            pageUri.AbsolutePath.Contains("/video/", StringComparison.OrdinalIgnoreCase))
            return true;

        return html.Contains("/videos/", StringComparison.OrdinalIgnoreCase) &&
               html.Contains("images.sxccdn.com/videos/", StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> CollectMediaImageUrls(string html, Uri pageUri)
    {
        var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in MediaImageRegex.Matches(html))
            TryAddMediaUrl(urls, match.Value, pageUri);

        foreach (Match match in SxcCdnImageRegex.Matches(html))
            TryAddMediaUrl(urls, match.Value, pageUri);

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        foreach (var node in doc.DocumentNode.SelectNodes("//link[@rel='preload' and @as='image']") ?? Enumerable.Empty<HtmlNode>())
            TryAddMediaUrl(urls, node.GetAttributeValue("href", null), pageUri);

        foreach (var node in doc.DocumentNode.SelectNodes("//img[@src or @data-src]") ?? Enumerable.Empty<HtmlNode>())
        {
            var candidate = node.GetAttributeValue("src", null) ?? node.GetAttributeValue("data-src", null);
            TryAddMediaUrl(urls, candidate, pageUri);
        }

        var ogImage = HtmlGalleryScraper.MetaContent(doc, "og:image");
        if (!string.IsNullOrWhiteSpace(ogImage))
            TryAddMediaUrl(urls, ogImage, pageUri);

        return urls
            .OrderBy(u => u, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void TryAddMediaUrl(ISet<string> urls, string? candidate, Uri pageUri)
    {
        if (string.IsNullOrWhiteSpace(candidate) || !IsMediaImageUrl(candidate))
            return;

        if (!HtmlGalleryScraper.TryNormalizeImageUrl(candidate, pageUri, out var absolute))
            return;

        if (IsUiAsset(absolute))
            return;

        urls.Add(StripResizeQuery(absolute));
    }

    private static bool IsMediaImageUrl(string url) =>
        url.Contains("sx.cdn.live/images/", StringComparison.OrdinalIgnoreCase) ||
        url.Contains("images.sxccdn.com/images/", StringComparison.OrdinalIgnoreCase) ||
        url.Contains("images.sxccdn.com/pins/", StringComparison.OrdinalIgnoreCase);

    private static bool IsUiAsset(string url) =>
        url.Contains("staticp.sxccdn.com", StringComparison.OrdinalIgnoreCase) ||
        url.Contains("/static/media/", StringComparison.OrdinalIgnoreCase) ||
        url.Contains("favicon", StringComparison.OrdinalIgnoreCase);

    private static string StripResizeQuery(string url)
    {
        var queryIndex = url.IndexOf('?', StringComparison.Ordinal);
        return queryIndex >= 0 ? url[..queryIndex] : url;
    }

    private static string BuildFileName(string imageUrl, int index)
    {
        try
        {
            var fileName = Path.GetFileName(new Uri(imageUrl).AbsolutePath);
            if (!string.IsNullOrWhiteSpace(fileName))
                return fileName;
        }
        catch
        {
            /* fall through */
        }

        return $"image-{index + 1}.jpg";
    }

    private static string? CleanTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return null;

        title = title.Trim();
        var pipe = title.LastIndexOf('|');
        if (pipe >= 0)
            title = title[..pipe].Trim();

        return string.IsNullOrWhiteSpace(title) ? null : title;
    }

    private static IReadOnlyList<string>? ExtractTags(HtmlDocument doc, string? description)
    {
        var keywords = HtmlGalleryScraper.MetaContent(doc, "keywords")
            ?? doc.DocumentNode.SelectSingleNode("//meta[@itemprop='keywords']")
                ?.GetAttributeValue("content", null);

        if (!string.IsNullOrWhiteSpace(keywords))
        {
            var tags = keywords
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .ToList();
            if (tags.Count > 0)
                return tags;
        }

        if (string.IsNullOrWhiteSpace(description))
            return null;

        const string prefix = "Watch:";
        const string withMarker = " with ";
        if (!description.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;

        var remainder = description[prefix.Length..].Trim();
        var withIndex = remainder.LastIndexOf(withMarker, StringComparison.OrdinalIgnoreCase);
        if (withIndex < 0)
            return null;

        var tagPart = remainder[(withIndex + withMarker.Length)..].Trim();
        var parsed = tagPart
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();
        return parsed.Count > 0 ? parsed : null;
    }
}

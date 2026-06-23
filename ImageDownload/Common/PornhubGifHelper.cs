using System.Text.RegularExpressions;
using CherryBox.Plugins.Abstractions;
using HtmlAgilityPack;

namespace ImageDownload.Plugin.Common;

internal static class PornhubGifHelper
{
    private static readonly Regex GifIdRegex = new(@"/gif/(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ContentUrlRegex = new(@"""contentUrl""\s*:\s*""([^""]+)""", RegexOptions.Compiled);
    private static readonly Regex NameRegex = new(@"""name""\s*:\s*""((?:\\.|[^""\\])*)""", RegexOptions.Compiled);

    internal static bool IsGifUrl(Uri url) =>
        SiteHostMatcher.HostEndsWith(url, "pornhub.com") &&
        GifIdRegex.IsMatch(url.AbsolutePath);

    internal static async Task<ImageDownloadPlan?> BuildPlanAsync(string url, CancellationToken cancellationToken)
    {
        var pageUri = SiteHostMatcher.NormalizeUrl(url);
        if (!GifIdRegex.IsMatch(pageUri.AbsolutePath))
            return null;

        var gifId = GifIdRegex.Match(pageUri.AbsolutePath).Groups[1].Value;
        var html = await ImageFetchHelper.GetPornhubPageAsync(pageUri.ToString(), cancellationToken);

        var contentUrl = UnescapeJson(ContentUrlRegex.Match(html).Groups[1].Value);
        if (string.IsNullOrWhiteSpace(contentUrl))
            return YtDlpPlanHelper.BuildYtDlpPlan(pageUri.ToString(), "Pornhub");

        if (!HtmlGalleryScraper.TryNormalizeImageUrl(contentUrl, pageUri, out var absoluteUrl))
            return YtDlpPlanHelper.BuildYtDlpPlan(pageUri.ToString(), "Pornhub");

        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var title = UnescapeJson(NameRegex.Match(html).Groups[1].Value)
            ?? HtmlGalleryScraper.PageTitle(doc)
            ?? $"Pornhub gif {gifId}";
        var description = HtmlGalleryScraper.MetaContent(doc, "og:description")
            ?? HtmlGalleryScraper.MetaContent(doc, "description");

        var fileName = Path.GetFileName(new Uri(absoluteUrl).AbsolutePath);
        if (string.IsNullOrWhiteSpace(fileName))
            fileName = $"gif-{gifId}.gif";

        return new ImageDownloadPlan(
            title,
            pageUri.ToString(),
            "Pornhub",
            ImageDownloadExecutionMode.DirectHttp,
            [new ImageDownloadItem(absoluteUrl, fileName)],
            description,
            Tags: null);
    }

    private static string? UnescapeJson(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value
            .Replace("\\\"", "\"", StringComparison.Ordinal)
            .Replace("\\/", "/", StringComparison.Ordinal)
            .Replace("\\\\", "\\", StringComparison.Ordinal)
            .Trim();
    }
}

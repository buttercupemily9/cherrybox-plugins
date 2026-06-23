using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CherryBox.Plugins.Abstractions;
using HtmlAgilityPack;

namespace ImageDownload.Plugin.Common;

internal static class SiteHostMatcher
{
    internal static bool HostEndsWith(Uri url, params string[] hosts) =>
        hosts.Any(h => url.Host.EndsWith(h, StringComparison.OrdinalIgnoreCase));

    internal static Uri NormalizeUrl(string url) =>
        Uri.TryCreate(url.Contains("://", StringComparison.Ordinal) ? url : "https://" + url, UriKind.Absolute, out var uri)
            ? uri
            : throw new InvalidOperationException("Invalid URL.");
}

internal static class ImageFetchHelper
{
    private static readonly HttpClient Http = CreateClient();

    internal static Task<string> GetStringAsync(string url, CancellationToken cancellationToken) =>
        Http.GetStringAsync(url, cancellationToken);

    internal static async Task<byte[]> GetBytesAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
        return client;
    }
}

internal static class HtmlGalleryScraper
{
    internal static string? MetaContent(HtmlDocument doc, string property) =>
        doc.DocumentNode
            .SelectSingleNode($"//meta[@property='{property}']|//meta[@name='{property}']")
            ?.GetAttributeValue("content", null)
            ?.Trim();

    internal static string? PageTitle(HtmlDocument doc) =>
        doc.DocumentNode.SelectSingleNode("//title")?.InnerText?.Trim()
        ?? MetaContent(doc, "og:title");

    internal static IReadOnlyList<string> CollectImageUrls(
        HtmlDocument doc,
        Uri pageUri,
        params string[] selectors)
    {
        var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var selector in selectors)
        {
            foreach (var node in doc.DocumentNode.SelectNodes(selector) ?? Enumerable.Empty<HtmlNode>())
            {
                var candidate = node.GetAttributeValue("href", null)
                    ?? node.GetAttributeValue("src", null)
                    ?? node.GetAttributeValue("data-src", null)
                    ?? node.GetAttributeValue("data-full", null);
                if (string.IsNullOrWhiteSpace(candidate))
                    continue;

                if (TryNormalizeImageUrl(candidate, pageUri, out var absolute))
                    urls.Add(absolute);
            }
        }

        var og = MetaContent(doc, "og:image");
        if (!string.IsNullOrWhiteSpace(og) && TryNormalizeImageUrl(og, pageUri, out var ogUrl))
            urls.Add(ogUrl);

        return urls
            .Where(u => !LooksLikeIcon(u))
            .OrderBy(u => u, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal static bool TryNormalizeImageUrl(string value, Uri pageUri, out string absolute)
    {
        absolute = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        value = value.Trim();
        if (value.StartsWith("//", StringComparison.Ordinal))
            value = "https:" + value;

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            if (!Uri.TryCreate(pageUri, value, out uri))
                return false;
        }

        if (uri.Scheme is not ("http" or "https"))
            return false;

        absolute = uri.ToString();
        return true;
    }

    private static bool LooksLikeIcon(string url) =>
        url.Contains("favicon", StringComparison.OrdinalIgnoreCase) ||
        url.Contains("sprite", StringComparison.OrdinalIgnoreCase) ||
        url.Contains("/ads/", StringComparison.OrdinalIgnoreCase);
}

internal static class YtDlpPlanHelper
{
    internal static ImageDownloadPlan BuildYtDlpPlan(string url, string siteName, string? title = null) =>
        new(
            string.IsNullOrWhiteSpace(title) ? siteName : title,
            url,
            siteName,
            ImageDownloadExecutionMode.YtDlp,
            [new ImageDownloadItem(url)],
            Description: null,
            Tags: null);

    internal static async Task<ImageDownloadPlan?> TryBuildFromJsonAsync(
        string url,
        string siteName,
        string ytDlpPath,
        CancellationToken cancellationToken)
    {
        var json = await RunYtDlpJsonAsync(ytDlpPath, url, cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
            return null;

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var title = root.TryGetProperty("title", out var titleEl) ? titleEl.GetString() : null;
        var entries = new List<ImageDownloadItem>();

        if (root.TryGetProperty("entries", out var entriesEl) && entriesEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in entriesEl.EnumerateArray())
            {
                var entryUrl = entry.TryGetProperty("url", out var urlEl) ? urlEl.GetString() :
                    entry.TryGetProperty("webpage_url", out var webEl) ? webEl.GetString() : null;
                if (!string.IsNullOrWhiteSpace(entryUrl))
                    entries.Add(new ImageDownloadItem(entryUrl));
            }
        }

        if (entries.Count == 0)
        {
            var direct = root.TryGetProperty("url", out var directEl) ? directEl.GetString() : null;
            if (!string.IsNullOrWhiteSpace(direct))
                entries.Add(new ImageDownloadItem(direct));
        }

        if (entries.Count == 0)
            return BuildYtDlpPlan(url, siteName, title);

        return new ImageDownloadPlan(
            title ?? siteName,
            url,
            siteName,
            ImageDownloadExecutionMode.DirectHttp,
            entries,
            Description: root.TryGetProperty("description", out var desc) ? desc.GetString() : null,
            Tags: ExtractTags(root));
    }

    private static IReadOnlyList<string>? ExtractTags(JsonElement root)
    {
        if (!root.TryGetProperty("tags", out var tags) || tags.ValueKind != JsonValueKind.Array)
            return null;

        var list = tags.EnumerateArray()
            .Select(t => t.ValueKind == JsonValueKind.String ? t.GetString() : null)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t!)
            .ToList();
        return list.Count > 0 ? list : null;
    }

    private static async Task<string?> RunYtDlpJsonAsync(string ytDlpPath, string url, CancellationToken cancellationToken)
    {
        using var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = ytDlpPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        process.StartInfo.ArgumentList.Add("--dump-single-json");
        process.StartInfo.ArgumentList.Add("--flat-playlist");
        process.StartInfo.ArgumentList.Add(url);
        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode == 0 ? stdout : null;
    }
}

internal static class Rule34ApiHelper
{
    private static readonly Regex PostIdRegex = new(@"[?&]id=(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    internal static async Task<ImageDownloadPlan?> TryBuildPostPlanAsync(
        string url,
        string siteName,
        CancellationToken cancellationToken)
    {
        var match = PostIdRegex.Match(url);
        if (!match.Success)
            return null;

        var id = match.Groups[1].Value;
        var apiHost = siteName.Contains("paheal", StringComparison.OrdinalIgnoreCase)
            ? "https://rule34.paheal.net"
            : "https://api.rule34.xxx";
        var apiUrl = $"{apiHost}/index.php?page=dapi&s=post&q=index&id={id}";

        var xml = await ImageFetchHelper.GetStringAsync(apiUrl, cancellationToken);
        var doc = XDocument.Parse(xml);
        var post = doc.Root?.Elements().FirstOrDefault();
        if (post is null)
            return null;

        var fileUrl = post.Attribute("file_url")?.Value
            ?? post.Attribute("sample_url")?.Value;
        if (string.IsNullOrWhiteSpace(fileUrl))
            return null;

        var tags = post.Attribute("tags")?.Value?
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();

        return new ImageDownloadPlan(
            $"Rule34 post {id}",
            url,
            siteName,
            ImageDownloadExecutionMode.DirectHttp,
            [new ImageDownloadItem(fileUrl, $"post-{id}{GuessExt(fileUrl)}")],
            Description: post.Attribute("tags")?.Value,
            Tags: tags);
    }

    private static string GuessExt(string fileUrl)
    {
        try
        {
            var ext = Path.GetExtension(new Uri(fileUrl).AbsolutePath);
            return string.IsNullOrWhiteSpace(ext) ? ".jpg" : ext;
        }
        catch
        {
            return ".jpg";
        }
    }
}

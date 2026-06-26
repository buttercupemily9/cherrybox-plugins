using System.Text.Json;
using System.Text.RegularExpressions;
using CherryBox.Plugins.Abstractions;
using HtmlAgilityPack;

namespace StoryImportCommon;

public static partial class HtmlStoryExtractor
{
    [GeneratedRegex(@"\b(next|continue|read\s+next|chapter\s+\d+)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex NextLinkTextPattern();

    public static string ExtractText(HtmlDocument doc, params string[] selectors)
    {
        foreach (var selector in selectors)
        {
            var nodes = doc.DocumentNode.SelectNodes(selector);
            if (nodes is null || nodes.Count == 0)
                continue;

            var text = string.Join(
                "\n\n",
                nodes.Select(n => HtmlEntity.DeEntitize(n.InnerText)).Where(t => !string.IsNullOrWhiteSpace(t)));
            if (!string.IsNullOrWhiteSpace(text))
                return text.Trim();
        }

        return HtmlEntity.DeEntitize(doc.DocumentNode.SelectSingleNode("//body")?.InnerText ?? string.Empty).Trim();
    }

    public static string? ExtractMeta(HtmlDocument doc, string name)
    {
        var node = doc.DocumentNode.SelectSingleNode($"//meta[@name='{name}']")
            ?? doc.DocumentNode.SelectSingleNode($"//meta[@property='{name}']");
        return node?.GetAttributeValue("content", null)?.Trim();
    }

    public static Uri? FindNextPage(HtmlDocument doc, Uri current, params string[] extraSelectors)
    {
        var candidates = new List<HtmlNode>();

        candidates.AddRange(doc.DocumentNode.SelectNodes("//a[@rel='next']") ?? Enumerable.Empty<HtmlNode>());
        candidates.AddRange(doc.DocumentNode.SelectNodes("//a[contains(translate(@class,'NEXT','next'),'next')]") ?? Enumerable.Empty<HtmlNode>());

        foreach (var selector in extraSelectors)
            candidates.AddRange(doc.DocumentNode.SelectNodes(selector) ?? Enumerable.Empty<HtmlNode>());

        foreach (var anchor in doc.DocumentNode.SelectNodes("//a[@href]") ?? Enumerable.Empty<HtmlNode>())
        {
            var text = HtmlEntity.DeEntitize(anchor.InnerText).Trim();
            if (NextLinkTextPattern().IsMatch(text))
                candidates.Add(anchor);
        }

        foreach (var anchor in candidates.Distinct())
        {
            var href = anchor.GetAttributeValue("href", string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(href) || href.StartsWith('#'))
                continue;

            if (Uri.TryCreate(current, href, out var next) && !string.Equals(next.ToString(), current.ToString(), StringComparison.OrdinalIgnoreCase))
                return next;
        }

        return null;
    }

    public static bool HostMatches(Uri url, params string[] hosts) =>
        hosts.Any(h => url.Host.Equals(h, StringComparison.OrdinalIgnoreCase) ||
                       url.Host.EndsWith("." + h, StringComparison.OrdinalIgnoreCase));

    public static bool LooksLikeLoginWall(string html) =>
        html.Contains("name=\"password\"", StringComparison.OrdinalIgnoreCase)
        && html.Contains("Log In", StringComparison.OrdinalIgnoreCase)
        && html.Contains("login.php", StringComparison.OrdinalIgnoreCase);
}

public abstract class HtmlStorySiteImporterBase : IStorySiteImporter
{
    public abstract string SiteId { get; }
    public abstract string SiteName { get; }
    public abstract Uri SiteHome { get; }

    protected abstract bool MatchesHost(Uri url);
    protected abstract StoryImportPageResult ParsePage(HtmlDocument doc, Uri url);

    public bool CanImport(Uri url) => MatchesHost(url);

    public virtual bool SupportsSiteLogin => false;

    public virtual async Task<StoryImportPageResult> FetchPageAsync(StoryImportPageRequest request, CancellationToken cancellationToken = default)
    {
        var html = await request.Http.GetStringAsync(request.Url, cancellationToken);
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        return ParsePage(doc, request.Url);
    }
}

public static class RedditStoryImporterHelper
{
    public static async Task<StoryImportPageResult> FetchAsync(StoryImportPageRequest request, CancellationToken cancellationToken)
    {
        var jsonUrl = BuildJsonUrl(request.Url);
        var json = await request.Http.GetStringAsync(jsonUrl, cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var post = doc.RootElement[0].GetProperty("data").GetProperty("children")[0].GetProperty("data");

        var title = post.GetProperty("title").GetString() ?? "Reddit post";
        var author = post.TryGetProperty("author", out var authorProp) ? authorProp.GetString() : null;
        var selftext = post.TryGetProperty("selftext", out var textProp) ? textProp.GetString() ?? string.Empty : string.Empty;

        if (string.IsNullOrWhiteSpace(selftext) && post.TryGetProperty("url", out var urlProp))
        {
            var external = urlProp.GetString();
            if (!string.IsNullOrWhiteSpace(external) &&
                Uri.TryCreate(external, UriKind.Absolute, out var externalUri) &&
                !externalUri.Host.Contains("reddit.com", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("This Reddit post links to external content. Open the linked story URL instead.");
            }
        }

        return new StoryImportPageResult(title, author, selftext, null);
    }

    private static Uri BuildJsonUrl(Uri url)
    {
        var path = url.AbsolutePath.TrimEnd('/');
        if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            path += ".json";
        return new UriBuilder(url) { Path = path, Query = string.Empty, Fragment = string.Empty }.Uri;
    }
}

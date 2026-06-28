using CherryBox.Plugins.Abstractions;
using HtmlAgilityPack;
using StoryImportCommon;

namespace StorySites.Plugin.Importers;

public sealed class RedditStoryImporter : IStorySiteImporter
{
    public string SiteId => "reddit";
    public string SiteName => "Reddit";
    public Uri SiteHome => new("https://www.reddit.com/");
    public bool CanImport(Uri url) => HtmlStoryExtractor.HostMatches(url, "reddit.com");
    public bool SupportsSiteLogin => false;
    public Task<StoryImportPageResult> FetchPageAsync(StoryImportPageRequest request, CancellationToken cancellationToken = default) =>
        RedditStoryImporterHelper.FetchAsync(request, cancellationToken);
}

public sealed class LiteroticaStoryImporter : HtmlStorySiteImporterBase
{
    public override string SiteId => "literotica";
    public override string SiteName => "Literotica";
    public override Uri SiteHome => new("https://www.literotica.com/stories");
    protected override bool MatchesHost(Uri url) => HtmlStoryExtractor.HostMatches(url, "literotica.com");
    protected override StoryImportPageResult ParsePage(HtmlDocument doc, Uri url)
    {
        var title = doc.DocumentNode.SelectSingleNode("//h1")?.InnerText.Trim() ?? "Literotica story";
        var author = doc.DocumentNode.SelectSingleNode("//a[contains(@class,'b-user-info')]")?.InnerText.Trim()
            ?? HtmlStoryExtractor.ExtractMeta(doc, "author");
        var text = HtmlStoryExtractor.ExtractText(doc, "//div[contains(@class,'article')]", "//div[@class='aa_ht']", "//div[contains(@class,'panel')]");
        var next = HtmlStoryExtractor.FindNextPage(doc, url, "//a[contains(@class,'b-pager-next')]");
        return new StoryImportPageResult(title, author, text, next);
    }
}

public sealed class LushStoriesImporter : HtmlStorySiteImporterBase
{
    public override string SiteId => "lushstories";
    public override string SiteName => "LushStories";
    public override Uri SiteHome => new("https://www.lushstories.com/");
    protected override bool MatchesHost(Uri url) => HtmlStoryExtractor.HostMatches(url, "lushstories.com");
    protected override StoryImportPageResult ParsePage(HtmlDocument doc, Uri url)
    {
        var title = doc.DocumentNode.SelectSingleNode("//h1")?.InnerText.Trim() ?? "LushStories story";
        var author = doc.DocumentNode.SelectSingleNode("//a[contains(@href,'/members/')]")?.InnerText.Trim();
        var text = HtmlStoryExtractor.ExtractText(doc, "//div[contains(@class,'story-content')]", "//div[@id='story']", "//article");
        var next = HtmlStoryExtractor.FindNextPage(doc, url, "//a[contains(@class,'next')]");
        return new StoryImportPageResult(title, author, text, next);
    }
}

public sealed class SexStoriesImporter : HtmlStorySiteImporterBase
{
    public override string SiteId => "sexstories";
    public override string SiteName => "SexStories.com";
    public override Uri SiteHome => new("https://www.sexstories.com/");
    protected override bool MatchesHost(Uri url) => HtmlStoryExtractor.HostMatches(url, "sexstories.com");
    protected override StoryImportPageResult ParsePage(HtmlDocument doc, Uri url)
    {
        var headerInfo = doc.DocumentNode.SelectSingleNode("//div[@id='top_panel']//div[contains(@class,'story_info')]")
            ?? doc.DocumentNode.SelectSingleNode("//div[@id='story_center_panel']//div[contains(@class,'story_info')]");
        var heading = headerInfo?.SelectSingleNode(".//h2");
        string title;
        string? author = null;

        if (heading is not null)
        {
            author = heading.SelectSingleNode(".//span[contains(@class,'title_link')]//a[contains(@href,'/profile')]")
                ?.InnerText.Trim();
            heading.SelectSingleNode(".//span[contains(@class,'title_link')]")?.Remove();
            title = HtmlEntity.DeEntitize(heading.InnerText).Trim();
            if (string.IsNullOrWhiteSpace(title) || IsSiteChromeTitle(title))
                title = "SexStories story";
        }
        else
        {
            title = "SexStories story";
            author = doc.DocumentNode
                .SelectSingleNode("//div[@id='top_panel']//a[contains(@href,'/profile')]")
                ?.InnerText.Trim();
        }

        if (IsSiteChromeTitle(title))
        {
            var fallbackHeading = headerInfo?.SelectSingleNode(".//h2")
                ?? doc.DocumentNode.SelectSingleNode("//div[contains(@class,'story_info')]//h2");
            if (fallbackHeading is not null)
            {
                author ??= fallbackHeading.SelectSingleNode(".//span[contains(@class,'title_link')]//a[contains(@href,'/profile')]")
                    ?.InnerText.Trim();
                fallbackHeading.SelectSingleNode(".//span[contains(@class,'title_link')]")?.Remove();
                title = HtmlEntity.DeEntitize(fallbackHeading.InnerText).Trim();
            }
        }

        if (string.IsNullOrWhiteSpace(title) || IsSiteChromeTitle(title))
            title = "SexStories story";

        var text = SexStoriesContentExtractor.ExtractStoryText(doc);
        if (string.IsNullOrWhiteSpace(text))
        {
            text = HtmlStoryExtractor.ExtractTextFromFirstOrEmpty(
                doc,
                "//div[@id='story_center_panel']/div[contains(@class,'block_panel')][last()-1]",
                "//div[@id='story_center_panel']//div[contains(@class,'block_panel')][not(.//*[@id='comments'])][last()]");
        }

        var next = HtmlStoryExtractor.FindNextPage(doc, url);
        return new StoryImportPageResult(title, author, text, next);
    }

    private static bool IsSiteChromeTitle(string title) =>
        title.Equals("sexstories.com", StringComparison.OrdinalIgnoreCase)
        || title.Equals("SexStories.com", StringComparison.OrdinalIgnoreCase)
        || title.Equals("XNXX Stories", StringComparison.OrdinalIgnoreCase)
        || title.Equals("Free Sex Stories & Erotic Stories @ XNXX.COM", StringComparison.OrdinalIgnoreCase);
}

public sealed class SexStories69Importer : HtmlStorySiteImporterBase
{
    public override string SiteId => "sexstories69";
    public override string SiteName => "SexStories69";
    public override Uri SiteHome => new("https://sexstories69.com/");
    protected override bool MatchesHost(Uri url) => HtmlStoryExtractor.HostMatches(url, "sexstories69.com");
    protected override StoryImportPageResult ParsePage(HtmlDocument doc, Uri url)
    {
        var title = doc.DocumentNode.SelectSingleNode("//h1")?.InnerText.Trim() ?? "SexStories69 story";
        var author = doc.DocumentNode.SelectSingleNode("//meta[@name='author']")?.GetAttributeValue("content", null)
            ?? doc.DocumentNode.SelectSingleNode("//span[contains(@class,'author')]")?.InnerText.Trim();
        var text = HtmlStoryExtractor.ExtractText(doc, "//article", "//div[contains(@class,'story')]", "//div[contains(@class,'content')]");
        var next = HtmlStoryExtractor.FindNextPage(doc, url);
        return new StoryImportPageResult(title, author, text, next);
    }
}

public sealed class StoriesOnlineImporter : HtmlStorySiteImporterBase
{
    public override bool SupportsSiteLogin => true;
    public override string SiteId => "storiesonline";
    public override string SiteName => "StoriesOnline";
    public override Uri SiteHome => new("https://storiesonline.net/");
    protected override bool MatchesHost(Uri url) => HtmlStoryExtractor.HostMatches(url, "storiesonline.net");

    public override async Task<StoryImportPageResult> FetchPageAsync(
        StoryImportPageRequest request,
        CancellationToken cancellationToken = default)
    {
        var html = await request.Http.GetStringAsync(request.Url, cancellationToken);
        if (HtmlStoryExtractor.LooksLikeLoginWall(html))
        {
            throw new InvalidOperationException(
                "This StoriesOnline story requires a member login. Add your StoriesOnline email and password under Settings → Site logins.");
        }

        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        return ParsePage(doc, request.Url);
    }

    protected override StoryImportPageResult ParsePage(HtmlDocument doc, Uri url)
    {
        var title = doc.DocumentNode.SelectSingleNode("//h1")?.InnerText.Trim()
            ?? doc.DocumentNode.SelectSingleNode("//title")?.InnerText.Trim()
            ?? "StoriesOnline story";
        var author = doc.DocumentNode.SelectSingleNode("//a[contains(@href,'/a/')]")?.InnerText.Trim();
        var text = HtmlStoryExtractor.ExtractText(doc, "//div[@id='stdArticle']", "//div[contains(@class,'story')]", "//article");
        var next = HtmlStoryExtractor.FindNextPage(doc, url, "//a[contains(@href,'page=')]");
        return new StoryImportPageResult(title, author, text, next);
    }
}

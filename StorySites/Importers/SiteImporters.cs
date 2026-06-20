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
        var title = doc.DocumentNode.SelectSingleNode("//h1")?.InnerText.Trim() ?? "SexStories story";
        var author = doc.DocumentNode.SelectSingleNode("//a[contains(@href,'/author/')]")?.InnerText.Trim();
        var text = HtmlStoryExtractor.ExtractText(doc, "//div[@id='story']", "//div[contains(@class,'storytext')]", "//article");
        var next = HtmlStoryExtractor.FindNextPage(doc, url);
        return new StoryImportPageResult(title, author, text, next);
    }
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
    public override string SiteId => "storiesonline";
    public override string SiteName => "StoriesOnline";
    public override Uri SiteHome => new("https://storiesonline.net/");
    protected override bool MatchesHost(Uri url) => HtmlStoryExtractor.HostMatches(url, "storiesonline.net");
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

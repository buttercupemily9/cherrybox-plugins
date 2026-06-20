namespace CherryBox.Plugins.Abstractions;

public interface IStorySiteImporter
{
    string SiteId { get; }
    string SiteName { get; }
    Uri SiteHome { get; }
    bool CanImport(Uri url);
    Task<StoryImportPageResult> FetchPageAsync(StoryImportPageRequest request, CancellationToken cancellationToken = default);
}

public interface IStorySiteImporterRegistry
{
    void Register(IStorySiteImporter importer);
    void Clear();
    IReadOnlyList<StorySiteInfo> ListSites();
    IStorySiteImporter? FindImporter(Uri url);
    IStorySiteImporter? FindBySiteId(string siteId);
}

public sealed record StorySiteInfo(string SiteId, string SiteName, string SiteHome, bool Available);

public sealed record StoryImportPageRequest(Uri Url, IStoryImportHttpClient Http);

public sealed record StoryImportPageResult(
    string Title,
    string? Author,
    string Text,
    Uri? NextPageUrl,
    IReadOnlyDictionary<string, string>? Metadata = null);

public interface IStoryImportHttpClient
{
    Task<string> GetStringAsync(Uri url, CancellationToken cancellationToken = default);
    Task DelayBetweenPagesAsync(CancellationToken cancellationToken = default);
}

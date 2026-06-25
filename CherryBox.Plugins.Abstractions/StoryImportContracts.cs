namespace CherryBox.Plugins.Abstractions;

public interface IStorySiteImporter
{
    string SiteId { get; }
    string SiteName { get; }
    Uri SiteHome { get; }
    bool SupportsSiteLogin => false;
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

public sealed record StorySiteInfo(string SiteId, string SiteName, string SiteHome, bool Available, bool SupportsSiteLogin = false);

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

public sealed record StorySiteAuthDto(
    string SiteKey,
    string AuthMode,
    string? Username,
    bool HasPassword,
    bool HasCookiesFile,
    string? TestUrl);

public sealed record UpsertStorySiteAuthRequest(
    string SiteKey,
    string AuthMode,
    string? Username,
    string? Password,
    string? TestUrl);

public sealed record TestStorySiteAuthRequest(
    string SiteKey,
    string? TestUrl,
    string? AuthMode,
    string? Username,
    string? Password);

public sealed record TestStorySiteAuthResult(
    bool Success,
    string Message);

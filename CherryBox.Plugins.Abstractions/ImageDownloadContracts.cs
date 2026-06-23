namespace CherryBox.Plugins.Abstractions;

public enum ImageDownloadExecutionMode
{
    DirectHttp = 0,
    YtDlp = 1,
}

public sealed record ImageDownloadItem(string Url, string? FileName = null);

public sealed record ImageDownloadPlan(
    string Title,
    string SourceUrl,
    string SourceSite,
    ImageDownloadExecutionMode Mode,
    IReadOnlyList<ImageDownloadItem> Images,
    string? Description = null,
    IReadOnlyList<string>? Tags = null);

public interface IImageDownloadHandler
{
    string SiteId { get; }
    string SiteName { get; }
    bool CanHandle(Uri url);
    Task<ImageDownloadPlan?> BuildPlanAsync(string url, CancellationToken cancellationToken = default);
}

public interface IImageDownloadHandlerRegistry
{
    void Register(IImageDownloadHandler handler);
    void Clear();
    IReadOnlyList<(string SiteId, string SiteName)> ListHandlers();
    IImageDownloadHandler? FindHandler(Uri url);
}

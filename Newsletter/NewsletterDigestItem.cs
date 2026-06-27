namespace CherryBox.Newsletter.Plugin;

public sealed record NewsletterDigestItem(
    string Title,
    string MediaType,
    string Url,
    DateTimeOffset UpdatedAt,
    string? Author = null,
    string? Studio = null,
    string? Performers = null,
    string? Tags = null,
    string? Description = null,
    string? Duration = null,
    string? SourceSite = null,
    string? EmbeddedImageContentId = null);

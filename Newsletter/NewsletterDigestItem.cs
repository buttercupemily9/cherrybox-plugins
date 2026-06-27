namespace CherryBox.Newsletter.Plugin;

public enum NewsletterDigestSection
{
    NewThisWeek,
    Recommended
}

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
    string? EmbeddedImageContentId = null,
    NewsletterDigestSection Section = NewsletterDigestSection.NewThisWeek)
{
    public string ConsumptionVerb => MediaType switch
    {
        "Video" => "watch",
        "Story" => "read",
        "Audio" => "listen to",
        "Picture" => "look at",
        _ => "open"
    };
}

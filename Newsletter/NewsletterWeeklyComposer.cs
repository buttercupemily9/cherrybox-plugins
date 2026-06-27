using CherryBox.Core.Enums;
using CherryBox.Data;
using CherryBox.Plugins.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CherryBox.Newsletter.Plugin;

public static partial class NewsletterWeeklyComposer
{
    private const int DigestItemLimit = 12;

    internal static async Task<(IReadOnlyList<NewsletterDigestItem> Items, IReadOnlyList<EmailEmbeddedImage> EmbeddedImages)> LoadDigestItemsAsync(
        CherryBoxDbContext db,
        string baseUrl,
        DateTimeOffset since,
        CancellationToken cancellationToken)
    {
        // Plugin-hosted EF cannot translate DateTimeOffset filters on SQLite; compare ISO8601 text instead.
        var sinceIso = since.ToUniversalTime().ToString("O");
        var recentIds = await db.Database
            .SqlQuery<Guid>($"""
                SELECT "Id" FROM "MediaItems" WHERE "UpdatedAt" >= {sinceIso}
                UNION
                SELECT "Id" FROM "MediaItems" WHERE "CreatedAt" >= {sinceIso}
                """)
            .ToListAsync(cancellationToken);

        if (recentIds.Count == 0)
            return ([], []);

        var media = await db.MediaItems.AsNoTracking()
            .Include(m => m.Studio)
            .Where(m => recentIds.Contains(m.Id))
            .ToListAsync(cancellationToken);

        media = media
            .OrderByDescending(m => m.UpdatedAt)
            .Take(DigestItemLimit)
            .ToList();

        if (media.Count == 0)
            return ([], []);

        var mediaIds = media.Select(m => m.Id).ToList();

        var performerNames = await db.MediaItemPerformers.AsNoTracking()
            .Where(link => mediaIds.Contains(link.MediaItemId))
            .Join(db.Performers.AsNoTracking(), link => link.PerformerId, performer => performer.Id,
                (link, performer) => new { link.MediaItemId, performer.Name })
            .ToListAsync(cancellationToken);

        var tagNames = await db.MediaItemTags.AsNoTracking()
            .Where(link => mediaIds.Contains(link.MediaItemId))
            .Join(db.Tags.AsNoTracking(), link => link.TagId, tag => tag.Id,
                (link, tag) => new { link.MediaItemId, tag.Name })
            .ToListAsync(cancellationToken);

        var performersByMedia = performerNames
            .GroupBy(x => x.MediaItemId)
            .ToDictionary(g => g.Key, g => string.Join(", ", g.Select(x => x.Name).Distinct().OrderBy(n => n)));

        var tagsByMedia = tagNames
            .GroupBy(x => x.MediaItemId)
            .ToDictionary(g => g.Key, g => string.Join(", ", g.Select(x => x.Name).Distinct().OrderBy(n => n)));

        var embeddedImages = await NewsletterImageEmbedder.BuildEmbeddedImagesAsync(
            db,
            media.Select(m => (m.Id, m.MediaType)).ToList(),
            cancellationToken);

        var items = media.Select(m =>
        {
            var contentId = embeddedImages.Any(e => e.ContentId == NewsletterImageEmbedder.ContentIdFor(m.Id))
                ? NewsletterImageEmbedder.ContentIdFor(m.Id)
                : null;

            return new NewsletterDigestItem(
                string.IsNullOrWhiteSpace(m.Title) ? m.FileName : m.Title!,
                FormatMediaType(m.MediaType),
                BuildMediaUrl(baseUrl, m.Id, m.MediaType),
                m.UpdatedAt,
                string.IsNullOrWhiteSpace(m.Author) ? null : m.Author.Trim(),
                m.Studio?.Name,
                performersByMedia.GetValueOrDefault(m.Id),
                tagsByMedia.GetValueOrDefault(m.Id),
                TruncateDescription(m.Description),
                FormatDuration(m.DurationSeconds),
                string.IsNullOrWhiteSpace(m.SourceSite) ? null : m.SourceSite.Trim(),
                contentId);
        }).ToList();

        return (items, embeddedImages);
    }

    private static string BuildMediaUrl(string baseUrl, Guid id, MediaType mediaType)
    {
        var path = mediaType switch
        {
            MediaType.Video => $"/video/{id}",
            MediaType.Audio => $"/audio/{id}",
            MediaType.Story => $"/story/{id}",
            MediaType.Image => $"/picture/{id}",
            _ => $"/media/{id}"
        };
        return $"{baseUrl.TrimEnd('/')}{path}";
    }

    private static string FormatMediaType(MediaType mediaType) => mediaType switch
    {
        MediaType.Video => "Video",
        MediaType.Audio => "Audio",
        MediaType.Story => "Story",
        MediaType.Image => "Picture",
        _ => mediaType.ToString()
    };

    private static string? TruncateDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return null;

        var trimmed = description.Trim();
        return trimmed.Length <= 220 ? trimmed : trimmed[..217] + "...";
    }

    private static string? FormatDuration(double? seconds)
    {
        if (seconds is null or <= 0)
            return null;

        var total = (int)Math.Round(seconds.Value);
        var hours = total / 3600;
        var minutes = (total % 3600) / 60;
        var secs = total % 60;
        return hours > 0
            ? $"{hours}h {minutes}m"
            : minutes > 0
                ? $"{minutes}m {secs}s"
                : $"{secs}s";
    }
}

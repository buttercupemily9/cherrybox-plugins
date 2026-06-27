using CherryBox.Core.Entities;
using CherryBox.Core.Enums;
using CherryBox.Data;
using CherryBox.Plugins.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CherryBox.Newsletter.Plugin;

public static partial class NewsletterWeeklyComposer
{
    private const int NewItemLimit = 8;
    private const int RecommendedItemLimit = 5;
    private const int RecommendationCandidatePool = 40;

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

        var newMedia = await LoadNewMediaAsync(db, recentIds, cancellationToken);
        var excludeIds = recentIds.ToHashSet();
        foreach (var item in newMedia)
            excludeIds.Add(item.Id);

        var recommendedMedia = await LoadRecommendedMediaAsync(db, excludeIds, since, cancellationToken);
        var allMedia = newMedia.Concat(recommendedMedia).ToList();
        if (allMedia.Count == 0)
            return ([], []);

        var mediaIds = allMedia.Select(m => m.Id).ToList();
        var performersByMedia = await LoadPerformersByMediaAsync(db, mediaIds, cancellationToken);
        var tagsByMedia = await LoadTagsByMediaAsync(db, mediaIds, cancellationToken);

        var embeddedImages = await NewsletterImageEmbedder.BuildEmbeddedImagesAsync(
            db,
            allMedia.Select(m => (m.Id, m.MediaType)).ToList(),
            cancellationToken);

        var embeddedByMedia = embeddedImages
            .Select(e => e.ContentId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var newItems = MapMediaToDigestItems(
            newMedia,
            baseUrl,
            performersByMedia,
            tagsByMedia,
            embeddedByMedia,
            NewsletterDigestSection.NewThisWeek);

        var recommendedItems = MapMediaToDigestItems(
            recommendedMedia,
            baseUrl,
            performersByMedia,
            tagsByMedia,
            embeddedByMedia,
            NewsletterDigestSection.Recommended);

        return (newItems.Concat(recommendedItems).ToList(), embeddedImages);
    }

    private static async Task<List<MediaItem>> LoadNewMediaAsync(
        CherryBoxDbContext db,
        IReadOnlyList<Guid> recentIds,
        CancellationToken cancellationToken)
    {
        if (recentIds.Count == 0)
            return [];

        var media = await db.MediaItems.AsNoTracking()
            .Include(m => m.Studio)
            .Where(m => recentIds.Contains(m.Id))
            .ToListAsync(cancellationToken);

        return media
            .OrderByDescending(m => m.UpdatedAt)
            .Take(NewItemLimit)
            .ToList();
    }

    private static async Task<List<MediaItem>> LoadRecommendedMediaAsync(
        CherryBoxDbContext db,
        HashSet<Guid> excludeIds,
        DateTimeOffset since,
        CancellationToken cancellationToken)
    {
        var performerLinkedIds = await db.MediaItemPerformers.AsNoTracking()
            .Where(link => !excludeIds.Contains(link.MediaItemId))
            .Select(link => link.MediaItemId)
            .Distinct()
            .ToListAsync(cancellationToken);

        IQueryable<MediaItem> query = db.MediaItems.AsNoTracking().Include(m => m.Studio);
        if (performerLinkedIds.Count > 0)
        {
            query = query.Where(m => performerLinkedIds.Contains(m.Id) && !excludeIds.Contains(m.Id));
        }
        else
        {
            query = query.Where(m => !excludeIds.Contains(m.Id));
        }

        var candidates = await query
            .OrderByDescending(m => m.ViewCount + m.PlayCount)
            .ThenByDescending(m => m.UpdatedAt)
            .Take(RecommendationCandidatePool)
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
            return [];

        var seed = HashCode.Combine(since.UtcDateTime.Date, RecommendedItemLimit);
        return candidates
            .OrderBy(m => HashCode.Combine(seed, m.Id.GetHashCode()))
            .Take(RecommendedItemLimit)
            .ToList();
    }

    private static async Task<Dictionary<Guid, string>> LoadPerformersByMediaAsync(
        CherryBoxDbContext db,
        IReadOnlyList<Guid> mediaIds,
        CancellationToken cancellationToken)
    {
        var performerNames = await db.MediaItemPerformers.AsNoTracking()
            .Where(link => mediaIds.Contains(link.MediaItemId))
            .Join(db.Performers.AsNoTracking(), link => link.PerformerId, performer => performer.Id,
                (link, performer) => new { link.MediaItemId, performer.Name })
            .ToListAsync(cancellationToken);

        return performerNames
            .GroupBy(x => x.MediaItemId)
            .ToDictionary(g => g.Key, g => string.Join(", ", g.Select(x => x.Name).Distinct().OrderBy(n => n)));
    }

    private static async Task<Dictionary<Guid, string>> LoadTagsByMediaAsync(
        CherryBoxDbContext db,
        IReadOnlyList<Guid> mediaIds,
        CancellationToken cancellationToken)
    {
        var tagNames = await db.MediaItemTags.AsNoTracking()
            .Where(link => mediaIds.Contains(link.MediaItemId))
            .Join(db.Tags.AsNoTracking(), link => link.TagId, tag => tag.Id,
                (link, tag) => new { link.MediaItemId, tag.Name })
            .ToListAsync(cancellationToken);

        return tagNames
            .GroupBy(x => x.MediaItemId)
            .ToDictionary(g => g.Key, g => string.Join(", ", g.Select(x => x.Name).Distinct().OrderBy(n => n)));
    }

    private static IReadOnlyList<NewsletterDigestItem> MapMediaToDigestItems(
        IReadOnlyList<MediaItem> media,
        string baseUrl,
        IReadOnlyDictionary<Guid, string> performersByMedia,
        IReadOnlyDictionary<Guid, string> tagsByMedia,
        IReadOnlySet<string> embeddedContentIds,
        NewsletterDigestSection section)
    {
        return media.Select(m =>
        {
            var contentId = embeddedContentIds.Contains(NewsletterImageEmbedder.ContentIdFor(m.Id))
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
                contentId,
                section);
        }).ToList();
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

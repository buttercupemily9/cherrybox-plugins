using CherryBox.Core.Enums;
using CherryBox.Data;
using CherryBox.Plugins.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CherryBox.Newsletter.Plugin;

public static class NewsletterWeeklyComposer
{
    private const int DigestItemLimit = 12;

    public static async Task<(string Html, string Plain)> BuildAsync(
        CherryBoxDbContext db,
        IPluginServiceRegistry plugins,
        IServiceProvider services,
        string username,
        string? skinId,
        string baseUrl,
        DateTimeOffset since,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        var items = await LoadDigestItemsAsync(db, baseUrl, since, cancellationToken);
        var aiIntro = await TryGenerateAiIntroAsync(plugins, services, username, items, logger, cancellationToken);
        var theme = NewsletterTemplates.GetTheme(skinId);
        var html = NewsletterTemplates.RenderWeeklyDigest(username, baseUrl, theme, items, aiIntro);
        var plain = NewsletterTemplates.WeeklyPlainText(username, baseUrl, items, aiIntro);
        return (html, plain);
    }

    internal static async Task<IReadOnlyList<NewsletterDigestItem>> LoadDigestItemsAsync(
        CherryBoxDbContext db,
        string baseUrl,
        DateTimeOffset since,
        CancellationToken cancellationToken)
    {
        var media = await db.MediaItems.AsNoTracking()
            .Include(m => m.Studio)
            .Where(m => m.UpdatedAt >= since || m.CreatedAt >= since)
            .OrderByDescending(m => m.UpdatedAt)
            .Take(DigestItemLimit)
            .ToListAsync(cancellationToken);

        if (media.Count == 0)
            return [];

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

        return media.Select(m => new NewsletterDigestItem(
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
            string.IsNullOrWhiteSpace(m.SourceSite) ? null : m.SourceSite.Trim())).ToList();
    }

    private static async Task<string?> TryGenerateAiIntroAsync(
        IPluginServiceRegistry plugins,
        IServiceProvider services,
        string username,
        IReadOnlyList<NewsletterDigestItem> items,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        var ai = plugins.Resolve<IAiService>(services);
        if (ai is null)
            return null;

        try
        {
            var settings = await ai.GetSettingsAsync(cancellationToken);
            if (!settings.HasApiKey)
                return null;

            var prompt = NewsletterAiPrompts.BuildUserPrompt(username, items);
            var text = await ai.CompleteChatAsync(
                new AiChatRequest(prompt, NewsletterAiPrompts.SystemPrompt, MaxTokens: 500),
                cancellationToken);

            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "AI newsletter intro generation failed; using default copy.");
            return null;
        }
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

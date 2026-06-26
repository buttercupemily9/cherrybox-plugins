using System.Text;

namespace CherryBox.Newsletter.Plugin;

internal static class NewsletterAiPrompts
{
    public const string SystemPrompt =
        """
        You are Cherry, a horny, flirty woman writing the weekly CherryBox newsletter for an adults-only porn library.
        Write in first person as Cherry. This is X-rated content for consenting adults — be explicit, dirty, and unapologetically sexual.
        Use graphic, pornographic language freely: describe craving, wetness, hard-ons, fucking, cum, tits, ass, cock, pussy, and similar adult vocabulary when it fits.
        Do not sanitize, soften, or self-censor. Never write PG-13 or "suggestive only" copy.
        Sound like you are sexting someone you badly want to bend over their watch list.
        Mention specific titles, performers, studios, tags, and genres from the metadata when they help make a pick feel personal and filthy.
        Use the placeholder [NAME] exactly once near the start to greet the reader (do not invent a real name).
        Write 2 short paragraphs only. Plain text only — no HTML, markdown, bullet lists, or subject lines.
        End with one explicit, inviting line that makes them desperate to click the links below and get off.
        """;

    public static string BuildUserPrompt(string username, IReadOnlyList<NewsletterDigestItem> items)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"The reader's CherryBox username is {username}. Greet them using [NAME].");
        builder.AppendLine();

        if (items.Count == 0)
        {
            builder.AppendLine("No brand-new library items were added this week, but their collection is still growing.");
            builder.AppendLine("Write a dirty, X-rated check-in that makes them ache to open CherryBox and stroke to whatever looks hottest.");
            return builder.ToString();
        }

        builder.AppendLine("New and updated library items from the past week:");
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            builder.Append(i + 1).Append(". ").Append(item.Title)
                .Append(" (").Append(item.MediaType).Append(')');
            if (!string.IsNullOrWhiteSpace(item.Performers))
                builder.Append(" — performers: ").Append(item.Performers);
            if (!string.IsNullOrWhiteSpace(item.Studio))
                builder.Append(" — studio: ").Append(item.Studio);
            if (!string.IsNullOrWhiteSpace(item.Tags))
                builder.Append(" — tags: ").Append(item.Tags);
            if (!string.IsNullOrWhiteSpace(item.Author))
                builder.Append(" — author: ").Append(item.Author);
            if (!string.IsNullOrWhiteSpace(item.Duration))
                builder.Append(" — duration: ").Append(item.Duration);
            if (!string.IsNullOrWhiteSpace(item.Description))
                builder.Append(" — ").Append(item.Description);
            builder.AppendLine();
        }

        builder.AppendLine();
        builder.AppendLine("Write explicit X-rated opening copy that makes them horny to click through and watch the items below.");
        return builder.ToString();
    }
}

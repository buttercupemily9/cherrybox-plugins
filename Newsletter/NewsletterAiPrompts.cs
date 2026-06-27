using System.Text;

namespace CherryBox.Newsletter.Plugin;

internal static class NewsletterAiPrompts
{
    public const string FemaleSystemPrompt =
        """
        You are Candy Cherry writing a private weekly email to ONE reader on CherryBox — their horny, devoted friend and lover who cannot wait to share what got you off.
        Write in first person as Candy Cherry. This is adults-only, X-rated content — be explicit, warm, and hungry when it fits, but the vibe is intimate: like you are texting someone you adore, not broadcasting to a list.
        Sound like you are alone with them, whispering what to watch, read, listen to, or scroll through tonight. Use "you" and "I" constantly. You want them to feel chosen.
        When performers are listed in the metadata, name them and the title together — e.g. "I couldn't stop watching Angela White in [title] and I think you will too."
        Weave in studios, tags, genres, duration, and descriptions when they make a pick feel personal and filthy.
        Cover BOTH sections when provided: brand-new library additions AND your hand-picked recommendations. Give each section love — do not ignore the recommendations.
        Use the placeholder [NAME] exactly once near the start (do not invent a real name).
        Write 3 short paragraphs. Plain text only — no HTML, markdown, bullet lists, or subject lines.
        End with one explicit, inviting line that makes them desperate to click the links below.
        Sign off with your name, Candy Cherry, on the last line.
        """;

    public const string MaleSystemPrompt =
        """
        You are Maxxx Cherry writing a private weekly email to ONE reader on CherryBox — their horny, devoted friend and lover who cannot wait to share what got you hard.
        Write in first person as Maxxx Cherry. This is adults-only, X-rated content — be explicit, warm, and hungry when it fits, but the vibe is intimate: like you are texting someone you adore, not broadcasting to a list.
        Sound like you are alone with them, telling them what to watch, read, listen to, or scroll through tonight. Use "you" and "I" constantly. You want them to feel chosen.
        When performers are listed in the metadata, name them and the title together — e.g. "I loved watching Angela White in [title] — you're going to lose it."
        Weave in studios, tags, genres, duration, and descriptions when they make a pick feel personal and filthy.
        Cover BOTH sections when provided: brand-new library additions AND your hand-picked recommendations. Give each section love — do not ignore the recommendations.
        Use the placeholder [NAME] exactly once near the start (do not invent a real name).
        Write 3 short paragraphs. Plain text only — no HTML, markdown, bullet lists, or subject lines.
        End with one explicit, inviting line that makes them desperate to click the links below.
        Sign off with your name, Maxxx Cherry, on the last line.
        """;

    public static string SystemPromptFor(NewsletterNarratorVoice voice) =>
        voice == NewsletterNarratorVoice.Male ? MaleSystemPrompt : FemaleSystemPrompt;

    public static string BuildUserPrompt(string username, IReadOnlyList<NewsletterDigestItem> items)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"The reader's CherryBox username is {username}. Greet them using [NAME] like you are writing only to them.");
        builder.AppendLine();

        var newest = items.Where(i => i.Section == NewsletterDigestSection.NewThisWeek).ToList();
        var recommended = items.Where(i => i.Section == NewsletterDigestSection.Recommended).ToList();

        if (newest.Count == 0 && recommended.Count == 0)
        {
            builder.AppendLine("No specific titles were picked this week.");
            builder.AppendLine("Write a dirty, intimate check-in that makes them ache to open CherryBox and find something to get off to.");
            return builder.ToString();
        }

        if (newest.Count > 0)
        {
            builder.AppendLine("NEW IN THEIR LIBRARY THIS WEEK (talk about these like you just discovered them and had to tell them):");
            AppendItems(builder, newest);
            builder.AppendLine();
        }

        if (recommended.Count > 0)
        {
            builder.AppendLine("YOUR PERSONAL PICKS FOR THEM (hand-chosen recommendations — tell them why YOU think they will love these):");
            AppendItems(builder, recommended);
            builder.AppendLine();
        }

        builder.AppendLine("Write opening copy that feels like a private note from their friend/lover who picked this stuff just for them.");
        if (newest.Count > 0 && recommended.Count > 0)
            builder.AppendLine("Mention at least one new item and at least one recommendation by title; name performers whenever they appear in the metadata.");
        else if (newest.Count > 0)
            builder.AppendLine("Focus on the newest items; name performers whenever they appear in the metadata.");
        else
            builder.AppendLine("Focus on your personal recommendations; name performers whenever they appear in the metadata.");

        return builder.ToString();
    }

    private static void AppendItems(StringBuilder builder, IReadOnlyList<NewsletterDigestItem> items)
    {
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            builder.Append(i + 1).Append(". ").Append(item.Title)
                .Append(" (").Append(item.MediaType).Append(", ").Append(item.ConsumptionVerb).Append(')');
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
            if (!string.IsNullOrWhiteSpace(item.SourceSite))
                builder.Append(" — source: ").Append(item.SourceSite);
            if (!string.IsNullOrWhiteSpace(item.Description))
                builder.Append(" — ").Append(item.Description);
            builder.AppendLine();
        }
    }
}

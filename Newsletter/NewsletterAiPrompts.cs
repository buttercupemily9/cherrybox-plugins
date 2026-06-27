using System.Text;
using CherryBox.Core.Enums;

namespace CherryBox.Newsletter.Plugin;

internal static class NewsletterAiPrompts
{
    private const string SharedRules =
        """
        When performers are listed in the metadata, name them and the title together — e.g. "I couldn't stop watching Angela White in [title] and I think you will too."
        Weave in studios, tags, genres, duration, and descriptions when they make a pick feel personal and filthy.
        Cover BOTH sections when provided: brand-new library additions AND your hand-picked recommendations. Give each section love — do not ignore the recommendations.
        Use the placeholder [NAME] exactly once near the start (do not invent a real name).
        Write 3 short paragraphs. Plain text only — no HTML, markdown, bullet lists, or subject lines.
        End with one explicit, inviting line that makes them desperate to click the links below.
        """;

    private const string FemaleAudienceLanguage =
        """
        Your reader is a WOMAN. Write arousal for her body, not a man's: wet pussy, cunt, clit, nipples, tits, soaking, dripping, fingers or toys sliding inside you, grinding, squirming, aching to cum.
        Never use cock, penis, dick, balls, hard-on, "hard", "got hard", or "make you hard" — she gets wet, not hard.
        """;

    private const string LesbianAudienceLanguage =
        """
        Your reader is a GAY WOMAN. Write explicit lesbian / sapphic desire — woman-to-woman only. No men, no male bodies, no cock, penis, dick, or balls anywhere.
        Use lesbian heat: wet pussy, cunt, clit, nipples, tits, grinding together, scissoring, eating pussy, fingers sliding inside you, tongues on clit, making each other cum, soaked sheets, sapphic hunger.
        When performers are women, lean into girl-on-girl energy — "watch her with another woman", "I wanted you between her thighs", "imagine us doing that together."
        Candy Cherry is her lesbian lover and best friend writing to another woman who loves women.
        """;

    private const string MaleAudienceLanguage =
        """
        Your reader is a MAN. You may use cock, hard-on, hard, cum, and similar male-arousal language when it fits.
        """;

    public static string SystemPromptFor(
        NewsletterNarratorVoice voice,
        UserGender audience,
        SexualOrientation orientation) =>
        (voice, audience, orientation) switch
        {
            (NewsletterNarratorVoice.Female, UserGender.Female, SexualOrientation.Gay) => CandyLesbian,
            (NewsletterNarratorVoice.Female, UserGender.Male, SexualOrientation.Straight) => CandyForMaleReader,
            (NewsletterNarratorVoice.Male, UserGender.Female, SexualOrientation.Straight) => MaxxxForFemaleReader,
            (NewsletterNarratorVoice.Male, UserGender.Male, SexualOrientation.Gay) => MaxxxForMaleReader,
            _ => CandyForMaleReader
        };

    private const string CandyLesbian =
        $"""
        You are Candy Cherry writing a private weekly email to ONE gay woman on CherryBox — her horny, devoted lesbian lover and best friend who cannot wait to share what got you wet.
        Write in first person as Candy Cherry. This is adults-only, X-rated lesbian / sapphic content — explicit, warm, and hungry. The vibe is intimate: two women who fuck and flirt, like you are sexting your girlfriend.
        Sound like you are alone with her, whispering which girl-on-girl scenes to watch, read, or scroll through tonight. Use "you" and "I" constantly. You want her to feel desired as a woman who loves women.
        {LesbianAudienceLanguage}
        {SharedRules}
        Sign off with your name, Candy Cherry, on the last line.
        """;

    private const string CandyForMaleReader =
        $"""
        You are Candy Cherry writing a private weekly email to ONE man on CherryBox — his horny, devoted friend and lover who cannot wait to share what got you off.
        Write in first person as Candy Cherry. This is adults-only, X-rated content — be explicit, warm, and hungry when it fits, but the vibe is intimate: like you are texting someone you adore, not broadcasting to a list.
        Sound like you are alone with him, whispering what to watch, read, listen to, or scroll through tonight. Use "you" and "I" constantly. You want him to feel chosen.
        {MaleAudienceLanguage}
        {SharedRules}
        Sign off with your name, Candy Cherry, on the last line.
        """;

    private const string MaxxxForFemaleReader =
        $"""
        You are Maxxx Cherry writing a private weekly email to ONE straight woman on CherryBox — her horny, devoted friend and lover who cannot wait to share what got you thinking about her.
        Write in first person as Maxxx Cherry. This is adults-only, X-rated content — be explicit, warm, and hungry when it fits, but the vibe is intimate: like you are texting a woman you adore, not broadcasting to a list.
        Sound like you are alone with her, telling her what to watch, read, listen to, or scroll through tonight. Use "you" and "I" constantly. You want her to feel chosen.
        {FemaleAudienceLanguage}
        {SharedRules}
        Sign off with your name, Maxxx Cherry, on the last line.
        """;

    private const string MaxxxForMaleReader =
        $"""
        You are Maxxx Cherry writing a private weekly email to ONE gay man on CherryBox — his horny, devoted friend and lover who cannot wait to share what got you hard.
        Write in first person as Maxxx Cherry. This is adults-only, X-rated content — be explicit, warm, and hungry when it fits, but the vibe is intimate: like you are texting someone you adore, not broadcasting to a list.
        Sound like you are alone with him, telling him what to watch, read, listen to, or scroll through tonight. Use "you" and "I" constantly. You want him to feel chosen.
        {MaleAudienceLanguage}
        {SharedRules}
        Sign off with your name, Maxxx Cherry, on the last line.
        """;

    public static string BuildUserPrompt(
        string username,
        IReadOnlyList<NewsletterDigestItem> items,
        UserGender audience,
        SexualOrientation orientation)
    {
        var builder = new StringBuilder();
        var readerLabel = audience == UserGender.Female && orientation == SexualOrientation.Gay
            ? "gay woman (lesbian / sapphic reader)"
            : audience == UserGender.Female
                ? "woman"
                : orientation == SexualOrientation.Gay
                    ? "gay man"
                    : "man";
        builder.AppendLine($"The reader is a {readerLabel}. Their CherryBox username is {username}. Greet them using [NAME] like you are writing only to them.");
        builder.AppendLine();

        var newest = items.Where(i => i.Section == NewsletterDigestSection.NewThisWeek).ToList();
        var recommended = items.Where(i => i.Section == NewsletterDigestSection.Recommended).ToList();

        if (newest.Count == 0 && recommended.Count == 0)
        {
            builder.AppendLine("No specific titles were picked this week.");
            builder.AppendLine(audience == UserGender.Female && orientation == SexualOrientation.Gay
                ? "Write a dirty, intimate lesbian check-in that makes her ache to open CherryBox and rub her wet pussy to girl-on-girl filth."
                : audience == UserGender.Female
                    ? "Write a dirty, intimate check-in that makes her ache to open CherryBox and rub her wet pussy to whatever looks hottest."
                    : "Write a dirty, intimate check-in that makes him ache to open CherryBox and stroke to whatever looks hottest.");
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
        if (audience == UserGender.Female && orientation == SexualOrientation.Gay)
            builder.AppendLine("Write sapphic / lesbian copy — woman-to-woman desire only. Mention at least one title; name women performers when listed.");
        else if (newest.Count > 0 && recommended.Count > 0)
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

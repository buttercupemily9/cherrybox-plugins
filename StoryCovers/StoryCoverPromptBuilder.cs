using System.Text;
using CherryBox.Plugins.Abstractions;

namespace CherryBox.StoryCovers.Plugin;

internal static class StoryCoverPromptBuilder
{
    private const string VisualPromptSystem =
        """
        You write image generation prompts for vertical published book covers based on fiction stories.
        Output one detailed prompt only. Plain text, no markdown or quotes around the whole prompt.
        Every cover MUST be a complete book cover design with:
        - Large readable title typography in the upper area
        - Author name typography in the lower area
        - A full-bleed background illustration behind the text that reflects the story setting, mood, and themes
        Describe the background scene, palette, lighting, and composition from the story context.
        Keep artwork SFW: no nudity, no explicit sexual acts, no minors, no copyrighted characters.
        Use symbolic or cinematic imagery rather than literal explicit scenes.
        """;

    public static async Task<string> BuildImagePromptAsync(
        IAiService ai,
        string title,
        string? author,
        string storyExcerpt,
        bool useChatRefinement,
        CancellationToken cancellationToken)
    {
        var titleLine = NormalizeTitle(title);
        var authorLine = NormalizeAuthor(author);
        var backgroundContext = ExtractBackgroundContext(storyExcerpt);

        if (useChatRefinement)
        {
            try
            {
                var userPrompt =
                    $"""
                    Story title: {titleLine}
                    Author: {authorLine}

                    Story context for the background illustration (do not quote explicit scenes):
                    {backgroundContext}

                    Write a single detailed image generation prompt for a vertical book cover.
                    Requirements:
                    - The image IS a book cover with title "{titleLine}" and author "{authorLine}" rendered as readable typography
                    - A full-bleed background illustration reflects the story setting, mood, and themes
                    - Professional publishing layout, cinematic lighting, rich colors, SFW
                    """;

                var refined = await ai.CompleteChatAsync(
                    new AiChatRequest(userPrompt, VisualPromptSystem, MaxTokens: 600),
                    cancellationToken);

                if (!string.IsNullOrWhiteSpace(refined))
                    return EnsureBookCoverPrompt(refined.Trim(), titleLine, authorLine, backgroundContext);
            }
            catch
            {
                // Fall back to direct prompt below.
            }
        }

        return BuildDirectPrompt(titleLine, authorLine, backgroundContext);
    }

    public static string ExtractBackgroundContext(string excerpt)
    {
        if (string.IsNullOrWhiteSpace(excerpt))
            return "dramatic literary fiction atmosphere with moody cinematic lighting";

        var builder = new StringBuilder();
        foreach (var line in excerpt.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length < 8)
                continue;
            if (trimmed.StartsWith("Note", StringComparison.OrdinalIgnoreCase))
                continue;
            if (trimmed.StartsWith("##", StringComparison.Ordinal))
                continue;

            if (builder.Length > 0)
                builder.Append(' ');
            builder.Append(trimmed);
            if (builder.Length >= 320)
                break;
        }

        var text = builder.ToString().Trim();
        if (text.Length == 0)
            return "dramatic literary fiction atmosphere with moody cinematic lighting";

        if (text.Length > 320)
            text = text[..320].Trim() + "...";

        return text;
    }

    public static string BuildAbstractPrompt(string title, string author, string backgroundContext) =>
        EnsureBookCoverPrompt(
            "Symbolic cinematic background illustration inspired by the story. Elegant serif typography, rich jewel-tone palette, soft depth-of-field, tasteful literary fiction aesthetic.",
            NormalizeTitle(title),
            NormalizeAuthor(author),
            backgroundContext);

    public static string BuildGenericPrompt(string title, string author, string backgroundContext) =>
        EnsureBookCoverPrompt(
            "Atmospheric background scene with soft gradient sky and subtle environmental details matching the story mood. Clean polished typography, minimalist publishing layout.",
            NormalizeTitle(title),
            NormalizeAuthor(author),
            backgroundContext);

    private static string BuildDirectPrompt(string title, string author, string backgroundContext) =>
        EnsureBookCoverPrompt(
            "Detailed illustrated background scene reflecting the story setting and emotional tone. Cinematic lighting, rich color palette, professional publishing quality.",
            title,
            author,
            backgroundContext);

    private static string EnsureBookCoverPrompt(
        string artDirection,
        string title,
        string author,
        string backgroundContext) =>
        "Vertical portrait book cover (2:3 aspect ratio). " +
        $"The cover must display large readable title text \"{title}\" in the upper third and author name \"{author}\" in the lower third as part of the image. " +
        $"Full-bleed background illustration behind the typography, based on the story: {backgroundContext}. " +
        artDirection +
        " SFW, no watermarks, no explicit content.";

    private static string NormalizeTitle(string title) =>
        string.IsNullOrWhiteSpace(title) ? "Untitled" : title.Trim();

    private static string NormalizeAuthor(string? author) =>
        string.IsNullOrWhiteSpace(author) ? "Unknown Author" : author.Trim();
}

using CherryBox.Plugins.Abstractions;

namespace CherryBox.StoryCovers.Plugin;

internal static class StoryCoverPromptBuilder
{
    private const string VisualPromptSystem =
        """
        You write image generation prompts for vertical book covers based on adult fiction story excerpts.
        Output one detailed prompt only. Plain text, no markdown or quotes around the whole prompt.
        The cover must look like a professional published book with readable title and author typography.
        Describe mood, setting, characters, palette, and composition inspired by the story themes.
        """;

    public static async Task<string> BuildImagePromptAsync(
        IAiService ai,
        string title,
        string? author,
        string storyExcerpt,
        bool useChatRefinement,
        CancellationToken cancellationToken)
    {
        var titleLine = string.IsNullOrWhiteSpace(title) ? "Untitled" : title.Trim();
        var authorLine = string.IsNullOrWhiteSpace(author) ? "Unknown Author" : author.Trim();

        if (useChatRefinement)
        {
            try
            {
                var userPrompt =
                    $"""
                    Story title: {titleLine}
                    Author: {authorLine}

                    Story excerpt:
                    {storyExcerpt}

                    Write a single detailed image generation prompt for a vertical book cover.
                    The cover MUST include readable title text "{titleLine}" and author text "{authorLine}".
                    Describe artwork mood, setting, characters, and palette inspired by the story.
                    """;

                var refined = await ai.CompleteChatAsync(
                    new AiChatRequest(userPrompt, VisualPromptSystem, MaxTokens: 500),
                    cancellationToken);

                if (!string.IsNullOrWhiteSpace(refined))
                    return refined.Trim();
            }
            catch
            {
                // Fall back to direct prompt below.
            }
        }

        return BuildDirectPrompt(titleLine, authorLine, storyExcerpt);
    }

    private static string BuildDirectPrompt(string title, string author, string excerpt) =>
        $"Professional vertical book cover art for the novel \"{title}\" by {author}. " +
        $"Illustration inspired by the story: {excerpt}. " +
        $"Prominently display the title \"{title}\" at the top and author \"{author}\" near the bottom. " +
        "Cinematic lighting, rich colors, polished publishing cover design, no watermarks.";
}

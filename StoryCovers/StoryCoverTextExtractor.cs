using System.Text;
using System.Text.RegularExpressions;
using CherryBox.Data;
using CherryBox.Media;
using Microsoft.EntityFrameworkCore;

namespace CherryBox.StoryCovers.Plugin;

internal static partial class StoryCoverTextExtractor
{
    public static async Task<string> ExtractPlainTextAsync(
        CherryBoxDbContext db,
        Guid storyMediaItemId,
        CancellationToken cancellationToken)
    {
        var story = await db.MediaItems.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == storyMediaItemId, cancellationToken);
        if (story is null || !File.Exists(story.FilePath))
            throw new InvalidOperationException("Story file was not found.");

        var chapters = await db.StoryChapters.AsNoTracking()
            .Where(c => c.StoryMediaItemId == storyMediaItemId)
            .OrderBy(c => c.Index)
            .ToListAsync(cancellationToken);

        if (chapters.Count > 0)
        {
            var builder = new StringBuilder();
            foreach (var chapter in chapters)
            {
                if (!string.IsNullOrWhiteSpace(chapter.Title))
                {
                    if (builder.Length > 0) builder.AppendLine().AppendLine();
                    builder.Append(chapter.Title.Trim());
                    builder.AppendLine();
                }

                if (!string.IsNullOrWhiteSpace(chapter.Content))
                {
                    if (builder.Length > 0 && !builder.ToString().EndsWith('\n')) builder.AppendLine();
                    builder.AppendLine(StripMarkdown(chapter.Content));
                }
            }

            var chapterText = builder.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(chapterText))
                return chapterText;
        }

        var (_, body) = await StoryFileFormat.ReadAsync(story.FilePath, cancellationToken);
        var plain = StripMarkdown(body);
        if (string.IsNullOrWhiteSpace(plain))
            throw new InvalidOperationException("Story has no readable text content.");

        return plain;
    }

    public static string TruncateForContext(string text, int maxChars)
    {
        text = text.Trim();
        if (text.Length <= maxChars)
            return text;

        var slice = text[..maxChars];
        var lastBreak = slice.LastIndexOf('\n');
        if (lastBreak > maxChars / 2)
            slice = slice[..lastBreak];

        return slice.Trim() + "...";
    }

    private static string StripMarkdown(string markdown)
    {
        var text = markdown;
        text = ImageLinkRegex().Replace(text, "$1");
        text = LinkRegex().Replace(text, "$1");
        text = HeadingRegex().Replace(text, "$1");
        text = text.Replace("**", "", StringComparison.Ordinal);
        text = text.Replace("__", "", StringComparison.Ordinal);
        text = text.Replace('*', ' ');
        text = text.Replace('`', ' ');
        text = HorizontalRuleRegex().Replace(text, "\n");
        text = MultipleNewlineRegex().Replace(text, "\n\n");
        return text.Trim();
    }

    [GeneratedRegex(@"!\[([^\]]*)\]\([^)]+\)", RegexOptions.Compiled)]
    private static partial Regex ImageLinkRegex();

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]+\)", RegexOptions.Compiled)]
    private static partial Regex LinkRegex();

    [GeneratedRegex(@"^#{1,6}\s*", RegexOptions.Multiline | RegexOptions.Compiled)]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"^[-*_]{3,}\s*$", RegexOptions.Multiline | RegexOptions.Compiled)]
    private static partial Regex HorizontalRuleRegex();

    [GeneratedRegex(@"\n{3,}", RegexOptions.Compiled)]
    private static partial Regex MultipleNewlineRegex();
}

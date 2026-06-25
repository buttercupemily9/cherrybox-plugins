using System.Text;
using System.Text.RegularExpressions;
using CherryBox.Data;
using CherryBox.Media;
using Microsoft.EntityFrameworkCore;

namespace CherryBox.StoryTts.Plugin;

internal static partial class StoryTextExtractor
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

    public static IReadOnlyList<string> ChunkText(string text, int maxChars)
    {
        if (maxChars < 500) maxChars = 500;
        text = text.Trim();
        if (text.Length <= maxChars)
            return [text];

        var chunks = new List<string>();
        var paragraphs = text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var current = new StringBuilder();

        void Flush()
        {
            if (current.Length == 0) return;
            chunks.Add(current.ToString().Trim());
            current.Clear();
        }

        foreach (var paragraph in paragraphs)
        {
            if (paragraph.Length > maxChars)
            {
                Flush();
                foreach (var sentenceChunk in SplitLongParagraph(paragraph, maxChars))
                    chunks.Add(sentenceChunk);
                continue;
            }

            var candidate = current.Length == 0
                ? paragraph
                : current + "\n\n" + paragraph;
            if (candidate.Length > maxChars)
            {
                Flush();
                current.Append(paragraph);
            }
            else
            {
                current.Clear();
                current.Append(candidate);
            }
        }

        Flush();
        return chunks.Count == 0 ? [text] : chunks;
    }

    private static IEnumerable<string> SplitLongParagraph(string paragraph, int maxChars)
    {
        var sentences = paragraph.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var current = new StringBuilder();
        foreach (var sentence in sentences)
        {
            var piece = sentence.EndsWith('.') ? sentence : sentence + ".";
            if (piece.Length > maxChars)
            {
                if (current.Length > 0)
                {
                    yield return current.ToString().Trim();
                    current.Clear();
                }

                for (var offset = 0; offset < piece.Length; offset += maxChars)
                {
                    var length = Math.Min(maxChars, piece.Length - offset);
                    yield return piece.Substring(offset, length).Trim();
                }
                continue;
            }

            var candidate = current.Length == 0 ? piece : current + " " + piece;
            if (candidate.Length > maxChars)
            {
                yield return current.ToString().Trim();
                current.Clear();
                current.Append(piece);
            }
            else
            {
                current.Clear();
                current.Append(candidate);
            }
        }

        if (current.Length > 0)
            yield return current.ToString().Trim();
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

using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace StoryImportCommon;

public static partial class SexStoriesContentExtractor
{
    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex BrTagPattern();

    public static string ExtractStoryText(HtmlDocument doc)
    {
        var centerPanel = doc.DocumentNode.SelectSingleNode("//div[@id='story_center_panel']");
        if (centerPanel is null)
            return string.Empty;

        var panels = centerPanel.SelectNodes("./div[contains(@class,'block_panel')]");
        if (panels is null || panels.Count == 0)
            return string.Empty;

        var parts = new List<string>();
        foreach (var panel in panels)
        {
            if (IsCommentsPanel(panel))
                continue;

            var clone = panel.CloneNode(deep: true);
            StripVoteSection(clone);

            var introHeading = clone.SelectSingleNode("./h2[contains(translate(normalize-space(.),'INTRODUCTION','introduction'),'introduction')]");
            if (introHeading is not null)
            {
                introHeading.Remove();
                AppendIfPresent(parts, NodeToStoryText(clone));
                continue;
            }

            AppendIfPresent(parts, NodeToStoryText(clone));
        }

        return string.Join("\n\n", parts).Trim();
    }

    private static void AppendIfPresent(List<string> parts, string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
            parts.Add(text.Trim());
    }

    private static bool IsCommentsPanel(HtmlNode panel) =>
        panel.SelectSingleNode(".//*[contains(@class,'count_comments')]") is not null
        || panel.SelectSingleNode(".//*[@id='comments']") is not null;

    private static void StripVoteSection(HtmlNode panel)
    {
        foreach (var node in panel.SelectNodes(".//*[@id='rating' or @id='addfavorite']") ?? Enumerable.Empty<HtmlNode>())
            node.Remove();

        foreach (var info in panel.SelectNodes(".//div[contains(@class,'story_info')]") ?? Enumerable.Empty<HtmlNode>())
        {
            var text = HtmlEntity.DeEntitize(info.InnerText);
            if (text.Contains("times", StringComparison.OrdinalIgnoreCase)
                && text.Contains("Rated", StringComparison.OrdinalIgnoreCase))
                info.Remove();
        }

        foreach (var hr in panel.SelectNodes(".//hr") ?? Enumerable.Empty<HtmlNode>())
            hr.Remove();
    }

    private static string NodeToStoryText(HtmlNode node)
    {
        var html = BrTagPattern().Replace(node.InnerHtml, "\n");
        var stripped = new HtmlDocument();
        stripped.LoadHtml(html);
        var text = HtmlEntity.DeEntitize(stripped.DocumentNode.InnerText);
        return Regex.Replace(text.Replace("\r\n", "\n"), @"\n{3,}", "\n\n").Trim();
    }
}

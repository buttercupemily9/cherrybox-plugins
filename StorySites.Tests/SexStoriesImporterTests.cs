using HtmlAgilityPack;
using StorySites.Plugin.Importers;
using Xunit;

namespace StorySites.Tests;

public sealed class SexStoriesImporterTests
{
    [Theory]
    [InlineData("kim-possible.html", "Kim Possible (The Better Revised Version)", "tj_hawk")]
    [InlineData("daddy-lil-whore.html", "Daddy's lil whore", "horny25")]
    public void ParsePage_extracts_title_author_and_story_body(string fixture, string expectedTitle, string expectedAuthor)
    {
        var html = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture));
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var result = Parse(doc, new Uri("https://www.sexstories.com/stories/test"));

        Assert.Equal(expectedTitle, result.Title);
        Assert.Equal(expectedAuthor, result.Author);
        Assert.DoesNotContain("sexstories.com", result.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Font size", result.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Author's infos", result.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SUBMIT A COMMENT", result.Text, StringComparison.OrdinalIgnoreCase);
        Assert.True(result.Text.Length > 500);
    }

    private static CherryBox.Plugins.Abstractions.StoryImportPageResult Parse(HtmlDocument doc, Uri url)
    {
        var importer = new SexStoriesImporter();
        var method = typeof(SexStoriesImporter).GetMethod(
            "ParsePage",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        return (CherryBox.Plugins.Abstractions.StoryImportPageResult)method.Invoke(importer, [doc, url])!;
    }
}

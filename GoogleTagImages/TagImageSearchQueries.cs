namespace CherryBox.GoogleTagImages.Plugin;

internal static class TagImageSearchQueries
{
    public static IEnumerable<string> BuildQueries(string tagName, string? userSuffix)
    {
        var tag = tagName.Trim();
        if (string.IsNullOrWhiteSpace(tag))
            yield break;

        yield return $"{tag} porn nsfw xxx";
        yield return $"{tag} porn tag";
        yield return $"{tag} x-rated adult";

        if (!string.IsNullOrWhiteSpace(userSuffix))
            yield return $"{tag} {userSuffix.Trim()}";

        yield return tag;
    }
}

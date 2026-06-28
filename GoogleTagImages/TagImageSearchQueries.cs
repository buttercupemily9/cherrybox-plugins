namespace CherryBox.GoogleTagImages.Plugin;

internal static class TagImageSearchQueries
{
    public static IEnumerable<string> BuildQueries(string tagName, string? userSuffix)
    {
        var tag = tagName.Trim();
        if (string.IsNullOrWhiteSpace(tag))
            yield break;

        yield return $"{tag} porn nsfw xxx nude";
        yield return $"{tag} porn photo thumbnail";
        yield return $"{tag} x-rated adult erotic";
        yield return $"{tag} hardcore sex porn";
        yield return $"{tag} nsfw tag image";

        if (!string.IsNullOrWhiteSpace(userSuffix))
            yield return $"{tag} {userSuffix.Trim()}";

        yield return tag;
    }
}

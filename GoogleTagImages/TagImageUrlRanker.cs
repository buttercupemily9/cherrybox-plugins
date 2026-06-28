using System.Text.RegularExpressions;

namespace CherryBox.GoogleTagImages.Plugin;

internal static partial class TagImageUrlRanker
{
    private static readonly string[] AdultTerms =
    [
        "porn", "xxx", "nsfw", "nude", "naked", "sex", "erotic", "hentai", "x-rated", "xrated",
        "adult", "milf", "fetish", "bdsm", "hardcore", "slutty", "threesome", "orgy", "blowjob",
        "anal", "cumshot", "pussy", "boobs", "tits", "ass", "dick", "cock", "lesbian", "gay porn",
        "amateur porn", "pornstar", "playboy", "onlyfans", "camgirl"
    ];

    private static readonly string[] AdultDomains =
    [
        "pornhub", "xvideos", "xhamster", "redtube", "youporn", "spankbang", "erome", "imagefap",
        "motherless", "efukt", "literotica", "sex.com", "xnxx", "beeg", "tnaflix", "hqporner",
        "eporner", "porntrex", "thumbzilla", "pornpics", "sexstories", "nudevista"
    ];

    private static readonly string[] SafeTerms =
    [
        "wikipedia", "wikimedia", "shutterstock", "getty", "istock", "alamy", "dreamstime",
        "clipart", "logo", "icon", "avatar", "stock photo", "stock-photo", "amazon", "etsy",
        "family friendly", "family-friendly", "safe for work", "sfw", "kids", "children",
        "cartoon network", "disney", "school", "education", "textbook", "news.", "bbc.co",
        "placeholder", "no-image", "default-image"
    ];

    public static IReadOnlyList<string> Rank(IEnumerable<TagImageCandidate> candidates)
    {
        return candidates
            .Select(c => new ScoredCandidate(c, Score(c)))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Candidate.Url, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Candidate.Url)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int Score(TagImageCandidate candidate)
    {
        var haystack = string.Join(
            ' ',
            new[] { candidate.Url, candidate.Title, candidate.SourcePage, candidate.SourceSite }
                .Where(part => !string.IsNullOrWhiteSpace(part)));

        if (string.IsNullOrWhiteSpace(haystack))
            return 0;

        haystack = haystack.ToLowerInvariant();
        var score = 0;

        foreach (var term in AdultTerms)
        {
            if (haystack.Contains(term, StringComparison.Ordinal))
                score += 12;
        }

        foreach (var domain in AdultDomains)
        {
            if (haystack.Contains(domain, StringComparison.Ordinal))
                score += 25;
        }

        foreach (var term in SafeTerms)
        {
            if (haystack.Contains(term, StringComparison.Ordinal))
                score -= 20;
        }

        if (LogoLikePath().IsMatch(candidate.Url))
            score -= 30;

        if (ThumbnailLikePath().IsMatch(candidate.Url))
            score += 4;

        return score;
    }

    [GeneratedRegex(@"(?:logo|icon|avatar|badge|sprite|favicon)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex LogoLikePath();

    [GeneratedRegex(@"(?:thumb|thumbnail|preview|cover|poster|gallery|photo|pic|img|media)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex ThumbnailLikePath();

    private sealed record ScoredCandidate(TagImageCandidate Candidate, int Score);
}

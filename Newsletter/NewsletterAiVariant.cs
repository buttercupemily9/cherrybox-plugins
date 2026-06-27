using CherryBox.Core.Enums;

namespace CherryBox.Newsletter.Plugin;

internal static class NewsletterAiVariant
{
    public static string CacheKey(NewsletterNarratorVoice voice, UserGender audience, SexualOrientation orientation) =>
        $"{voice}:{audience}:{orientation}";

    /// <summary>
    /// The four narrator/reader pairs produced by <see cref="NewsletterVoiceSelector.Resolve"/>.
    /// </summary>
    public static IEnumerable<(NewsletterNarratorVoice Voice, UserGender Audience, SexualOrientation Orientation)> All()
    {
        yield return (NewsletterNarratorVoice.Female, UserGender.Female, SexualOrientation.Gay);
        yield return (NewsletterNarratorVoice.Male, UserGender.Female, SexualOrientation.Straight);
        yield return (NewsletterNarratorVoice.Female, UserGender.Male, SexualOrientation.Straight);
        yield return (NewsletterNarratorVoice.Male, UserGender.Male, SexualOrientation.Gay);
    }

    /// <summary>
    /// Maps a user's resolved narrator voice to the cached AI intro variant (handles bi readers).
    /// </summary>
    public static (NewsletterNarratorVoice Voice, UserGender Audience, SexualOrientation Orientation) ResolveForUser(
        NewsletterNarratorVoice voice,
        UserGender audience,
        SexualOrientation? orientation)
    {
        var resolvedOrientation = orientation ?? SexualOrientation.Straight;
        if (resolvedOrientation == SexualOrientation.Bi)
        {
            return audience switch
            {
                UserGender.Female when voice == NewsletterNarratorVoice.Female =>
                    (NewsletterNarratorVoice.Female, UserGender.Female, SexualOrientation.Gay),
                UserGender.Female =>
                    (NewsletterNarratorVoice.Male, UserGender.Female, SexualOrientation.Straight),
                UserGender.Male when voice == NewsletterNarratorVoice.Male =>
                    (NewsletterNarratorVoice.Male, UserGender.Male, SexualOrientation.Gay),
                _ =>
                    (NewsletterNarratorVoice.Female, UserGender.Male, SexualOrientation.Straight)
            };
        }

        return (voice, audience, resolvedOrientation);
    }

    public static bool TryGetIntro(
        WeeklyDigestCache cache,
        NewsletterNarratorVoice voice,
        UserGender audience,
        SexualOrientation? orientation,
        out string? aiIntro)
    {
        var variant = ResolveForUser(voice, audience, orientation);
        if (cache.Versions.TryGetValue(CacheKey(variant.Voice, variant.Audience, variant.Orientation), out var version))
        {
            aiIntro = version.AiIntro;
            return true;
        }

        // Legacy cache keys (pre-1.5.2 orientation-aware variants).
        if (cache.Versions.TryGetValue($"{voice}:{audience}", out version))
        {
            aiIntro = version.AiIntro;
            return true;
        }

        if (cache.Versions.TryGetValue(voice.ToString(), out version))
        {
            aiIntro = version.AiIntro;
            return true;
        }

        aiIntro = null;
        return false;
    }
}

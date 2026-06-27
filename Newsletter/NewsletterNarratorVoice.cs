using CherryBox.Core.Enums;

namespace CherryBox.Newsletter.Plugin;

public enum NewsletterNarratorVoice
{
    Male = 0,
    Female = 1
}

internal static class NewsletterVoiceSelector
{
    public const string FemaleDisplayName = "Candy Cherry";
    public const string MaleDisplayName = "Maxxx Cherry";

    public static string DisplayName(NewsletterNarratorVoice voice) =>
        voice == NewsletterNarratorVoice.Male ? MaleDisplayName : FemaleDisplayName;

    /// <summary>
    /// Maps the reader's gender and orientation to the narrator voice used in the weekly digest.
    /// </summary>
    public static NewsletterNarratorVoice Resolve(
        UserGender? gender,
        SexualOrientation? orientation,
        Random? random = null)
    {
        if (!gender.HasValue || !orientation.HasValue)
            return NewsletterNarratorVoice.Female;

        if (orientation == SexualOrientation.Bi)
        {
            return (random ?? Random.Shared).Next(2) == 0
                ? NewsletterNarratorVoice.Male
                : NewsletterNarratorVoice.Female;
        }

        return (gender, orientation) switch
        {
            (UserGender.Male, SexualOrientation.Gay) => NewsletterNarratorVoice.Male,
            (UserGender.Male, SexualOrientation.Straight) => NewsletterNarratorVoice.Female,
            (UserGender.Female, SexualOrientation.Straight) => NewsletterNarratorVoice.Male,
            (UserGender.Female, SexualOrientation.Gay) => NewsletterNarratorVoice.Female,
            _ => NewsletterNarratorVoice.Female
        };
    }

    public static string Describe(NewsletterNarratorVoice voice) =>
        voice == NewsletterNarratorVoice.Male ? MaleDisplayName : FemaleDisplayName;

    public static bool TryParseVoice(string? value, out NewsletterNarratorVoice voice)
    {
        voice = NewsletterNarratorVoice.Female;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return Enum.TryParse(value.Trim(), true, out voice);
    }

    public static bool TryParseGender(string? value, out UserGender gender)
    {
        gender = UserGender.Female;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return Enum.TryParse(value.Trim(), true, out gender);
    }

    public static bool TryParseOrientation(string? value, out SexualOrientation orientation)
    {
        orientation = SexualOrientation.Straight;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return Enum.TryParse(value.Trim(), true, out orientation);
    }
}

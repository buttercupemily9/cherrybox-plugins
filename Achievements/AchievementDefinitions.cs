namespace CherryBox.Achievements.Plugin;

internal sealed record AchievementDefinition(
    string Id,
    string Title,
    string Description,
    string Category,
    int Target,
    Func<UserAchievementStats, int> Progress,
    Func<UserAchievementStats, bool> IsMet);

internal sealed class UserAchievementStats
{
    public int Plays { get; init; }
    public int StoryViews { get; init; }
    public int TotalPulses { get; init; }
    public int FavoritesCount { get; init; }
    public int RatingsCount { get; init; }
    public int FiveStarCount { get; init; }
    public bool HasFavoriteVideoOrAudio { get; init; }
    public bool HasFavoriteStory { get; init; }
    public bool HasFavoritePicture { get; init; }
}

internal static class AchievementDefinitions
{
    public static IReadOnlyList<AchievementDefinition> All { get; } = new List<AchievementDefinition>
    {
        Def("first-play", "First Look", "Press play on your first video or audio track.", "Watching", 1,
            s => s.Plays, s => s.Plays >= 1),
        Def("plays-10", "Warming Up", "Play 10 videos or audio tracks.", "Watching", 10,
            s => s.Plays, s => s.Plays >= 10),
        Def("plays-50", "Marathon Session", "Play 50 videos or audio tracks.", "Watching", 50,
            s => s.Plays, s => s.Plays >= 50),
        Def("first-story", "Bedtime Reading", "Open your first story.", "Stories", 1,
            s => s.StoryViews, s => s.StoryViews >= 1),
        Def("stories-10", "Serial Reader", "Open 10 stories.", "Stories", 10,
            s => s.StoryViews, s => s.StoryViews >= 10),
        Def("first-pulse", "First Throb", "Pulse your first scene.", "Engagement", 1,
            s => s.TotalPulses, s => s.TotalPulses >= 1),
        Def("pulses-25", "Into It", "Pulse 25 times across your library.", "Engagement", 25,
            s => s.TotalPulses, s => s.TotalPulses >= 25),
        Def("pulses-100", "Fully Worked Up", "Pulse 100 times.", "Engagement", 100,
            s => s.TotalPulses, s => s.TotalPulses >= 100),
        Def("first-favorite", "Saved for Later", "Favorite your first item.", "Collection", 1,
            s => s.FavoritesCount, s => s.FavoritesCount >= 1),
        Def("favorites-10", "Private Stash", "Favorite 10 items.", "Collection", 10,
            s => s.FavoritesCount, s => s.FavoritesCount >= 10),
        Def("favorites-25", "Curated Collection", "Favorite 25 items.", "Collection", 25,
            s => s.FavoritesCount, s => s.FavoritesCount >= 25),
        Def("first-rating", "Hot Take", "Rate your first video or story.", "Ratings", 1,
            s => s.RatingsCount, s => s.RatingsCount >= 1),
        Def("ratings-10", "Tough Critic", "Rate 10 different items.", "Ratings", 10,
            s => s.RatingsCount, s => s.RatingsCount >= 10),
        Def("five-star-5", "Perfect Ten", "Give 5 stars to 5 different items.", "Ratings", 5,
            s => s.FiveStarCount, s => s.FiveStarCount >= 5),
        Def("variety-favorites", "Tastes Like Candy", "Favorite a video or audio, a story, and a picture.", "Collection", 3,
            s => FavoriteTypeCount(s),
            s => s.HasFavoriteVideoOrAudio && s.HasFavoriteStory && s.HasFavoritePicture),
    };

    private static int FavoriteTypeCount(UserAchievementStats stats)
    {
        var count = 0;
        if (stats.HasFavoriteVideoOrAudio) count++;
        if (stats.HasFavoriteStory) count++;
        if (stats.HasFavoritePicture) count++;
        return count;
    }

    private static AchievementDefinition Def(
        string id,
        string title,
        string description,
        string category,
        int target,
        Func<UserAchievementStats, int> progress,
        Func<UserAchievementStats, bool> isMet) =>
        new(id, title, description, category, target, progress, isMet);
}

using CherryBox.Core.Enums;
using CherryBox.Data;
using CherryBox.Plugins.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace CherryBox.Achievements.Plugin;

public sealed class AchievementService : IAchievementService
{
    private readonly CherryBoxDbContext _db;
    private readonly AchievementStore _store;

    public AchievementService(CherryBoxDbContext db, AchievementStore store)
    {
        _db = db;
        _store = store;
    }

    public async Task<IReadOnlyList<AchievementDto>> ListForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var stats = await BuildStatsAsync(userId, cancellationToken);
        var unlocked = await _store.GetUnlockedAsync(userId, cancellationToken);

        return AchievementDefinitions.All
            .Select(def =>
            {
                var progress = Math.Min(def.Target, def.Progress(stats));
                unlocked.TryGetValue(def.Id, out var unlockedAt);
                var isUnlocked = unlocked.ContainsKey(def.Id) || def.IsMet(stats);
                return new AchievementDto(
                    def.Id,
                    def.Title,
                    def.Description,
                    def.Category,
                    def.Target,
                    progress,
                    isUnlocked,
                    unlocked.ContainsKey(def.Id) ? unlockedAt : null);
            })
            .ToList();
    }

    public async Task<AchievementSummaryDto> GetSummaryAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var achievements = await ListForUserAsync(userId, cancellationToken);
        return new AchievementSummaryDto(
            achievements.Count(a => a.Unlocked),
            achievements.Count);
    }

    public Task<IReadOnlyList<AchievementUnlockDto>> HandleEventAsync(
        Guid userId,
        AchievementEventKind kind,
        CancellationToken cancellationToken = default) =>
        ProcessEventAsync(userId, kind, cancellationToken);

    public Task<IReadOnlyList<AchievementUnlockDto>> SyncAsync(Guid userId, CancellationToken cancellationToken = default) =>
        EvaluateUnlocksAsync(userId, cancellationToken);

    private async Task<IReadOnlyList<AchievementUnlockDto>> ProcessEventAsync(
        Guid userId,
        AchievementEventKind kind,
        CancellationToken cancellationToken)
    {
        switch (kind)
        {
            case AchievementEventKind.Play:
                await _store.IncrementPlaysAsync(userId, cancellationToken);
                break;
            case AchievementEventKind.StoryView:
                await _store.IncrementStoryViewsAsync(userId, cancellationToken);
                break;
        }

        return await EvaluateUnlocksAsync(userId, cancellationToken);
    }

    private async Task<IReadOnlyList<AchievementUnlockDto>> EvaluateUnlocksAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var stats = await BuildStatsAsync(userId, cancellationToken);
        var unlocked = await _store.GetUnlockedAsync(userId, cancellationToken);
        var newlyUnlocked = new List<AchievementUnlockDto>();
        var now = DateTimeOffset.UtcNow;

        foreach (var def in AchievementDefinitions.All)
        {
            if (unlocked.ContainsKey(def.Id) || !def.IsMet(stats))
                continue;

            await _store.UnlockAsync(userId, def.Id, now, cancellationToken);
            newlyUnlocked.Add(new AchievementUnlockDto(def.Id, def.Title, def.Description));
        }

        return newlyUnlocked;
    }

    private async Task<UserAchievementStats> BuildStatsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var counters = await _store.GetCountersAsync(userId, cancellationToken);

        var mediaEngagements = await _db.UserMediaEngagements.AsNoTracking()
            .Where(e => e.UserId == userId)
            .Select(e => new { e.Favorited, e.Rating, e.PulseCount, e.MediaItemId })
            .ToListAsync(cancellationToken);

        var storyEngagements = await _db.UserStoryEngagements.AsNoTracking()
            .Where(e => e.UserId == userId)
            .Select(e => new { e.Favorited, e.Rating, e.PulseCount })
            .ToListAsync(cancellationToken);

        var favoriteMediaIds = mediaEngagements
            .Where(e => e.Favorited)
            .Select(e => e.MediaItemId)
            .ToList();

        HashSet<MediaType> favoriteTypes;
        if (favoriteMediaIds.Count == 0)
        {
            favoriteTypes = new HashSet<MediaType>();
        }
        else
        {
            var types = await _db.MediaItems.AsNoTracking()
                .Where(m => favoriteMediaIds.Contains(m.Id))
                .Select(m => m.MediaType)
                .ToListAsync(cancellationToken);
            favoriteTypes = types.ToHashSet();
        }

        var mediaPulses = mediaEngagements.Sum(e => e.PulseCount);
        var storyPulses = storyEngagements.Sum(e => e.PulseCount);
        var mediaRatings = mediaEngagements.Count(e => e.Rating is not null);
        var storyRatings = storyEngagements.Count(e => e.Rating is not null);
        var mediaFiveStars = mediaEngagements.Count(e => e.Rating == 5);
        var storyFiveStars = storyEngagements.Count(e => e.Rating == 5);
        var mediaFavorites = mediaEngagements.Count(e => e.Favorited);
        var storyFavorites = storyEngagements.Count(e => e.Favorited);

        return new UserAchievementStats
        {
            Plays = counters.Plays,
            StoryViews = counters.StoryViews,
            TotalPulses = mediaPulses + storyPulses,
            FavoritesCount = mediaFavorites + storyFavorites,
            RatingsCount = mediaRatings + storyRatings,
            FiveStarCount = mediaFiveStars + storyFiveStars,
            HasFavoriteVideoOrAudio = favoriteTypes.Contains(MediaType.Video) || favoriteTypes.Contains(MediaType.Audio),
            HasFavoriteStory = storyFavorites > 0,
            HasFavoritePicture = favoriteTypes.Contains(MediaType.Image),
        };
    }
}

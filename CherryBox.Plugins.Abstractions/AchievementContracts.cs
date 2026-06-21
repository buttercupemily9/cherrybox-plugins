namespace CherryBox.Plugins.Abstractions;

public enum AchievementEventKind
{
    Play,
    Pulse,
    Favorite,
    Rating,
    StoryView
}

public sealed record AchievementDto(
    string Id,
    string Title,
    string Description,
    string Category,
    int Target,
    int Progress,
    bool Unlocked,
    DateTimeOffset? UnlockedAt);

public sealed record AchievementUnlockDto(
    string Id,
    string Title,
    string Description);

public sealed record AchievementSummaryDto(
    int UnlockedCount,
    int TotalCount);

public interface IAchievementService
{
    Task<IReadOnlyList<AchievementDto>> ListForUserAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<AchievementSummaryDto> GetSummaryAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AchievementUnlockDto>> HandleEventAsync(Guid userId, AchievementEventKind kind, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AchievementUnlockDto>> SyncAsync(Guid userId, CancellationToken cancellationToken = default);
}

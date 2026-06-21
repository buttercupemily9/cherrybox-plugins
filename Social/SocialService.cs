using CherryBox.Core.Entities;
using CherryBox.Data;
using CherryBox.Plugins.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace CherryBox.Social.Plugin;

public sealed class SocialService : ISocialService
{
    private readonly CherryBoxDbContext _db;
    private readonly SocialStore _store;

    public SocialService(CherryBoxDbContext db, SocialStore store)
    {
        _db = db;
        _store = store;
    }

    public async Task<SocialSelfProfileDto> GetSelfProfileAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await RequireUserAsync(userId, cancellationToken);
        var profile = await _store.GetProfileAsync(userId, cancellationToken);
        return new SocialSelfProfileDto(user.Username, profile.BioMarkdown, profile.LikesPublic);
    }

    public async Task<SocialSelfProfileDto> UpdateSelfProfileAsync(
        Guid userId,
        UpdateSocialProfileRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await RequireUserAsync(userId, cancellationToken);
        await _store.UpsertProfileAsync(userId, request.BioMarkdown?.Trim() ?? string.Empty, request.LikesPublic, cancellationToken);
        return new SocialSelfProfileDto(user.Username, request.BioMarkdown?.Trim() ?? string.Empty, request.LikesPublic);
    }

    public async Task<SocialUserProfileDto?> GetUserProfileAsync(
        Guid viewerId,
        string username,
        CancellationToken cancellationToken = default)
    {
        var target = await FindUserByUsernameAsync(username, cancellationToken);
        if (target is null)
            return null;

        var profile = await _store.GetProfileAsync(target.Id, cancellationToken);
        var friendship = await GetFriendshipStateAsync(viewerId, target.Id, cancellationToken);
        var friendCount = await _store.CountAcceptedFriendsAsync(target.Id, cancellationToken);

        return new SocialUserProfileDto(
            target.Id,
            target.Username,
            profile.BioMarkdown,
            profile.LikesPublic,
            target.Id == viewerId,
            friendship.IsFriend,
            friendship.OutgoingPending,
            friendship.IncomingPending,
            friendCount);
    }

    public async Task<IReadOnlyList<SocialUserSearchResult>> SearchUsersAsync(
        Guid viewerId,
        string query,
        CancellationToken cancellationToken = default)
    {
        var term = query.Trim();
        if (term.Length < 1)
            return Array.Empty<SocialUserSearchResult>();

        var users = await _db.Users.AsNoTracking()
            .Where(u => u.IsActive && u.Id != viewerId && EF.Functions.Like(u.Username, $"%{term}%"))
            .OrderBy(u => u.Username)
            .Take(20)
            .ToListAsync(cancellationToken);

        var results = new List<SocialUserSearchResult>();
        foreach (var user in users)
        {
            var friendship = await GetFriendshipStateAsync(viewerId, user.Id, cancellationToken);
            results.Add(new SocialUserSearchResult(user.Id, user.Username, friendship.IsFriend));
        }

        return results;
    }

    public async Task<MediaSocialStatsDto> GetMediaStatsAsync(
        Guid userId,
        Guid mediaId,
        CancellationToken cancellationToken = default)
    {
        await RequireMediaAsync(mediaId, cancellationToken);
        return await BuildMediaStatsAsync(userId, mediaId, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, MediaSocialStatsDto>> GetMediaStatsBatchAsync(
        Guid userId,
        IReadOnlyList<Guid> mediaIds,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<Guid, MediaSocialStatsDto>();
        foreach (var mediaId in mediaIds.Distinct())
        {
            if (!await _db.MediaItems.AsNoTracking().AnyAsync(m => m.Id == mediaId, cancellationToken))
                continue;

            result[mediaId] = await BuildMediaStatsAsync(userId, mediaId, cancellationToken);
        }

        return result;
    }

    public async Task<MediaSocialStatsDto> SetMediaVoteAsync(
        Guid userId,
        Guid mediaId,
        int vote,
        CancellationToken cancellationToken = default)
    {
        await RequireMediaAsync(mediaId, cancellationToken);
        if (vote is not (0 or 1 or -1))
            throw new InvalidOperationException("Vote must be 1 (like), -1 (dislike), or 0 (clear).");

        await _store.SetVoteAsync(userId, mediaId, vote, cancellationToken);
        return await BuildMediaStatsAsync(userId, mediaId, cancellationToken);
    }

    public async Task<IReadOnlyList<MediaCommentDto>> ListMediaCommentsAsync(
        Guid mediaId,
        CancellationToken cancellationToken = default)
    {
        await RequireMediaAsync(mediaId, cancellationToken);
        var rows = await _store.ListMediaCommentsAsync(mediaId, cancellationToken);
        return await MapMediaCommentsAsync(rows, cancellationToken);
    }

    public async Task<MediaCommentDto> AddMediaCommentAsync(
        Guid userId,
        Guid mediaId,
        string body,
        CancellationToken cancellationToken = default)
    {
        var trimmed = body.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            throw new InvalidOperationException("Comment cannot be empty.");

        await RequireMediaAsync(mediaId, cancellationToken);
        var row = await _store.AddMediaCommentAsync(userId, mediaId, trimmed, cancellationToken);
        var user = await RequireUserAsync(userId, cancellationToken);
        return new MediaCommentDto(row.Id, row.UserId, user.Username, row.Body, row.CreatedAt);
    }

    public async Task<IReadOnlyList<ProfileCommentDto>> ListProfileCommentsAsync(
        string username,
        CancellationToken cancellationToken = default)
    {
        var target = await FindUserByUsernameAsync(username, cancellationToken)
            ?? throw new InvalidOperationException("User not found.");

        var rows = await _store.ListProfileCommentsAsync(target.Id, cancellationToken);
        return await MapProfileCommentsAsync(rows, cancellationToken);
    }

    public async Task<ProfileCommentDto> AddProfileCommentAsync(
        Guid authorId,
        string targetUsername,
        string body,
        CancellationToken cancellationToken = default)
    {
        var trimmed = body.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            throw new InvalidOperationException("Comment cannot be empty.");

        var target = await FindUserByUsernameAsync(targetUsername, cancellationToken)
            ?? throw new InvalidOperationException("User not found.");

        var row = await _store.AddProfileCommentAsync(authorId, target.Id, trimmed, cancellationToken);
        var author = await RequireUserAsync(authorId, cancellationToken);
        return new ProfileCommentDto(row.Id, row.AuthorUserId, author.Username, row.Body, row.CreatedAt);
    }

    public async Task<IReadOnlyList<FriendListItemDto>> ListFriendsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var friendships = await _store.ListFriendshipsForUserAsync(userId, cancellationToken);
        var items = new Dictionary<Guid, FriendListItemDto>();

        foreach (var (requesterId, addresseeId, status) in friendships)
        {
            var partnerId = requesterId == userId ? addresseeId : requesterId;
            var partner = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == partnerId, cancellationToken);
            if (partner is null)
                continue;

            var pendingIncoming = status == "pending" && addresseeId == userId;
            var pendingOutgoing = status == "pending" && requesterId == userId;
            if (status == "accepted" || pendingIncoming || pendingOutgoing)
            {
                items[partnerId] = new FriendListItemDto(partnerId, partner.Username, pendingIncoming, pendingOutgoing);
            }
        }

        return items.Values.OrderBy(f => f.Username, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task SendFriendRequestAsync(Guid userId, string targetUsername, CancellationToken cancellationToken = default)
    {
        var target = await FindUserByUsernameAsync(targetUsername, cancellationToken)
            ?? throw new InvalidOperationException("User not found.");
        if (target.Id == userId)
            throw new InvalidOperationException("You cannot friend yourself.");

        var existing = await _store.GetFriendshipStatusAsync(userId, target.Id, cancellationToken);
        if (existing == "accepted")
            throw new InvalidOperationException("You are already friends.");
        if (existing == "pending")
            throw new InvalidOperationException("A friend request is already pending.");

        await _store.UpsertFriendshipAsync(userId, target.Id, "pending", cancellationToken);
    }

    public async Task AcceptFriendRequestAsync(
        Guid userId,
        string requesterUsername,
        CancellationToken cancellationToken = default)
    {
        var requester = await FindUserByUsernameAsync(requesterUsername, cancellationToken)
            ?? throw new InvalidOperationException("User not found.");

        var status = await _store.GetFriendshipStatusAsync(requester.Id, userId, cancellationToken);
        if (status != "pending")
            throw new InvalidOperationException("No pending friend request from this user.");

        await _store.DeleteFriendshipAsync(requester.Id, userId, cancellationToken);
        await _store.UpsertFriendshipAsync(requester.Id, userId, "accepted", cancellationToken);
    }

    public async Task RemoveFriendAsync(Guid userId, string friendUsername, CancellationToken cancellationToken = default)
    {
        var friend = await FindUserByUsernameAsync(friendUsername, cancellationToken)
            ?? throw new InvalidOperationException("User not found.");

        await _store.DeleteFriendshipAsync(userId, friend.Id, cancellationToken);
    }

    public async Task<IReadOnlyList<MessageThreadDto>> ListMessageThreadsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var rows = await _store.ListMessagesForUserAsync(userId, cancellationToken);
        var threads = new Dictionary<Guid, MessageThreadDto>();

        foreach (var group in rows.GroupBy(m => m.FromUserId == userId ? m.ToUserId : m.FromUserId))
        {
            var partner = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == group.Key, cancellationToken);
            if (partner is null)
                continue;

            var ordered = group.OrderByDescending(m => m.CreatedAt).ToList();
            var latest = ordered[0];
            threads[group.Key] = new MessageThreadDto(
                partner.Id,
                partner.Username,
                TrimPreview(latest.Body),
                latest.CreatedAt,
                ordered.Count(m => m.ToUserId == userId && m.ReadAt is null));
        }

        return threads.Values.OrderByDescending(t => t.LastMessageAt).ToList();
    }

    public async Task<IReadOnlyList<MessageDto>> ListMessagesAsync(
        Guid userId,
        string withUsername,
        CancellationToken cancellationToken = default)
    {
        var partner = await FindUserByUsernameAsync(withUsername, cancellationToken)
            ?? throw new InvalidOperationException("User not found.");

        await _store.MarkMessagesReadAsync(userId, partner.Id, cancellationToken);
        var rows = await _store.ListMessagesBetweenAsync(userId, partner.Id, cancellationToken);
        return await MapMessagesAsync(rows, cancellationToken);
    }

    public async Task<MessageDto> SendMessageAsync(
        Guid userId,
        string toUsername,
        string body,
        CancellationToken cancellationToken = default)
    {
        var trimmed = body.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            throw new InvalidOperationException("Message cannot be empty.");

        var recipient = await FindUserByUsernameAsync(toUsername, cancellationToken)
            ?? throw new InvalidOperationException("User not found.");
        if (recipient.Id == userId)
            throw new InvalidOperationException("You cannot message yourself.");

        var row = await _store.AddMessageAsync(userId, recipient.Id, trimmed, cancellationToken);
        return await MapMessageAsync(row, cancellationToken);
    }

    public Task<int> GetUnreadMessageCountAsync(Guid userId, CancellationToken cancellationToken = default) =>
        _store.GetUnreadCountAsync(userId, cancellationToken);

    public async Task<IReadOnlyList<PublicLikeDto>> ListPublicLikesAsync(
        Guid viewerId,
        string username,
        CancellationToken cancellationToken = default)
    {
        var target = await FindUserByUsernameAsync(username, cancellationToken)
            ?? throw new InvalidOperationException("User not found.");

        var profile = await _store.GetProfileAsync(target.Id, cancellationToken);
        var friendship = await GetFriendshipStateAsync(viewerId, target.Id, cancellationToken);
        if (target.Id != viewerId && (!profile.LikesPublic || !friendship.IsFriend))
            throw new InvalidOperationException("This user's likes are not visible to you.");

        var likes = await _store.ListLikesByUserAsync(target.Id, cancellationToken);
        var mediaIds = likes.Select(l => l.MediaId).ToList();
        var media = await _db.MediaItems.AsNoTracking()
            .Where(m => mediaIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, cancellationToken);

        return likes
            .Where(l => media.ContainsKey(l.MediaId))
            .Select(l =>
            {
                var item = media[l.MediaId];
                return new PublicLikeDto(
                    l.MediaId,
                    item.Title ?? item.FileName,
                    item.MediaType.ToString(),
                    l.LikedAt);
            })
            .ToList();
    }

    private async Task<MediaSocialStatsDto> BuildMediaStatsAsync(
        Guid userId,
        Guid mediaId,
        CancellationToken cancellationToken)
    {
        var counts = await _store.GetVoteCountsAsync(mediaId, cancellationToken);
        var commentCount = await _store.GetCommentCountAsync(mediaId, cancellationToken);
        var userVote = await _store.GetUserVoteAsync(userId, mediaId, cancellationToken);

        int? rating = null;
        var engagement = await _db.UserMediaEngagements.AsNoTracking()
            .FirstOrDefaultAsync(e => e.UserId == userId && e.MediaItemId == mediaId, cancellationToken);
        if (engagement?.Rating is not null)
            rating = engagement.Rating;

        if (rating is null)
        {
            var storyEngagement = await _db.UserStoryEngagements.AsNoTracking()
                .FirstOrDefaultAsync(e => e.UserId == userId && e.StoryMediaItemId == mediaId, cancellationToken);
            rating = storyEngagement?.Rating;
        }

        var vote = userVote switch
        {
            1 => MediaVoteValue.Like,
            -1 => MediaVoteValue.Dislike,
            _ => MediaVoteValue.None
        };

        return new MediaSocialStatsDto(counts.Likes, counts.Dislikes, commentCount, rating, vote);
    }

    private async Task<IReadOnlyList<MediaCommentDto>> MapMediaCommentsAsync(
        IReadOnlyList<(Guid Id, Guid UserId, string Body, DateTimeOffset CreatedAt)> rows,
        CancellationToken cancellationToken)
    {
        var userIds = rows.Select(r => r.UserId).Distinct().ToList();
        var users = await _db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Username, cancellationToken);

        return rows.Select(r => new MediaCommentDto(
            r.Id,
            r.UserId,
            users.TryGetValue(r.UserId, out var name) ? name : "Unknown",
            r.Body,
            r.CreatedAt)).ToList();
    }

    private async Task<IReadOnlyList<ProfileCommentDto>> MapProfileCommentsAsync(
        IReadOnlyList<(Guid Id, Guid AuthorUserId, string Body, DateTimeOffset CreatedAt)> rows,
        CancellationToken cancellationToken)
    {
        var userIds = rows.Select(r => r.AuthorUserId).Distinct().ToList();
        var users = await _db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Username, cancellationToken);

        return rows.Select(r => new ProfileCommentDto(
            r.Id,
            r.AuthorUserId,
            users.TryGetValue(r.AuthorUserId, out var name) ? name : "Unknown",
            r.Body,
            r.CreatedAt)).ToList();
    }

    private async Task<IReadOnlyList<MessageDto>> MapMessagesAsync(
        IReadOnlyList<(Guid Id, Guid FromUserId, Guid ToUserId, string Body, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt)> rows,
        CancellationToken cancellationToken)
    {
        var result = new List<MessageDto>();
        foreach (var row in rows)
            result.Add(await MapMessageAsync(row, cancellationToken));
        return result;
    }

    private async Task<MessageDto> MapMessageAsync(
        (Guid Id, Guid FromUserId, Guid ToUserId, string Body, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt) row,
        CancellationToken cancellationToken)
    {
        var from = await _db.Users.AsNoTracking().FirstAsync(u => u.Id == row.FromUserId, cancellationToken);
        var to = await _db.Users.AsNoTracking().FirstAsync(u => u.Id == row.ToUserId, cancellationToken);
        return new MessageDto(
            row.Id,
            row.FromUserId,
            from.Username,
            row.ToUserId,
            to.Username,
            row.Body,
            row.CreatedAt,
            row.ReadAt);
    }

    private static string TrimPreview(string body) =>
        body.Length <= 80 ? body : body[..77] + "…";

    private async Task<(bool IsFriend, bool OutgoingPending, bool IncomingPending)> GetFriendshipStateAsync(
        Guid viewerId,
        Guid targetId,
        CancellationToken cancellationToken)
    {
        if (viewerId == targetId)
            return (false, false, false);

        var status = await _store.GetFriendshipStatusAsync(viewerId, targetId, cancellationToken);
        if (status == "accepted")
            return (true, false, false);

        var reverse = await _store.GetFriendshipStatusAsync(targetId, viewerId, cancellationToken);
        if (reverse == "accepted")
            return (true, false, false);

        if (status == "pending")
            return (false, true, false);
        if (reverse == "pending")
            return (false, false, true);

        return (false, false, false);
    }

    private async Task<User> RequireUserAsync(Guid userId, CancellationToken cancellationToken) =>
        await _db.Users.FindAsync([userId], cancellationToken)
        ?? throw new InvalidOperationException("User not found.");

    private async Task<User?> FindUserByUsernameAsync(string username, CancellationToken cancellationToken) =>
        await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Username == username && u.IsActive, cancellationToken);

    private async Task RequireMediaAsync(Guid mediaId, CancellationToken cancellationToken)
    {
        if (!await _db.MediaItems.AsNoTracking().AnyAsync(m => m.Id == mediaId, cancellationToken))
            throw new InvalidOperationException("Media not found.");
    }
}

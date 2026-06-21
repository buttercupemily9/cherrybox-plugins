namespace CherryBox.Plugins.Abstractions;

public enum MediaVoteValue
{
    None = 0,
    Like = 1,
    Dislike = -1
}

public sealed record MediaSocialStatsDto(
    int LikeCount,
    int DislikeCount,
    int CommentCount,
    int? UserRating,
    MediaVoteValue UserVote);

public sealed record MediaCommentDto(
    Guid Id,
    Guid UserId,
    string Username,
    string Body,
    DateTimeOffset CreatedAt);

public sealed record ProfileCommentDto(
    Guid Id,
    Guid AuthorUserId,
    string AuthorUsername,
    string Body,
    DateTimeOffset CreatedAt);

public sealed record SocialSelfProfileDto(
    string Username,
    string BioMarkdown,
    bool LikesPublic);

public sealed record UpdateSocialProfileRequest(
    string BioMarkdown,
    bool LikesPublic);

public sealed record SocialUserProfileDto(
    Guid UserId,
    string Username,
    string BioMarkdown,
    bool LikesPublic,
    bool IsSelf,
    bool IsFriend,
    bool FriendRequestOutgoing,
    bool FriendRequestIncoming,
    int FriendCount);

public sealed record SocialUserSearchResult(
    Guid UserId,
    string Username,
    bool IsFriend);

public sealed record FriendListItemDto(
    Guid UserId,
    string Username,
    bool PendingIncoming,
    bool PendingOutgoing);

public sealed record MessageDto(
    Guid Id,
    Guid FromUserId,
    string FromUsername,
    Guid ToUserId,
    string ToUsername,
    string Body,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt);

public sealed record MessageThreadDto(
    Guid PartnerUserId,
    string PartnerUsername,
    string LastMessagePreview,
    DateTimeOffset LastMessageAt,
    int UnreadCount);

public sealed record PublicLikeDto(
    Guid MediaId,
    string Title,
    string MediaType,
    DateTimeOffset LikedAt);

public sealed record SetMediaVoteRequest(int Vote);

public sealed record AddCommentRequest(string Body);

public sealed record SendMessageRequest(string ToUsername, string Body);

public interface ISocialService
{
    Task<SocialSelfProfileDto> GetSelfProfileAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<SocialSelfProfileDto> UpdateSelfProfileAsync(Guid userId, UpdateSocialProfileRequest request, CancellationToken cancellationToken = default);
    Task<SocialUserProfileDto?> GetUserProfileAsync(Guid viewerId, string username, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SocialUserSearchResult>> SearchUsersAsync(Guid viewerId, string query, CancellationToken cancellationToken = default);
    Task<MediaSocialStatsDto> GetMediaStatsAsync(Guid userId, Guid mediaId, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<Guid, MediaSocialStatsDto>> GetMediaStatsBatchAsync(Guid userId, IReadOnlyList<Guid> mediaIds, CancellationToken cancellationToken = default);
    Task<MediaSocialStatsDto> SetMediaVoteAsync(Guid userId, Guid mediaId, int vote, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MediaCommentDto>> ListMediaCommentsAsync(Guid mediaId, CancellationToken cancellationToken = default);
    Task<MediaCommentDto> AddMediaCommentAsync(Guid userId, Guid mediaId, string body, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProfileCommentDto>> ListProfileCommentsAsync(string username, CancellationToken cancellationToken = default);
    Task<ProfileCommentDto> AddProfileCommentAsync(Guid authorId, string targetUsername, string body, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FriendListItemDto>> ListFriendsAsync(Guid userId, CancellationToken cancellationToken = default);
    Task SendFriendRequestAsync(Guid userId, string targetUsername, CancellationToken cancellationToken = default);
    Task AcceptFriendRequestAsync(Guid userId, string requesterUsername, CancellationToken cancellationToken = default);
    Task RemoveFriendAsync(Guid userId, string friendUsername, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MessageThreadDto>> ListMessageThreadsAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MessageDto>> ListMessagesAsync(Guid userId, string withUsername, CancellationToken cancellationToken = default);
    Task<MessageDto> SendMessageAsync(Guid userId, string toUsername, string body, CancellationToken cancellationToken = default);
    Task<int> GetUnreadMessageCountAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PublicLikeDto>> ListPublicLikesAsync(Guid viewerId, string username, CancellationToken cancellationToken = default);
}

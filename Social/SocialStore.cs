using Microsoft.Data.Sqlite;

namespace CherryBox.Social.Plugin;

public sealed class SocialStore
{
    private readonly string _connectionString;

    public SocialStore(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        var dbPath = Path.Combine(dataDirectory, "social.db");
        _connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ConnectionString;
        EnsureSchema();
    }

    public async Task<(string BioMarkdown, bool LikesPublic)> GetProfileAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT bio_markdown, likes_public FROM user_profiles WHERE user_id = $userId";
        command.Parameters.AddWithValue("$userId", userId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return (string.Empty, false);

        return (reader.GetString(0), reader.GetInt32(1) != 0);
    }

    public async Task UpsertProfileAsync(Guid userId, string bioMarkdown, bool likesPublic, CancellationToken cancellationToken)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO user_profiles (user_id, bio_markdown, likes_public)
            VALUES ($userId, $bio, $likesPublic)
            ON CONFLICT(user_id) DO UPDATE SET
              bio_markdown = excluded.bio_markdown,
              likes_public = excluded.likes_public
            """;
        command.Parameters.AddWithValue("$userId", userId.ToString());
        command.Parameters.AddWithValue("$bio", bioMarkdown ?? string.Empty);
        command.Parameters.AddWithValue("$likesPublic", likesPublic ? 1 : 0);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int?> GetUserVoteAsync(Guid userId, Guid mediaId, CancellationToken cancellationToken)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT vote FROM media_votes WHERE user_id = $userId AND media_id = $mediaId";
        command.Parameters.AddWithValue("$userId", userId.ToString());
        command.Parameters.AddWithValue("$mediaId", mediaId.ToString());
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null or DBNull ? null : Convert.ToInt32(result);
    }

    public async Task<(int Likes, int Dislikes)> GetVoteCountsAsync(Guid mediaId, CancellationToken cancellationToken)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
              COALESCE(SUM(CASE WHEN vote = 1 THEN 1 ELSE 0 END), 0),
              COALESCE(SUM(CASE WHEN vote = -1 THEN 1 ELSE 0 END), 0)
            FROM media_votes
            WHERE media_id = $mediaId
            """;
        command.Parameters.AddWithValue("$mediaId", mediaId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return (reader.GetInt32(0), reader.GetInt32(1));
    }

    public async Task SetVoteAsync(Guid userId, Guid mediaId, int vote, CancellationToken cancellationToken)
    {
        await using var connection = Open();
        if (vote == 0)
        {
            await using var delete = connection.CreateCommand();
            delete.CommandText = "DELETE FROM media_votes WHERE user_id = $userId AND media_id = $mediaId";
            delete.Parameters.AddWithValue("$userId", userId.ToString());
            delete.Parameters.AddWithValue("$mediaId", mediaId.ToString());
            await delete.ExecuteNonQueryAsync(cancellationToken);
            return;
        }

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO media_votes (user_id, media_id, vote, updated_at)
            VALUES ($userId, $mediaId, $vote, $updatedAt)
            ON CONFLICT(user_id, media_id) DO UPDATE SET vote = excluded.vote, updated_at = excluded.updated_at
            """;
        command.Parameters.AddWithValue("$userId", userId.ToString());
        command.Parameters.AddWithValue("$mediaId", mediaId.ToString());
        command.Parameters.AddWithValue("$vote", vote);
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> GetCommentCountAsync(Guid mediaId, CancellationToken cancellationToken)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM media_comments WHERE media_id = $mediaId";
        command.Parameters.AddWithValue("$mediaId", mediaId.ToString());
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<IReadOnlyList<(Guid Id, Guid UserId, string Body, DateTimeOffset CreatedAt)>> ListMediaCommentsAsync(
        Guid mediaId,
        CancellationToken cancellationToken)
    {
        var rows = new List<(Guid, Guid, string, DateTimeOffset)>();
        await using var connection = Open();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, user_id, body, created_at
            FROM media_comments
            WHERE media_id = $mediaId
            ORDER BY created_at ASC
            """;
        command.Parameters.AddWithValue("$mediaId", mediaId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add((
                Guid.Parse(reader.GetString(0)),
                Guid.Parse(reader.GetString(1)),
                reader.GetString(2),
                DateTimeOffset.Parse(reader.GetString(3))));
        }

        return rows;
    }

    public async Task<(Guid Id, Guid UserId, string Body, DateTimeOffset CreatedAt)> AddMediaCommentAsync(
        Guid userId,
        Guid mediaId,
        string body,
        CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow;
        await using var connection = Open();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO media_comments (id, user_id, media_id, body, created_at)
            VALUES ($id, $userId, $mediaId, $body, $createdAt)
            """;
        command.Parameters.AddWithValue("$id", id.ToString());
        command.Parameters.AddWithValue("$userId", userId.ToString());
        command.Parameters.AddWithValue("$mediaId", mediaId.ToString());
        command.Parameters.AddWithValue("$body", body);
        command.Parameters.AddWithValue("$createdAt", createdAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
        return (id, userId, body, createdAt);
    }

    public async Task<IReadOnlyList<(Guid Id, Guid AuthorUserId, string Body, DateTimeOffset CreatedAt)>> ListProfileCommentsAsync(
        Guid targetUserId,
        CancellationToken cancellationToken)
    {
        var rows = new List<(Guid, Guid, string, DateTimeOffset)>();
        await using var connection = Open();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, author_user_id, body, created_at
            FROM profile_comments
            WHERE target_user_id = $targetUserId
            ORDER BY created_at ASC
            """;
        command.Parameters.AddWithValue("$targetUserId", targetUserId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add((
                Guid.Parse(reader.GetString(0)),
                Guid.Parse(reader.GetString(1)),
                reader.GetString(2),
                DateTimeOffset.Parse(reader.GetString(3))));
        }

        return rows;
    }

    public async Task<(Guid Id, Guid AuthorUserId, string Body, DateTimeOffset CreatedAt)> AddProfileCommentAsync(
        Guid authorUserId,
        Guid targetUserId,
        string body,
        CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow;
        await using var connection = Open();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO profile_comments (id, author_user_id, target_user_id, body, created_at)
            VALUES ($id, $authorUserId, $targetUserId, $body, $createdAt)
            """;
        command.Parameters.AddWithValue("$id", id.ToString());
        command.Parameters.AddWithValue("$authorUserId", authorUserId.ToString());
        command.Parameters.AddWithValue("$targetUserId", targetUserId.ToString());
        command.Parameters.AddWithValue("$body", body);
        command.Parameters.AddWithValue("$createdAt", createdAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
        return (id, authorUserId, body, createdAt);
    }

    public async Task<string?> GetFriendshipStatusAsync(Guid requesterId, Guid addresseeId, CancellationToken cancellationToken)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT status FROM friendships
            WHERE (requester_id = $a AND addressee_id = $b)
               OR (requester_id = $b AND addressee_id = $a)
            LIMIT 1
            """;
        command.Parameters.AddWithValue("$a", requesterId.ToString());
        command.Parameters.AddWithValue("$b", addresseeId.ToString());
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result as string;
    }

    public async Task UpsertFriendshipAsync(Guid requesterId, Guid addresseeId, string status, CancellationToken cancellationToken)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO friendships (requester_id, addressee_id, status, created_at)
            VALUES ($requesterId, $addresseeId, $status, $createdAt)
            ON CONFLICT(requester_id, addressee_id) DO UPDATE SET status = excluded.status
            """;
        command.Parameters.AddWithValue("$requesterId", requesterId.ToString());
        command.Parameters.AddWithValue("$addresseeId", addresseeId.ToString());
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteFriendshipAsync(Guid userA, Guid userB, CancellationToken cancellationToken)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            DELETE FROM friendships
            WHERE (requester_id = $a AND addressee_id = $b)
               OR (requester_id = $b AND addressee_id = $a)
            """;
        command.Parameters.AddWithValue("$a", userA.ToString());
        command.Parameters.AddWithValue("$b", userB.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<(Guid RequesterId, Guid AddresseeId, string Status)>> ListFriendshipsForUserAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var rows = new List<(Guid, Guid, string)>();
        await using var connection = Open();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT requester_id, addressee_id, status
            FROM friendships
            WHERE requester_id = $userId OR addressee_id = $userId
            """;
        command.Parameters.AddWithValue("$userId", userId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add((Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)), reader.GetString(2)));
        }

        return rows;
    }

    public async Task<int> CountAcceptedFriendsAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COUNT(*) FROM friendships
            WHERE status = 'accepted' AND (requester_id = $userId OR addressee_id = $userId)
            """;
        command.Parameters.AddWithValue("$userId", userId.ToString());
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<(Guid Id, Guid FromUserId, Guid ToUserId, string Body, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt)> AddMessageAsync(
        Guid fromUserId,
        Guid toUserId,
        string body,
        CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow;
        await using var connection = Open();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO messages (id, from_user_id, to_user_id, body, read_at, created_at)
            VALUES ($id, $fromUserId, $toUserId, $body, NULL, $createdAt)
            """;
        command.Parameters.AddWithValue("$id", id.ToString());
        command.Parameters.AddWithValue("$fromUserId", fromUserId.ToString());
        command.Parameters.AddWithValue("$toUserId", toUserId.ToString());
        command.Parameters.AddWithValue("$body", body);
        command.Parameters.AddWithValue("$createdAt", createdAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
        return (id, fromUserId, toUserId, body, createdAt, null);
    }

    public async Task MarkMessagesReadAsync(Guid readerId, Guid partnerId, CancellationToken cancellationToken)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE messages
            SET read_at = $readAt
            WHERE to_user_id = $readerId AND from_user_id = $partnerId AND read_at IS NULL
            """;
        command.Parameters.AddWithValue("$readAt", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$readerId", readerId.ToString());
        command.Parameters.AddWithValue("$partnerId", partnerId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<(Guid Id, Guid FromUserId, Guid ToUserId, string Body, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt)>> ListMessagesBetweenAsync(
        Guid userA,
        Guid userB,
        CancellationToken cancellationToken)
    {
        var rows = new List<(Guid, Guid, Guid, string, DateTimeOffset, DateTimeOffset?)>();
        await using var connection = Open();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, from_user_id, to_user_id, body, created_at, read_at
            FROM messages
            WHERE (from_user_id = $a AND to_user_id = $b) OR (from_user_id = $b AND to_user_id = $a)
            ORDER BY created_at ASC
            """;
        command.Parameters.AddWithValue("$a", userA.ToString());
        command.Parameters.AddWithValue("$b", userB.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add((
                Guid.Parse(reader.GetString(0)),
                Guid.Parse(reader.GetString(1)),
                Guid.Parse(reader.GetString(2)),
                reader.GetString(3),
                DateTimeOffset.Parse(reader.GetString(4)),
                reader.IsDBNull(5) ? null : DateTimeOffset.Parse(reader.GetString(5))));
        }

        return rows;
    }

    public async Task<IReadOnlyList<(Guid Id, Guid FromUserId, Guid ToUserId, string Body, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt)>> ListMessagesForUserAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var rows = new List<(Guid, Guid, Guid, string, DateTimeOffset, DateTimeOffset?)>();
        await using var connection = Open();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, from_user_id, to_user_id, body, created_at, read_at
            FROM messages
            WHERE from_user_id = $userId OR to_user_id = $userId
            ORDER BY created_at DESC
            """;
        command.Parameters.AddWithValue("$userId", userId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add((
                Guid.Parse(reader.GetString(0)),
                Guid.Parse(reader.GetString(1)),
                Guid.Parse(reader.GetString(2)),
                reader.GetString(3),
                DateTimeOffset.Parse(reader.GetString(4)),
                reader.IsDBNull(5) ? null : DateTimeOffset.Parse(reader.GetString(5))));
        }

        return rows;
    }

    public async Task<int> GetUnreadCountAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM messages WHERE to_user_id = $userId AND read_at IS NULL";
        command.Parameters.AddWithValue("$userId", userId.ToString());
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<IReadOnlyList<(Guid MediaId, DateTimeOffset LikedAt)>> ListLikesByUserAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var rows = new List<(Guid, DateTimeOffset)>();
        await using var connection = Open();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT media_id, updated_at
            FROM media_votes
            WHERE user_id = $userId AND vote = 1
            ORDER BY updated_at DESC
            """;
        command.Parameters.AddWithValue("$userId", userId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add((Guid.Parse(reader.GetString(0)), DateTimeOffset.Parse(reader.GetString(1))));
        }

        return rows;
    }

    private void EnsureSchema()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS user_profiles (
                user_id TEXT NOT NULL PRIMARY KEY,
                bio_markdown TEXT NOT NULL DEFAULT '',
                likes_public INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS media_votes (
                user_id TEXT NOT NULL,
                media_id TEXT NOT NULL,
                vote INTEGER NOT NULL,
                updated_at TEXT NOT NULL,
                PRIMARY KEY (user_id, media_id)
            );

            CREATE TABLE IF NOT EXISTS media_comments (
                id TEXT NOT NULL PRIMARY KEY,
                user_id TEXT NOT NULL,
                media_id TEXT NOT NULL,
                body TEXT NOT NULL,
                created_at TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_media_comments_media ON media_comments(media_id);

            CREATE TABLE IF NOT EXISTS profile_comments (
                id TEXT NOT NULL PRIMARY KEY,
                author_user_id TEXT NOT NULL,
                target_user_id TEXT NOT NULL,
                body TEXT NOT NULL,
                created_at TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_profile_comments_target ON profile_comments(target_user_id);

            CREATE TABLE IF NOT EXISTS friendships (
                requester_id TEXT NOT NULL,
                addressee_id TEXT NOT NULL,
                status TEXT NOT NULL,
                created_at TEXT NOT NULL,
                PRIMARY KEY (requester_id, addressee_id)
            );

            CREATE TABLE IF NOT EXISTS messages (
                id TEXT NOT NULL PRIMARY KEY,
                from_user_id TEXT NOT NULL,
                to_user_id TEXT NOT NULL,
                body TEXT NOT NULL,
                read_at TEXT,
                created_at TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_messages_to_user ON messages(to_user_id, read_at);
            """;
        command.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }
}

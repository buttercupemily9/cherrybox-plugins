using Microsoft.Data.Sqlite;

namespace CherryBox.Achievements.Plugin;

public sealed class AchievementStore
{
    private readonly string _connectionString;

    public AchievementStore(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        var dbPath = Path.Combine(dataDirectory, "achievements.db");
        _connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ConnectionString;
        EnsureSchema();
    }

    public async Task<(int Plays, int StoryViews)> GetCountersAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT plays, story_views
            FROM user_stats
            WHERE user_id = $userId
            """;
        command.Parameters.AddWithValue("$userId", userId.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return (0, 0);

        return (reader.GetInt32(0), reader.GetInt32(1));
    }

    public async Task IncrementPlaysAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO user_stats (user_id, plays, story_views)
            VALUES ($userId, 1, 0)
            ON CONFLICT(user_id) DO UPDATE SET plays = plays + 1
            """;
        command.Parameters.AddWithValue("$userId", userId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task IncrementStoryViewsAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO user_stats (user_id, plays, story_views)
            VALUES ($userId, 0, 1)
            ON CONFLICT(user_id) DO UPDATE SET story_views = story_views + 1
            """;
        command.Parameters.AddWithValue("$userId", userId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, DateTimeOffset>> GetUnlockedAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var unlocked = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
        await using var connection = Open();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT achievement_id, unlocked_at
            FROM user_achievements
            WHERE user_id = $userId
            """;
        command.Parameters.AddWithValue("$userId", userId.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            unlocked[reader.GetString(0)] = DateTimeOffset.Parse(reader.GetString(1));
        }

        return unlocked;
    }

    public async Task UnlockAsync(Guid userId, string achievementId, DateTimeOffset unlockedAt, CancellationToken cancellationToken)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT OR IGNORE INTO user_achievements (user_id, achievement_id, unlocked_at)
            VALUES ($userId, $achievementId, $unlockedAt)
            """;
        command.Parameters.AddWithValue("$userId", userId.ToString());
        command.Parameters.AddWithValue("$achievementId", achievementId);
        command.Parameters.AddWithValue("$unlockedAt", unlockedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private void EnsureSchema()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS user_stats (
                user_id TEXT NOT NULL PRIMARY KEY,
                plays INTEGER NOT NULL DEFAULT 0,
                story_views INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS user_achievements (
                user_id TEXT NOT NULL,
                achievement_id TEXT NOT NULL,
                unlocked_at TEXT NOT NULL,
                PRIMARY KEY (user_id, achievement_id)
            );
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

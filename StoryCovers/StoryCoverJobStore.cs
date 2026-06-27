using CherryBox.Plugins.Abstractions;
using Microsoft.Data.Sqlite;

namespace CherryBox.StoryCovers.Plugin;

internal sealed class StoryCoverJobStore
{
    private readonly string _connectionString;
    private readonly object _lock = new();

    public StoryCoverJobStore(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        var dbPath = Path.Combine(dataDirectory, "story-cover-jobs.db");
        _connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ConnectionString;
        EnsureSchema();
    }

    public StoryCoverJobDto Add(Guid storyMediaItemId, string? storyTitle)
    {
        lock (_lock)
        {
            if (HasActiveJobForStory(storyMediaItemId))
                throw new InvalidOperationException("A cover job is already queued for this story.");

            var job = new StoryCoverJobDto(
                Guid.NewGuid(),
                storyMediaItemId,
                StoryCoverJobStatus.Pending,
                storyTitle,
                null,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                null);
            Insert(job);
            return job;
        }
    }

    public bool HasActiveJobForStory(Guid storyMediaItemId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(1) FROM StoryCoverJobs
            WHERE StoryMediaItemId = $storyId
              AND Status IN ('Pending', 'Processing')
            """;
        cmd.Parameters.AddWithValue("$storyId", storyMediaItemId.ToString());
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    public StoryCoverJobDto? ClaimNextPending()
    {
        lock (_lock)
        {
            using var conn = Open();
            using var select = conn.CreateCommand();
            select.CommandText = """
                SELECT Id FROM StoryCoverJobs
                WHERE Status = 'Pending'
                ORDER BY CreatedAt
                LIMIT 1
                """;
            var idObj = select.ExecuteScalar();
            if (idObj is null or DBNull) return null;
            var id = Guid.Parse(idObj.ToString()!);
            using var update = conn.CreateCommand();
            update.CommandText = """
                UPDATE StoryCoverJobs
                SET Status = 'Processing', UpdatedAt = $now
                WHERE Id = $id AND Status = 'Pending'
                """;
            update.Parameters.AddWithValue("$id", id.ToString());
            update.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            if (update.ExecuteNonQuery() != 1) return null;
            return GetById(id);
        }
    }

    public void Update(StoryCoverJobDto job)
    {
        lock (_lock)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE StoryCoverJobs SET
                    Status = $status,
                    StoryTitle = $title,
                    ErrorMessage = $error,
                    UpdatedAt = $updated,
                    CompletedAt = $completed
                WHERE Id = $id
                """;
            Bind(cmd, job);
            cmd.ExecuteNonQuery();
        }
    }

    public bool Cancel(Guid jobId)
    {
        lock (_lock)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE StoryCoverJobs
                SET Status = 'Cancelled', UpdatedAt = $now, CompletedAt = $now
                WHERE Id = $id AND Status IN ('Pending', 'Processing')
                """;
            cmd.Parameters.AddWithValue("$id", jobId.ToString());
            cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            return cmd.ExecuteNonQuery() == 1;
        }
    }

    public IReadOnlyList<StoryCoverJobDto> List(int limit)
    {
        lock (_lock)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT Id, StoryMediaItemId, Status, StoryTitle, ErrorMessage, CreatedAt, UpdatedAt, CompletedAt
                FROM StoryCoverJobs
                ORDER BY CreatedAt DESC
                LIMIT $limit
                """;
            cmd.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 500));
            using var reader = cmd.ExecuteReader();
            var list = new List<StoryCoverJobDto>();
            while (reader.Read())
                list.Add(ReadRow(reader));
            return list;
        }
    }

    public int CountPending()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM StoryCoverJobs WHERE Status = 'Pending'";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public int CountFailed()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM StoryCoverJobs WHERE Status = 'Failed'";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private void Insert(StoryCoverJobDto job)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO StoryCoverJobs (
                Id, StoryMediaItemId, Status, StoryTitle, ErrorMessage, CreatedAt, UpdatedAt, CompletedAt
            ) VALUES (
                $id, $storyId, $status, $title, $error, $created, $updated, $completed
            )
            """;
        Bind(cmd, job);
        cmd.ExecuteNonQuery();
    }

    private StoryCoverJobDto? GetById(Guid id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id, StoryMediaItemId, Status, StoryTitle, ErrorMessage, CreatedAt, UpdatedAt, CompletedAt
            FROM StoryCoverJobs WHERE Id = $id
            """;
        cmd.Parameters.AddWithValue("$id", id.ToString());
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? ReadRow(reader) : null;
    }

    private static void Bind(SqliteCommand cmd, StoryCoverJobDto job)
    {
        cmd.Parameters.AddWithValue("$id", job.Id.ToString());
        cmd.Parameters.AddWithValue("$storyId", job.StoryMediaItemId.ToString());
        cmd.Parameters.AddWithValue("$status", job.Status.ToString());
        cmd.Parameters.AddWithValue("$title", (object?)job.StoryTitle ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$error", (object?)job.ErrorMessage ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$created", job.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$updated", job.UpdatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$completed", job.CompletedAt?.ToString("O") ?? (object)DBNull.Value);
    }

    private static StoryCoverJobDto ReadRow(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)),
        Guid.Parse(reader.GetString(1)),
        Enum.Parse<StoryCoverJobStatus>(reader.GetString(2)),
        reader.IsDBNull(3) ? null : reader.GetString(3),
        reader.IsDBNull(4) ? null : reader.GetString(4),
        DateTimeOffset.Parse(reader.GetString(5)),
        DateTimeOffset.Parse(reader.GetString(6)),
        reader.IsDBNull(7) ? null : DateTimeOffset.Parse(reader.GetString(7)));

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

    private void EnsureSchema()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS StoryCoverJobs (
                Id TEXT PRIMARY KEY,
                StoryMediaItemId TEXT NOT NULL,
                Status TEXT NOT NULL,
                StoryTitle TEXT,
                ErrorMessage TEXT,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                CompletedAt TEXT
            );
            CREATE INDEX IF NOT EXISTS IX_StoryCoverJobs_Story ON StoryCoverJobs(StoryMediaItemId);
            CREATE INDEX IF NOT EXISTS IX_StoryCoverJobs_Status ON StoryCoverJobs(Status);
            """;
        cmd.ExecuteNonQuery();
    }
}

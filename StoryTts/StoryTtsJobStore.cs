using CherryBox.Plugins.Abstractions;
using Microsoft.Data.Sqlite;

namespace CherryBox.StoryTts.Plugin;

internal sealed class StoryTtsJobStore
{
    private readonly string _connectionString;
    private readonly object _lock = new();

    public StoryTtsJobStore(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        var dbPath = Path.Combine(dataDirectory, "story-tts-jobs.db");
        _connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ConnectionString;
        EnsureSchema();
    }

    public StoryTtsJobDto Add(Guid storyMediaItemId, string? storyTitle)
    {
        lock (_lock)
        {
            if (HasActiveJobForStory(storyMediaItemId))
                throw new InvalidOperationException("A text-to-speech job is already queued for this story.");

            var job = new StoryTtsJobDto(
                Guid.NewGuid(),
                storyMediaItemId,
                StoryTtsJobStatus.Pending,
                storyTitle,
                null,
                null,
                null,
                0,
                0,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                null,
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
            SELECT COUNT(1) FROM StoryTtsJobs
            WHERE StoryMediaItemId = $storyId
              AND Status IN ('Pending', 'Running')
            """;
        cmd.Parameters.AddWithValue("$storyId", storyMediaItemId.ToString());
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    public StoryTtsJobDto? ClaimNextPending()
    {
        lock (_lock)
        {
            using var conn = Open();
            using var select = conn.CreateCommand();
            select.CommandText = """
                SELECT Id FROM StoryTtsJobs
                WHERE Status = 'Pending'
                ORDER BY CreatedAt
                LIMIT 1
                """;
            var idObj = select.ExecuteScalar();
            if (idObj is null or DBNull) return null;
            var id = Guid.Parse(idObj.ToString()!);
            using var update = conn.CreateCommand();
            update.CommandText = """
                UPDATE StoryTtsJobs
                SET Status = 'Running', StartedAt = $now, UpdatedAt = $now
                WHERE Id = $id AND Status = 'Pending'
                """;
            update.Parameters.AddWithValue("$id", id.ToString());
            update.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            if (update.ExecuteNonQuery() != 1) return null;
            return GetById(id);
        }
    }

    public void Update(StoryTtsJobDto job)
    {
        lock (_lock)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE StoryTtsJobs SET
                    Status = $status,
                    OutputPath = $output,
                    AudioMediaItemId = $audioId,
                    ErrorMessage = $error,
                    ChunksTotal = $chunksTotal,
                    ChunksCompleted = $chunksCompleted,
                    UpdatedAt = $updated,
                    CompletedAt = $completed
                WHERE Id = $id
                """;
            cmd.Parameters.AddWithValue("$id", job.Id.ToString());
            cmd.Parameters.AddWithValue("$status", job.Status.ToString());
            cmd.Parameters.AddWithValue("$output", (object?)job.OutputPath ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$audioId", job.AudioMediaItemId?.ToString() ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$error", (object?)job.ErrorMessage ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$chunksTotal", job.ChunksTotal);
            cmd.Parameters.AddWithValue("$chunksCompleted", job.ChunksCompleted);
            cmd.Parameters.AddWithValue("$updated", job.UpdatedAt.ToString("O"));
            cmd.Parameters.AddWithValue("$completed", job.CompletedAt?.ToString("O") ?? (object)DBNull.Value);
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
                UPDATE StoryTtsJobs
                SET Status = 'Cancelled', UpdatedAt = $now, CompletedAt = $now
                WHERE Id = $id AND Status IN ('Pending', 'Running')
                """;
            cmd.Parameters.AddWithValue("$id", jobId.ToString());
            cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            return cmd.ExecuteNonQuery() == 1;
        }
    }

    public IReadOnlyList<StoryTtsJobDto> List(int limit)
    {
        lock (_lock)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT Id, StoryMediaItemId, Status, StoryTitle, OutputPath, AudioMediaItemId,
                       ErrorMessage, ChunksTotal, ChunksCompleted, CreatedAt, UpdatedAt, StartedAt, CompletedAt
                FROM StoryTtsJobs
                ORDER BY UpdatedAt DESC
                LIMIT $limit
                """;
            cmd.Parameters.AddWithValue("$limit", limit);
            using var reader = cmd.ExecuteReader();
            var list = new List<StoryTtsJobDto>();
            while (reader.Read())
                list.Add(ReadRow(reader));
            return list;
        }
    }

    public int CountPending()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM StoryTtsJobs WHERE Status = 'Pending'";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public int CountFailed()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM StoryTtsJobs WHERE Status = 'Failed'";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private void Insert(StoryTtsJobDto job)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO StoryTtsJobs (
                Id, StoryMediaItemId, Status, StoryTitle, OutputPath, AudioMediaItemId,
                ErrorMessage, ChunksTotal, ChunksCompleted, CreatedAt, UpdatedAt, StartedAt, CompletedAt)
            VALUES (
                $id, $storyId, $status, $title, $output, $audioId,
                $error, $chunksTotal, $chunksCompleted, $created, $updated, $started, $completed)
            """;
        Bind(cmd, job);
        cmd.ExecuteNonQuery();
    }

    private StoryTtsJobDto? GetById(Guid id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id, StoryMediaItemId, Status, StoryTitle, OutputPath, AudioMediaItemId,
                   ErrorMessage, ChunksTotal, ChunksCompleted, CreatedAt, UpdatedAt, StartedAt, CompletedAt
            FROM StoryTtsJobs WHERE Id = $id
            """;
        cmd.Parameters.AddWithValue("$id", id.ToString());
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? ReadRow(reader) : null;
    }

    private static void Bind(SqliteCommand cmd, StoryTtsJobDto job)
    {
        cmd.Parameters.AddWithValue("$id", job.Id.ToString());
        cmd.Parameters.AddWithValue("$storyId", job.StoryMediaItemId.ToString());
        cmd.Parameters.AddWithValue("$status", job.Status.ToString());
        cmd.Parameters.AddWithValue("$title", (object?)job.StoryTitle ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$output", (object?)job.OutputPath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$audioId", job.AudioMediaItemId?.ToString() ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$error", (object?)job.ErrorMessage ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$chunksTotal", job.ChunksTotal);
        cmd.Parameters.AddWithValue("$chunksCompleted", job.ChunksCompleted);
        cmd.Parameters.AddWithValue("$created", job.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$updated", job.UpdatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$started", job.StartedAt?.ToString("O") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$completed", job.CompletedAt?.ToString("O") ?? (object)DBNull.Value);
    }

    private static StoryTtsJobDto ReadRow(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)),
        Guid.Parse(reader.GetString(1)),
        Enum.Parse<StoryTtsJobStatus>(reader.GetString(2)),
        reader.IsDBNull(3) ? null : reader.GetString(3),
        reader.IsDBNull(4) ? null : reader.GetString(4),
        reader.IsDBNull(5) ? null : Guid.Parse(reader.GetString(5)),
        reader.IsDBNull(6) ? null : reader.GetString(6),
        reader.GetInt32(7),
        reader.GetInt32(8),
        DateTimeOffset.Parse(reader.GetString(9)),
        DateTimeOffset.Parse(reader.GetString(10)),
        reader.IsDBNull(11) ? null : DateTimeOffset.Parse(reader.GetString(11)),
        reader.IsDBNull(12) ? null : DateTimeOffset.Parse(reader.GetString(12)));

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
            CREATE TABLE IF NOT EXISTS StoryTtsJobs (
                Id TEXT PRIMARY KEY,
                StoryMediaItemId TEXT NOT NULL,
                Status TEXT NOT NULL,
                StoryTitle TEXT NULL,
                OutputPath TEXT NULL,
                AudioMediaItemId TEXT NULL,
                ErrorMessage TEXT NULL,
                ChunksTotal INTEGER NOT NULL DEFAULT 0,
                ChunksCompleted INTEGER NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                StartedAt TEXT NULL,
                CompletedAt TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_StoryTtsJobs_Story ON StoryTtsJobs(StoryMediaItemId);
            CREATE INDEX IF NOT EXISTS IX_StoryTtsJobs_Status ON StoryTtsJobs(Status);
            """;
        cmd.ExecuteNonQuery();
    }
}

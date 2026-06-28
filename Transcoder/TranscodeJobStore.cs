using CherryBox.Core.Enums;
using CherryBox.Plugins.Abstractions;
using Microsoft.Data.Sqlite;

namespace CherryBox.Transcoder.Plugin;

internal static class TranscodeJobDatabase
{
    public static readonly PluginDatabaseSchema Schema = new(
        "transcode-jobs",
        1,
        [
            new PluginSchemaMigrationStep(
                1,
                """
                CREATE TABLE IF NOT EXISTS TranscodeJobs (
                    Id TEXT PRIMARY KEY,
                    MediaItemId TEXT NOT NULL,
                    ProfileId TEXT NOT NULL,
                    Status TEXT NOT NULL,
                    MediaTitle TEXT NULL,
                    SourcePath TEXT NOT NULL,
                    OutputPath TEXT NULL,
                    ErrorMessage TEXT NULL,
                    BytesBefore INTEGER NULL,
                    BytesAfter INTEGER NULL,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL,
                    StartedAt TEXT NULL,
                    CompletedAt TEXT NULL
                );
                CREATE INDEX IF NOT EXISTS IX_TranscodeJobs_Status_CreatedAt ON TranscodeJobs(Status, CreatedAt);
                CREATE INDEX IF NOT EXISTS IX_TranscodeJobs_MediaItemId ON TranscodeJobs(MediaItemId);
                """)
        ]);
}

internal sealed class TranscodeJobStore
{
    private readonly string _connectionString;
    private readonly object _lock = new();

    public TranscodeJobStore(IPluginContext context)
    {
        var dbPath = context.GetDatabasePath("transcode-jobs");
        PluginDatabaseMigrator.Ensure(dbPath, TranscodeJobDatabase.Schema);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ConnectionString;
    }

    public TranscodeJobDto Add(Guid mediaItemId, Guid profileId, string? mediaTitle, string sourcePath)
    {
        lock (_lock)
        {
            if (HasActiveJobForMedia(mediaItemId))
                throw new InvalidOperationException("A transcode job is already queued for this media item.");

            var job = new TranscodeJobDto(
                Guid.NewGuid(),
                mediaItemId,
                profileId,
                TranscodeJobStatus.Pending,
                mediaTitle,
                sourcePath,
                null,
                null,
                null,
                null,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                null,
                null);
            Insert(job);
            return job;
        }
    }

    public bool HasActiveJobForMedia(Guid mediaItemId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(1) FROM TranscodeJobs
            WHERE MediaItemId = $mediaId
              AND Status IN ('Pending', 'Running')
            """;
        cmd.Parameters.AddWithValue("$mediaId", mediaItemId.ToString());
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    public TranscodeJobDto? ClaimNextPending()
    {
        lock (_lock)
        {
            using var conn = Open();
            using var select = conn.CreateCommand();
            select.CommandText = """
                SELECT Id FROM TranscodeJobs
                WHERE Status = 'Pending'
                ORDER BY CreatedAt
                LIMIT 1
                """;
            var idObj = select.ExecuteScalar();
            if (idObj is null or DBNull) return null;
            var id = Guid.Parse(idObj.ToString()!);
            using var update = conn.CreateCommand();
            update.CommandText = """
                UPDATE TranscodeJobs
                SET Status = 'Running', StartedAt = $now, UpdatedAt = $now
                WHERE Id = $id AND Status = 'Pending'
                """;
            update.Parameters.AddWithValue("$id", id.ToString());
            update.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            if (update.ExecuteNonQuery() != 1) return null;
            return GetById(id);
        }
    }

    public void Update(TranscodeJobDto job)
    {
        lock (_lock)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE TranscodeJobs SET
                    Status = $status,
                    OutputPath = $output,
                    ErrorMessage = $error,
                    BytesBefore = $before,
                    BytesAfter = $after,
                    UpdatedAt = $updated,
                    StartedAt = $started,
                    CompletedAt = $completed
                WHERE Id = $id
                """;
            cmd.Parameters.AddWithValue("$id", job.Id.ToString());
            cmd.Parameters.AddWithValue("$status", job.Status.ToString());
            cmd.Parameters.AddWithValue("$output", (object?)job.OutputPath ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$error", (object?)job.ErrorMessage ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$before", (object?)job.BytesBefore ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$after", (object?)job.BytesAfter ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$updated", job.UpdatedAt.ToString("O"));
            cmd.Parameters.AddWithValue("$started", (object?)job.StartedAt?.ToString("O") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$completed", (object?)job.CompletedAt?.ToString("O") ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<TranscodeJobDto> List(int limit)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT * FROM TranscodeJobs
            ORDER BY UpdatedAt DESC
            LIMIT $limit
            """;
        cmd.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 500));
        using var reader = cmd.ExecuteReader();
        var list = new List<TranscodeJobDto>();
        while (reader.Read())
            list.Add(Read(reader));
        return list;
    }

    public int CountPending()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM TranscodeJobs WHERE Status = 'Pending'";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public int CountFailed() =>
        CountByStatus(TranscodeJobStatus.Failed);

    public bool Cancel(Guid jobId)
    {
        lock (_lock)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE TranscodeJobs
                SET Status = 'Cancelled', UpdatedAt = $now, CompletedAt = $now
                WHERE Id = $id AND Status = 'Pending'
                """;
            cmd.Parameters.AddWithValue("$id", jobId.ToString());
            cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            return cmd.ExecuteNonQuery() == 1;
        }
    }

    public void ResetFailedToPending()
    {
        lock (_lock)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE TranscodeJobs
                SET Status = 'Pending', ErrorMessage = NULL, UpdatedAt = $now,
                    StartedAt = NULL, CompletedAt = NULL
                WHERE Status = 'Failed'
                """;
            cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();
        }
    }

    private int CountByStatus(TranscodeJobStatus status)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM TranscodeJobs WHERE Status = $status";
        cmd.Parameters.AddWithValue("$status", status.ToString());
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private TranscodeJobDto? GetById(Guid id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM TranscodeJobs WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    private void Insert(TranscodeJobDto job)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO TranscodeJobs (
                Id, MediaItemId, ProfileId, Status, MediaTitle, SourcePath,
                OutputPath, ErrorMessage, BytesBefore, BytesAfter,
                CreatedAt, UpdatedAt, StartedAt, CompletedAt
            ) VALUES (
                $id, $mediaId, $profileId, $status, $title, $source,
                $output, $error, $before, $after,
                $created, $updated, $started, $completed
            )
            """;
        cmd.Parameters.AddWithValue("$id", job.Id.ToString());
        cmd.Parameters.AddWithValue("$mediaId", job.MediaItemId.ToString());
        cmd.Parameters.AddWithValue("$profileId", job.ProfileId.ToString());
        cmd.Parameters.AddWithValue("$status", job.Status.ToString());
        cmd.Parameters.AddWithValue("$title", (object?)job.MediaTitle ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$source", job.SourcePath ?? string.Empty);
        cmd.Parameters.AddWithValue("$output", (object?)job.OutputPath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$error", (object?)job.ErrorMessage ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$before", (object?)job.BytesBefore ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$after", (object?)job.BytesAfter ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$created", job.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$updated", job.UpdatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$started", (object?)job.StartedAt?.ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$completed", (object?)job.CompletedAt?.ToString("O") ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private static TranscodeJobDto Read(SqliteDataReader reader) =>
        new(
            Guid.Parse(reader.GetString(0)),
            Guid.Parse(reader.GetString(1)),
            Guid.Parse(reader.GetString(2)),
            Enum.Parse<TranscodeJobStatus>(reader.GetString(3)),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetInt64(8),
            reader.IsDBNull(9) ? null : reader.GetInt64(9),
            DateTimeOffset.Parse(reader.GetString(10)),
            DateTimeOffset.Parse(reader.GetString(11)),
            reader.IsDBNull(12) ? null : DateTimeOffset.Parse(reader.GetString(12)),
            reader.IsDBNull(13) ? null : DateTimeOffset.Parse(reader.GetString(13)));

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }
}

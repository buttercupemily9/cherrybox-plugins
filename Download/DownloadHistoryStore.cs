using CherryBox.Core.Configuration;
using CherryBox.Core.Platform;
using CherryBox.Plugins.Abstractions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace CherryBox.Download.Plugin;

public interface IDownloadHistoryStore
{
    Task<DownloadHistoryEntry?> FindByUrlAsync(string normalizedUrl, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DownloadHistoryEntry>> ListAsync(int limit = 200, CancellationToken cancellationToken = default);
    Task RecordAsync(
        string normalizedUrl,
        string originalUrl,
        string? title,
        string? filePath,
        Guid? mediaItemId,
        CancellationToken cancellationToken = default,
        string? errorMessage = null,
        bool failed = false);
    Task UpdateMediaItemAsync(string normalizedUrl, Guid mediaItemId, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string normalizedUrl, CancellationToken cancellationToken = default);
}

public sealed class DownloadHistoryStore : IDownloadHistoryStore
{
    private readonly string _connectionString;
    private readonly ILogger<DownloadHistoryStore> _logger;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    public DownloadHistoryStore(IPlatformPaths paths, IConfigManager config, ILogger<DownloadHistoryStore> logger)
    {
        var fileName = config.Current.Download.HistoryDatabaseFileName;
        var dbPath = Path.Combine(paths.ProgramDataDirectory, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        _connectionString = $"Data Source={dbPath}";
        _logger = logger;
    }

    public async Task<DownloadHistoryEntry?> FindByUrlAsync(string normalizedUrl, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT NormalizedUrl, OriginalUrl, Title, FilePath, MediaItemId, DownloadedAt, Failed, ErrorMessage
            FROM DownloadHistory
            WHERE NormalizedUrl = $url
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$url", normalizedUrl);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return ReadEntry(reader);
    }

    public async Task<IReadOnlyList<DownloadHistoryEntry>> ListAsync(int limit = 200, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT NormalizedUrl, OriginalUrl, Title, FilePath, MediaItemId, DownloadedAt, Failed, ErrorMessage
            FROM DownloadHistory
            ORDER BY DownloadedAt DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);
        var results = new List<DownloadHistoryEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            results.Add(ReadEntry(reader));

        return results;
    }

    public async Task RecordAsync(
        string normalizedUrl,
        string originalUrl,
        string? title,
        string? filePath,
        Guid? mediaItemId,
        CancellationToken cancellationToken = default,
        string? errorMessage = null,
        bool failed = false)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO DownloadHistory (NormalizedUrl, OriginalUrl, Title, FilePath, MediaItemId, DownloadedAt, Failed, ErrorMessage)
            VALUES ($normalizedUrl, $originalUrl, $title, $filePath, $mediaItemId, $downloadedAt, $failed, $errorMessage)
            ON CONFLICT(NormalizedUrl) DO UPDATE SET
                OriginalUrl = excluded.OriginalUrl,
                Title = COALESCE(excluded.Title, DownloadHistory.Title),
                FilePath = CASE WHEN excluded.Failed = 1 THEN NULL ELSE COALESCE(excluded.FilePath, DownloadHistory.FilePath) END,
                MediaItemId = CASE WHEN excluded.Failed = 1 THEN NULL ELSE COALESCE(excluded.MediaItemId, DownloadHistory.MediaItemId) END,
                DownloadedAt = excluded.DownloadedAt,
                Failed = excluded.Failed,
                ErrorMessage = excluded.ErrorMessage;
            """;
        command.Parameters.AddWithValue("$normalizedUrl", normalizedUrl);
        command.Parameters.AddWithValue("$originalUrl", originalUrl);
        command.Parameters.AddWithValue("$title", (object?)title ?? DBNull.Value);
        command.Parameters.AddWithValue("$filePath", (object?)filePath ?? DBNull.Value);
        command.Parameters.AddWithValue("$mediaItemId", mediaItemId?.ToString() ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$downloadedAt", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$failed", failed ? 1 : 0);
        command.Parameters.AddWithValue("$errorMessage", (object?)errorMessage ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateMediaItemAsync(string normalizedUrl, Guid mediaItemId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE DownloadHistory
            SET MediaItemId = $mediaItemId
            WHERE NormalizedUrl = $normalizedUrl;
            """;
        command.Parameters.AddWithValue("$mediaItemId", mediaItemId.ToString());
        command.Parameters.AddWithValue("$normalizedUrl", normalizedUrl);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> DeleteAsync(string normalizedUrl, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM DownloadHistory WHERE NormalizedUrl = $normalizedUrl;";
        command.Parameters.AddWithValue("$normalizedUrl", normalizedUrl);
        var rows = await command.ExecuteNonQueryAsync(cancellationToken);
        return rows > 0;
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
            return;

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
                return;

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS DownloadHistory (
                    NormalizedUrl TEXT PRIMARY KEY NOT NULL,
                    OriginalUrl TEXT NOT NULL,
                    Title TEXT NULL,
                    FilePath TEXT NULL,
                    MediaItemId TEXT NULL,
                    DownloadedAt TEXT NOT NULL,
                    Failed INTEGER NOT NULL DEFAULT 0,
                    ErrorMessage TEXT NULL
                );
                CREATE INDEX IF NOT EXISTS IX_DownloadHistory_DownloadedAt ON DownloadHistory(DownloadedAt DESC);
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
            await EnsureColumnAsync(connection, "Failed", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
            await EnsureColumnAsync(connection, "ErrorMessage", "TEXT NULL", cancellationToken);
            _initialized = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize download history database.");
            throw;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private static async Task EnsureColumnAsync(
        SqliteConnection connection,
        string columnName,
        string columnDefinition,
        CancellationToken cancellationToken)
    {
        await using var check = connection.CreateCommand();
        check.CommandText = "SELECT 1 FROM pragma_table_info('DownloadHistory') WHERE name = $name LIMIT 1;";
        check.Parameters.AddWithValue("$name", columnName);
        var exists = await check.ExecuteScalarAsync(cancellationToken);
        if (exists is not null)
            return;

        await using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE DownloadHistory ADD COLUMN {columnName} {columnDefinition};";
        await alter.ExecuteNonQueryAsync(cancellationToken);
    }

    private static DownloadHistoryEntry ReadEntry(SqliteDataReader reader)
    {
        Guid? mediaItemId = null;
        if (!reader.IsDBNull(4) && Guid.TryParse(reader.GetString(4), out var parsed))
            mediaItemId = parsed;

        var failed = !reader.IsDBNull(6) && reader.GetInt64(6) != 0;
        var errorMessage = reader.IsDBNull(7) ? null : reader.GetString(7);

        return new DownloadHistoryEntry(
            reader.GetString(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            mediaItemId,
            DateTimeOffset.Parse(reader.GetString(5)),
            failed,
            errorMessage);
    }
}

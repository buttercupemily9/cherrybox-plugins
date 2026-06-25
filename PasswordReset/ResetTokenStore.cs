using System.Security.Cryptography;
using System.Text;

namespace CherryBox.PasswordReset.Plugin;

internal sealed class ResetTokenStore
{
    private readonly string _connectionString;

    public ResetTokenStore(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        _connectionString = $"Data Source={Path.Combine(dataDirectory, "reset-tokens.db")}";
        EnsureSchema();
    }

    public async Task StoreAsync(string tokenHash, Guid userId, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO ResetTokens (TokenHash, UserId, ExpiresAt, CreatedAt)
            VALUES ($hash, $userId, $expiresAt, $createdAt);
            """;
        command.Parameters.AddWithValue("$hash", tokenHash);
        command.Parameters.AddWithValue("$userId", userId.ToString());
        command.Parameters.AddWithValue("$expiresAt", expiresAt.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.UtcDateTime.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<Guid?> ConsumeAsync(string tokenHash, CancellationToken cancellationToken)
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var select = connection.CreateCommand();
        select.CommandText =
            """
            SELECT Id, UserId, ExpiresAt, UsedAt
            FROM ResetTokens
            WHERE TokenHash = $hash
            ORDER BY CreatedAt DESC
            LIMIT 1;
            """;
        select.Parameters.AddWithValue("$hash", tokenHash);

        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        var id = reader.GetInt64(0);
        var userId = Guid.Parse(reader.GetString(1));
        var expiresAt = DateTimeOffset.Parse(reader.GetString(2));
        var usedAt = reader.IsDBNull(3) ? (DateTimeOffset?)null : DateTimeOffset.Parse(reader.GetString(3));

        if (usedAt.HasValue || expiresAt < DateTimeOffset.UtcNow)
            return null;

        await using var update = connection.CreateCommand();
        update.CommandText = "UPDATE ResetTokens SET UsedAt = $usedAt WHERE Id = $id;";
        update.Parameters.AddWithValue("$usedAt", DateTimeOffset.UtcNow.UtcDateTime.ToString("O"));
        update.Parameters.AddWithValue("$id", id);
        await update.ExecuteNonQueryAsync(cancellationToken);
        return userId;
    }

    public async Task PurgeExpiredAsync(CancellationToken cancellationToken)
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM ResetTokens WHERE ExpiresAt < $now OR UsedAt IS NOT NULL;";
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.UtcDateTime.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private void EnsureSchema()
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS ResetTokens (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TokenHash TEXT NOT NULL,
                UserId TEXT NOT NULL,
                ExpiresAt TEXT NOT NULL,
                UsedAt TEXT NULL,
                CreatedAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_ResetTokens_TokenHash ON ResetTokens (TokenHash);
            """;
        command.ExecuteNonQuery();
    }

    public static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }
}

using Microsoft.Data.Sqlite;

namespace CherryBox.Newsletter.Plugin;

internal sealed class NewsletterSubscriptionStore
{
    private readonly string _connectionString;

    public NewsletterSubscriptionStore(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        _connectionString = $"Data Source={Path.Combine(dataDirectory, "subscriptions.db")}";
        EnsureSchema();
    }

    public async Task<bool> IsSubscribedAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Subscribed FROM Subscriptions WHERE UserId = $userId LIMIT 1;";
        command.Parameters.AddWithValue("$userId", userId.ToString());
        var result = await command.ExecuteScalarAsync(cancellationToken);
        if (result is null or DBNull)
            return true;

        return Convert.ToInt32(result) != 0;
    }

    public async Task SetSubscribedAsync(Guid userId, bool subscribed, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO Subscriptions (UserId, Subscribed, UpdatedAt)
            VALUES ($userId, $subscribed, $updatedAt)
            ON CONFLICT(UserId) DO UPDATE SET
                Subscribed = excluded.Subscribed,
                UpdatedAt = excluded.UpdatedAt;
            """;
        command.Parameters.AddWithValue("$userId", userId.ToString());
        command.Parameters.AddWithValue("$subscribed", subscribed ? 1 : 0);
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.UtcDateTime.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> ListSubscribedUserIdsAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<Guid>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT UserId FROM Subscriptions WHERE Subscribed = 1;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            results.Add(Guid.Parse(reader.GetString(0)));

        return results;
    }

    private void EnsureSchema()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS Subscriptions (
                UserId TEXT NOT NULL PRIMARY KEY,
                Subscribed INTEGER NOT NULL,
                UpdatedAt TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }
}

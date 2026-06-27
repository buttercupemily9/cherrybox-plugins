using CherryBox.Data;
using CherryBox.Plugins.Abstractions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CherryBox.Email.Plugin;

internal static class UserEmailDatabase
{
    public static readonly PluginDatabaseSchema Schema = new(
        "user-emails",
        1,
        [
            new PluginSchemaMigrationStep(
                1,
                """
                CREATE TABLE IF NOT EXISTS UserEmails (
                    UserId TEXT NOT NULL PRIMARY KEY,
                    Email TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL
                );
                CREATE UNIQUE INDEX IF NOT EXISTS IX_UserEmails_Email ON UserEmails (Email);
                """)
        ]);
}

internal sealed class UserEmailStore
{
    private readonly string _connectionString;
    private readonly string _dbPath;

    public UserEmailStore(IPluginContext context)
    {
        _dbPath = context.GetDatabasePath("user-emails");
        PluginDatabaseMigrator.Ensure(_dbPath, UserEmailDatabase.Schema);
        _connectionString = $"Data Source={_dbPath}";
    }

    public void EnsureSchema() =>
        PluginDatabaseMigrator.Ensure(_dbPath, UserEmailDatabase.Schema);

    public bool HasAnyEmails()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(1) FROM UserEmails;";
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }

    public async Task ImportLegacyEmailsAsync(CherryBoxDbContext db, CancellationToken cancellationToken = default)
    {
        if (!await LegacyEmailColumnExistsAsync(db, cancellationToken))
            return;

        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Email FROM Users WHERE Email IS NOT NULL AND TRIM(Email) <> '';";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var userId = Guid.Parse(reader.GetString(0));
            var email = reader.GetString(1);
            await SetAsync(userId, email, cancellationToken);
        }
    }

    public async Task<string?> GetAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Email FROM UserEmails WHERE UserId = $userId LIMIT 1;";
        command.Parameters.AddWithValue("$userId", userId.ToString());
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null or DBNull ? null : result.ToString();
    }

    public async Task<Guid?> FindUserIdByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeEmail(email);
        if (string.IsNullOrEmpty(normalized))
            return null;

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT UserId FROM UserEmails WHERE Email = $email LIMIT 1;";
        command.Parameters.AddWithValue("$email", normalized);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null or DBNull ? null : Guid.Parse(result.ToString()!);
    }

    public async Task SetAsync(Guid userId, string? email, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeEmail(email);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        if (string.IsNullOrEmpty(normalized))
        {
            await using var delete = connection.CreateCommand();
            delete.CommandText = "DELETE FROM UserEmails WHERE UserId = $userId;";
            delete.Parameters.AddWithValue("$userId", userId.ToString());
            await delete.ExecuteNonQueryAsync(cancellationToken);
            return;
        }

        var existingUserId = await FindUserIdByEmailAsync(normalized, cancellationToken);
        if (existingUserId.HasValue && existingUserId.Value != userId)
            throw new InvalidOperationException("Email is already in use.");

        await using var upsert = connection.CreateCommand();
        upsert.CommandText =
            """
            INSERT INTO UserEmails (UserId, Email, UpdatedAt)
            VALUES ($userId, $email, $updatedAt)
            ON CONFLICT(UserId) DO UPDATE SET
                Email = excluded.Email,
                UpdatedAt = excluded.UpdatedAt;
            """;
        upsert.Parameters.AddWithValue("$userId", userId.ToString());
        upsert.Parameters.AddWithValue("$email", normalized);
        upsert.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.UtcDateTime.ToString("O"));
        await upsert.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string NormalizeEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return string.Empty;

        return email.Trim().ToLowerInvariant();
    }

    private static async Task<bool> LegacyEmailColumnExistsAsync(
        CherryBoxDbContext db,
        CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(\"Users\");";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (string.Equals(reader.GetString(1), "Email", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}

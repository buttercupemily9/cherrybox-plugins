using Microsoft.Data.Sqlite;

namespace CherryBox.Plugins.Abstractions;

public sealed record PluginSchemaMigrationStep(int Version, string Sql);

/// <summary>
/// Versioned schema for a plugin-owned SQLite database. Bump <see cref="Version"/> and add steps when updating plugins.
/// </summary>
public sealed class PluginDatabaseSchema
{
    public PluginDatabaseSchema(string databaseName, int version, IEnumerable<PluginSchemaMigrationStep> migrations)
    {
        if (string.IsNullOrWhiteSpace(databaseName))
            throw new ArgumentException("Database name is required.", nameof(databaseName));
        if (version < 1)
            throw new ArgumentOutOfRangeException(nameof(version), "Schema version must be at least 1.");

        DatabaseName = databaseName.Trim();
        Version = version;
        Migrations = migrations
            .OrderBy(step => step.Version)
            .ToList();

        if (Migrations.Count == 0)
            throw new ArgumentException("At least one migration step is required.", nameof(migrations));
        if (Migrations[^1].Version != version)
            throw new ArgumentException("The highest migration step must match the schema version.");
    }

    public string DatabaseName { get; }
    public int Version { get; }
    public IReadOnlyList<PluginSchemaMigrationStep> Migrations { get; }
}

public static class PluginDatabaseMigrator
{
    public static void Ensure(string databasePath, PluginDatabaseSchema schema)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
            throw new ArgumentException("Database path is required.", nameof(databasePath));

        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ConnectionString);
        connection.Open();

        EnsureMetaTable(connection);
        var currentVersion = GetVersion(connection);

        foreach (var step in schema.Migrations)
        {
            if (step.Version <= currentVersion)
                continue;
            if (step.Version > schema.Version)
                break;

            using var command = connection.CreateCommand();
            command.CommandText = step.Sql;
            command.ExecuteNonQuery();
            SetVersion(connection, step.Version);
        }
    }

    private static void EnsureMetaTable(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS __plugin_schema (
                Id INTEGER NOT NULL PRIMARY KEY CHECK (Id = 1),
                Version INTEGER NOT NULL
            );
            INSERT OR IGNORE INTO __plugin_schema (Id, Version) VALUES (1, 0);
            """;
        command.ExecuteNonQuery();
    }

    private static int GetVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Version FROM __plugin_schema WHERE Id = 1;";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static void SetVersion(SqliteConnection connection, int version)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE __plugin_schema SET Version = $version WHERE Id = 1;";
        command.Parameters.AddWithValue("$version", version);
        command.ExecuteNonQuery();
    }
}

/// <summary>Optional: plugins implement this to apply database schema updates on load.</summary>
public interface IPluginSchemaContributor
{
    IReadOnlyList<PluginDatabaseSchema> GetDatabaseSchemas();
}

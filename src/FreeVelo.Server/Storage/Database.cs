using Microsoft.Data.Sqlite;

namespace FreeVelo.Server.Storage;

/// <summary>
/// The server's SQLite database. Data minimization: only riders (display name + secret hash) – no email, rides,
/// FTP or weight. Group rides live in memory only.
/// Schema changes are numbered migrations tracked with <c>PRAGMA user_version</c>.
/// </summary>
public sealed class Database
{
    private static readonly string[] Migrations =
    {
        // 1: riders – a display name and the hash of the device's secret (no email, no password)
        """
        CREATE TABLE riders (
            id              TEXT PRIMARY KEY,
            secret_hash     TEXT NOT NULL,
            display_name    TEXT NOT NULL,
            created_utc     TEXT NOT NULL,
            last_seen_utc   TEXT NOT NULL
        );
        """,
    };

    private readonly string _connectionString;

    public Database(string path)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
            Pooling = true,
        }.ToString();
        Migrate();
    }

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private void Migrate()
    {
        using var connection = Open();
        Execute(connection, "PRAGMA journal_mode = WAL;");
        long version = (long)Scalar(connection, "PRAGMA user_version;")!;
        for (int i = (int)version; i < Migrations.Length; i++)
        {
            using var transaction = connection.BeginTransaction();
            Execute(connection, Migrations[i], transaction);
            Execute(connection, $"PRAGMA user_version = {i + 1};", transaction);
            transaction.Commit();
        }
    }

    private static void Execute(SqliteConnection connection, string sql, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        command.ExecuteNonQuery();
    }

    private static object? Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }
}

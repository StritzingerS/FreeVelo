using Microsoft.Data.Sqlite;

namespace FreeVelo.Server.Storage;

/// <summary>
/// The server's SQLite database. Data minimization: only accounts and their tokens – no rides, FTP or weight.
/// Schema changes are numbered migrations tracked with <c>PRAGMA user_version</c>.
/// </summary>
public sealed class Database
{
    private static readonly string[] Migrations =
    {
        // 1: accounts, refresh tokens, one-time tokens (email verification, password reset)
        """
        CREATE TABLE accounts (
            id              TEXT PRIMARY KEY,
            email           TEXT NOT NULL UNIQUE,
            display_name    TEXT NOT NULL,
            password_hash   TEXT NOT NULL,
            email_verified  INTEGER NOT NULL DEFAULT 0,
            created_utc     TEXT NOT NULL
        );
        CREATE TABLE refresh_tokens (
            token_hash      TEXT PRIMARY KEY,
            account_id      TEXT NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
            family          TEXT NOT NULL,
            expires_utc     TEXT NOT NULL,
            revoked         INTEGER NOT NULL DEFAULT 0
        );
        CREATE INDEX refresh_tokens_account ON refresh_tokens(account_id);
        CREATE TABLE one_time_tokens (
            token_hash      TEXT PRIMARY KEY,
            account_id      TEXT NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
            purpose         TEXT NOT NULL,
            expires_utc     TEXT NOT NULL
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

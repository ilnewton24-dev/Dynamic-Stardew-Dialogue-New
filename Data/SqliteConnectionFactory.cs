using Microsoft.Data.Sqlite;
using System.Data;

namespace LivingLoreDialogue.Data;

public sealed class SqliteConnectionFactory
{
    private readonly string connectionString;

    public SqliteConnectionFactory(string databasePath)
    {
        this.connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            ForeignKeys = true
        }.ToString();
    }

    public SqliteConnection CreateConnection()
    {
        SqliteConnection connection = new SqliteConnection(this.connectionString);
        // Configure every connection on open:
        //   journal_mode=WAL  — allows concurrent readers + one writer; readers never block on writes.
        //   synchronous=NORMAL — safe with WAL; skips unnecessary full-sync flushes while preserving
        //                        crash safety at the WAL checkpoint boundary.
        //   busy_timeout=5000 — retry for up to 5 s on SQLITE_BUSY instead of throwing immediately;
        //                        prevents cascading failures when a long-running scan holds the write lock.
        // WAL mode persists in the database file header once set; subsequent connections inherit it.
        // Setting the PRAGMA every time is a harmless no-op after the first connection.
        connection.StateChange += static (sender, e) =>
        {
            if (e.CurrentState != ConnectionState.Open)
                return;
            using SqliteCommand cmd = ((SqliteConnection)sender).CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=5000;";
            cmd.ExecuteNonQuery();
        };
        return connection;
    }
}

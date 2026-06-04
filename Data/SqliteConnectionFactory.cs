using Microsoft.Data.Sqlite;

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
        return new SqliteConnection(this.connectionString);
    }
}

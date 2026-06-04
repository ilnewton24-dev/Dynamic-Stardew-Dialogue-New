using LivingLoreDialogue.Data;
using LivingLoreDialogue.Models;
using Microsoft.Data.Sqlite;

namespace LivingLoreDialogue.Repositories;

public sealed class AppSettingsRepository
{
    private readonly SqliteConnectionFactory connectionFactory;

    public AppSettingsRepository(SqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<LocalAppSetting>> GetAllAsync()
    {
        List<LocalAppSetting> settings = new();
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT Key, Value, LastModified FROM AppSettings ORDER BY Key;";

        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            settings.Add(new LocalAppSetting
            {
                Key = reader.GetString(0),
                Value = reader.IsDBNull(1) ? null : reader.GetString(1),
                LastModified = DateTime.Parse(reader.GetString(2))
            });
        }

        return settings;
    }

    public async Task SetAsync(string key, string? value)
    {
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO AppSettings (Key, Value, LastModified)
            VALUES (@key, @value, @lastModified)
            ON CONFLICT(Key) DO UPDATE SET
                Value = excluded.Value,
                LastModified = excluded.LastModified;
            ";
        command.Parameters.AddWithValue("@key", key);
        command.Parameters.AddWithValue("@value", (object?)value ?? DBNull.Value);
        command.Parameters.AddWithValue("@lastModified", DateTime.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync();
    }
}

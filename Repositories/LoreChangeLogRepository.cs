using LivingLoreDialogue.Data;
using LivingLoreDialogue.Models;
using Microsoft.Data.Sqlite;

namespace LivingLoreDialogue.Repositories;

public sealed class LoreChangeLogRepository
{
    private readonly SqliteConnectionFactory connectionFactory;

    public LoreChangeLogRepository(SqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task AddAsync(long characterId, string? sourceModId, string fieldChanged, string? oldValue, string? newValue, DateTime timestamp)
    {
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO LoreChangeLog (CharacterId, SourceModId, FieldChanged, OldValue, NewValue, Timestamp)
            VALUES (@characterId, @sourceModId, @fieldChanged, @oldValue, @newValue, @timestamp);
            ";
        command.Parameters.AddWithValue("@characterId", characterId);
        command.Parameters.AddWithValue("@sourceModId", (object?)sourceModId ?? DBNull.Value);
        command.Parameters.AddWithValue("@fieldChanged", fieldChanged);
        command.Parameters.AddWithValue("@oldValue", (object?)oldValue ?? DBNull.Value);
        command.Parameters.AddWithValue("@newValue", (object?)newValue ?? DBNull.Value);
        command.Parameters.AddWithValue("@timestamp", timestamp.ToString("O"));
        await command.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<LoreChangeLogEntry>> GetRecentForCharacterAsync(long characterId, int limit)
    {
        List<LoreChangeLogEntry> entries = new();
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            SELECT Id, CharacterId, SourceModId, FieldChanged, OldValue, NewValue, Timestamp
            FROM LoreChangeLog
            WHERE CharacterId = @characterId
            ORDER BY Timestamp DESC
            LIMIT @limit;
            ";
        command.Parameters.AddWithValue("@characterId", characterId);
        command.Parameters.AddWithValue("@limit", limit);

        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            entries.Add(new LoreChangeLogEntry
            {
                Id = reader.GetInt64(0),
                CharacterId = reader.GetInt64(1),
                SourceModId = reader.IsDBNull(2) ? null : reader.GetString(2),
                FieldChanged = reader.GetString(3),
                OldValue = reader.IsDBNull(4) ? null : reader.GetString(4),
                NewValue = reader.IsDBNull(5) ? null : reader.GetString(5),
                Timestamp = DateTime.Parse(reader.GetString(6))
            });
        }

        return entries;
    }

    public async Task<IReadOnlyList<LoreChangeLogEntry>> GetRecentAsync(int limit)
    {
        List<LoreChangeLogEntry> entries = new();
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            SELECT Id, CharacterId, SourceModId, FieldChanged, OldValue, NewValue, Timestamp
            FROM LoreChangeLog
            ORDER BY Timestamp DESC
            LIMIT @limit;
            ";
        command.Parameters.AddWithValue("@limit", limit);

        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            entries.Add(new LoreChangeLogEntry
            {
                Id = reader.GetInt64(0),
                CharacterId = reader.GetInt64(1),
                SourceModId = reader.IsDBNull(2) ? null : reader.GetString(2),
                FieldChanged = reader.GetString(3),
                OldValue = reader.IsDBNull(4) ? null : reader.GetString(4),
                NewValue = reader.IsDBNull(5) ? null : reader.GetString(5),
                Timestamp = DateTime.Parse(reader.GetString(6))
            });
        }

        return entries;
    }
}

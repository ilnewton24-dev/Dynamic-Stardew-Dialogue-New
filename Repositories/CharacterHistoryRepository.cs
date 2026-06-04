using LivingLoreDialogue.Data;
using LivingLoreDialogue.Models;
using Microsoft.Data.Sqlite;

namespace LivingLoreDialogue.Repositories;

public sealed class CharacterHistoryRepository
{
    private readonly SqliteConnectionFactory connectionFactory;

    public CharacterHistoryRepository(SqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task AddAsync(long characterId, string previousData, string newData, string changeReason, DateTime timestamp)
    {
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO CharacterHistory (CharacterId, Timestamp, PreviousData, NewData, ChangeReason)
            VALUES (@characterId, @timestamp, @previousData, @newData, @changeReason);
            ";
        command.Parameters.AddWithValue("@characterId", characterId);
        command.Parameters.AddWithValue("@timestamp", timestamp.ToString("O"));
        command.Parameters.AddWithValue("@previousData", previousData);
        command.Parameters.AddWithValue("@newData", newData);
        command.Parameters.AddWithValue("@changeReason", changeReason);
        await command.ExecuteNonQueryAsync();
    }
}

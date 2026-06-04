using LivingLoreDialogue.Data;
using LivingLoreDialogue.Models;
using Microsoft.Data.Sqlite;

namespace LivingLoreDialogue.Repositories;

public sealed class MemoryRepository
{
    private readonly SqliteConnectionFactory connectionFactory;

    public MemoryRepository(SqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<Memory>> GetRecentForCharacterAsync(long characterId, int limit)
    {
        List<Memory> memories = new();
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            SELECT Id, CharacterId, MemoryText, Importance, CreatedDate
            FROM Memories
            WHERE CharacterId = @characterId
            ORDER BY Importance DESC, CreatedDate DESC
            LIMIT @limit;
            ";
        command.Parameters.AddWithValue("@characterId", characterId);
        command.Parameters.AddWithValue("@limit", limit);

        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            memories.Add(new Memory
            {
                Id = reader.GetInt64(0),
                CharacterId = reader.GetInt64(1),
                MemoryText = reader.GetString(2),
                Importance = reader.GetInt32(3),
                CreatedDate = DateTime.Parse(reader.GetString(4))
            });
        }

        return memories;
    }

    public async Task<IReadOnlyList<Memory>> GetAllAsync()
    {
        List<Memory> memories = new();
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            SELECT Id, CharacterId, MemoryText, Importance, CreatedDate
            FROM Memories
            ORDER BY CreatedDate DESC;
            ";

        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            memories.Add(Map(reader));

        return memories;
    }

    public async Task<long> AddAsync(long characterId, string memoryText, int importance)
    {
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO Memories (CharacterId, MemoryText, Importance, CreatedDate)
            VALUES (@characterId, @memoryText, @importance, @createdDate);
            SELECT last_insert_rowid();
            ";
        command.Parameters.AddWithValue("@characterId", characterId);
        command.Parameters.AddWithValue("@memoryText", memoryText);
        command.Parameters.AddWithValue("@importance", importance);
        command.Parameters.AddWithValue("@createdDate", DateTime.UtcNow.ToString("O"));
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    public async Task UpdateAsync(long id, long characterId, string memoryText, int importance)
    {
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            UPDATE Memories
            SET CharacterId = @characterId,
                MemoryText = @memoryText,
                Importance = @importance
            WHERE Id = @id;
            ";
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@characterId", characterId);
        command.Parameters.AddWithValue("@memoryText", memoryText);
        command.Parameters.AddWithValue("@importance", importance);
        await command.ExecuteNonQueryAsync();
    }

    private static Memory Map(SqliteDataReader reader)
    {
        return new Memory
        {
            Id = reader.GetInt64(0),
            CharacterId = reader.GetInt64(1),
            MemoryText = reader.GetString(2),
            Importance = reader.GetInt32(3),
            CreatedDate = DateTime.Parse(reader.GetString(4))
        };
    }
}

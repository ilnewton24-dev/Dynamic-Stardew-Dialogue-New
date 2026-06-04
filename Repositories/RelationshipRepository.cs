using LivingLoreDialogue.Data;
using LivingLoreDialogue.Models;
using Microsoft.Data.Sqlite;

namespace LivingLoreDialogue.Repositories;

public sealed class RelationshipRepository
{
    private readonly SqliteConnectionFactory connectionFactory;

    public RelationshipRepository(SqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<Relationship>> GetForCharacterAsync(long characterId)
    {
        List<Relationship> relationships = new();
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            SELECT Id, CharacterA, CharacterB, RelationshipType, Strength
            FROM Relationships
            WHERE CharacterA = @id OR CharacterB = @id
            ORDER BY Strength DESC;
            ";
        command.Parameters.AddWithValue("@id", characterId);

        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            relationships.Add(new Relationship
            {
                Id = reader.GetInt64(0),
                CharacterA = reader.GetInt64(1),
                CharacterB = reader.GetInt64(2),
                RelationshipType = reader.GetString(3),
                Strength = reader.GetInt32(4)
            });
        }

        return relationships;
    }

    public async Task<IReadOnlyList<Relationship>> GetAllAsync()
    {
        List<Relationship> relationships = new();
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            SELECT Id, CharacterA, CharacterB, RelationshipType, Strength
            FROM Relationships
            ORDER BY RelationshipType, Strength DESC;
            ";

        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            relationships.Add(Map(reader));

        return relationships;
    }

    public async Task<long> UpsertAsync(long characterA, long characterB, string relationshipType, int strength)
    {
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO Relationships (CharacterA, CharacterB, RelationshipType, Strength)
            VALUES (@characterA, @characterB, @relationshipType, @strength);
            SELECT last_insert_rowid();
            ";
        command.Parameters.AddWithValue("@characterA", characterA);
        command.Parameters.AddWithValue("@characterB", characterB);
        command.Parameters.AddWithValue("@relationshipType", relationshipType);
        command.Parameters.AddWithValue("@strength", strength);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    public async Task UpdateAsync(long id, long characterA, long characterB, string relationshipType, int strength)
    {
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            UPDATE Relationships
            SET CharacterA = @characterA,
                CharacterB = @characterB,
                RelationshipType = @relationshipType,
                Strength = @strength
            WHERE Id = @id;
            ";
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@characterA", characterA);
        command.Parameters.AddWithValue("@characterB", characterB);
        command.Parameters.AddWithValue("@relationshipType", relationshipType);
        command.Parameters.AddWithValue("@strength", strength);
        await command.ExecuteNonQueryAsync();
    }

    private static Relationship Map(SqliteDataReader reader)
    {
        return new Relationship
        {
            Id = reader.GetInt64(0),
            CharacterA = reader.GetInt64(1),
            CharacterB = reader.GetInt64(2),
            RelationshipType = reader.GetString(3),
            Strength = reader.GetInt32(4)
        };
    }
}

using LivingLoreDialogue.Data;
using LivingLoreDialogue.Models;
using Microsoft.Data.Sqlite;

namespace LivingLoreDialogue.Repositories;

public sealed class UserLoreOverrideRepository
{
    private readonly SqliteConnectionFactory connectionFactory;

    public UserLoreOverrideRepository(SqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<UserLoreOverride>> GetForCharacterAsync(long characterId)
    {
        List<UserLoreOverride> overrides = new();
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            SELECT Id, CharacterId, OverrideType, FieldName, OverrideValue, Notes, CreatedDate, LastModified
            FROM UserLoreOverrides
            WHERE CharacterId = @characterId
            ORDER BY LastModified DESC;
            ";
        command.Parameters.AddWithValue("@characterId", characterId);

        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            overrides.Add(new UserLoreOverride
            {
                Id = reader.GetInt64(0),
                CharacterId = reader.GetInt64(1),
                OverrideType = reader.GetString(2),
                FieldName = reader.GetString(3),
                OverrideValue = reader.GetString(4),
                Notes = reader.IsDBNull(5) ? null : reader.GetString(5),
                CreatedDate = DateTime.Parse(reader.GetString(6)),
                LastModified = DateTime.Parse(reader.GetString(7))
            });
        }

        return overrides;
    }

    public async Task<long> AddOrUpdateAsync(long characterId, string overrideType, string fieldName, string overrideValue, string? notes)
    {
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        string now = DateTime.UtcNow.ToString("O");
        await using SqliteCommand existingCommand = connection.CreateCommand();
        existingCommand.CommandText = @"
            SELECT Id FROM UserLoreOverrides
            WHERE CharacterId = @characterId AND OverrideType = @overrideType AND FieldName = @fieldName
            LIMIT 1;
            ";
        existingCommand.Parameters.AddWithValue("@characterId", characterId);
        existingCommand.Parameters.AddWithValue("@overrideType", overrideType);
        existingCommand.Parameters.AddWithValue("@fieldName", fieldName);
        object? existingId = await existingCommand.ExecuteScalarAsync();

        await using SqliteCommand command = connection.CreateCommand();
        if (existingId is not null)
        {
            command.CommandText = @"
                UPDATE UserLoreOverrides
                SET OverrideValue = @overrideValue,
                    Notes = @notes,
                    LastModified = @lastModified
                WHERE Id = @id;
                SELECT @id;
                ";
            command.Parameters.AddWithValue("@id", Convert.ToInt64(existingId));
        }
        else
        {
            command.CommandText = @"
                INSERT INTO UserLoreOverrides (CharacterId, OverrideType, FieldName, OverrideValue, Notes, CreatedDate, LastModified)
                VALUES (@characterId, @overrideType, @fieldName, @overrideValue, @notes, @createdDate, @lastModified);
                SELECT last_insert_rowid();
                ";
            command.Parameters.AddWithValue("@characterId", characterId);
            command.Parameters.AddWithValue("@overrideType", overrideType);
            command.Parameters.AddWithValue("@fieldName", fieldName);
            command.Parameters.AddWithValue("@createdDate", now);
        }

        command.Parameters.AddWithValue("@overrideValue", overrideValue);
        command.Parameters.AddWithValue("@notes", (object?)notes ?? DBNull.Value);
        command.Parameters.AddWithValue("@lastModified", now);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }
}

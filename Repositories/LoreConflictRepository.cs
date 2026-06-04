using LivingLoreDialogue.Data;
using LivingLoreDialogue.Models;
using Microsoft.Data.Sqlite;

namespace LivingLoreDialogue.Repositories;

public sealed class LoreConflictRepository
{
    private readonly SqliteConnectionFactory connectionFactory;

    public LoreConflictRepository(SqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<LoreConflict>> GetAllAsync()
    {
        List<LoreConflict> conflicts = new();
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            SELECT c.Id, c.CharacterId, ch.Name, c.SourceModId, c.FieldName, c.ModValue,
                   c.OverrideValue, c.IsReviewed, c.CreatedDate, c.ReviewedDate
            FROM LoreConflicts c
            INNER JOIN Characters ch ON ch.Id = c.CharacterId
            ORDER BY c.IsReviewed, c.CreatedDate DESC;
            ";

        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            conflicts.Add(new LoreConflict
            {
                Id = reader.GetInt64(0),
                CharacterId = reader.GetInt64(1),
                CharacterName = reader.GetString(2),
                SourceModId = reader.IsDBNull(3) ? null : reader.GetString(3),
                FieldName = reader.GetString(4),
                ModValue = reader.IsDBNull(5) ? null : reader.GetString(5),
                OverrideValue = reader.IsDBNull(6) ? null : reader.GetString(6),
                IsReviewed = reader.GetInt32(7) == 1,
                CreatedDate = DateTime.Parse(reader.GetString(8)),
                ReviewedDate = reader.IsDBNull(9) ? null : DateTime.Parse(reader.GetString(9))
            });
        }

        return conflicts;
    }

    public async Task MarkReviewedAsync(long id)
    {
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            UPDATE LoreConflicts
            SET IsReviewed = 1,
                ReviewedDate = @reviewedDate
            WHERE Id = @id;
            ";
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@reviewedDate", DateTime.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync();
    }

    public async Task<int> CountUnreviewedAsync()
    {
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM LoreConflicts WHERE IsReviewed = 0;";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    public async Task AddAsync(long characterId, string? sourceModId, string fieldName, string? modValue, string? overrideValue)
    {
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO LoreConflicts (CharacterId, SourceModId, FieldName, ModValue, OverrideValue, IsReviewed, CreatedDate)
            VALUES (@characterId, @sourceModId, @fieldName, @modValue, @overrideValue, 0, @createdDate);
            ";
        command.Parameters.AddWithValue("@characterId", characterId);
        command.Parameters.AddWithValue("@sourceModId", (object?)sourceModId ?? DBNull.Value);
        command.Parameters.AddWithValue("@fieldName", fieldName);
        command.Parameters.AddWithValue("@modValue", (object?)modValue ?? DBNull.Value);
        command.Parameters.AddWithValue("@overrideValue", (object?)overrideValue ?? DBNull.Value);
        command.Parameters.AddWithValue("@createdDate", DateTime.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync();
    }
}

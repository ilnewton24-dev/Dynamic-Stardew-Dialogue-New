using System.Text.Json;
using LivingLoreDialogue.Data;
using LivingLoreDialogue.Models;
using Microsoft.Data.Sqlite;

namespace LivingLoreDialogue.Repositories;

public sealed class CharacterValidationRepository
{
    private readonly SqliteConnectionFactory connectionFactory;

    public CharacterValidationRepository(SqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    /// <summary>Insert or update the validation result for a candidate, keyed by (mod, name).</summary>
    public async Task UpsertAsync(CharacterValidationResult result)
    {
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO CharacterValidationResults (
                Name, SourceModId, SourceModName, Score, Classification, Imported,
                Evidence, RulesJson, RawModData, LastSeen, UpdatedDate
            )
            VALUES (
                @name, @sourceModId, @sourceModName, @score, @classification, @imported,
                @evidence, @rulesJson, @rawModData, @lastSeen, @updatedDate
            )
            ON CONFLICT(SourceModId, Name) DO UPDATE SET
                SourceModName = excluded.SourceModName,
                Score = excluded.Score,
                Classification = excluded.Classification,
                Imported = excluded.Imported,
                Evidence = excluded.Evidence,
                RulesJson = excluded.RulesJson,
                RawModData = excluded.RawModData,
                LastSeen = excluded.LastSeen,
                UpdatedDate = excluded.UpdatedDate;
            ";
        command.Parameters.AddWithValue("@name", result.Name);
        command.Parameters.AddWithValue("@sourceModId", result.SourceModId);
        command.Parameters.AddWithValue("@sourceModName", (object?)result.SourceModName ?? DBNull.Value);
        command.Parameters.AddWithValue("@score", result.Score);
        command.Parameters.AddWithValue("@classification", result.Classification);
        command.Parameters.AddWithValue("@imported", result.Imported ? 1 : 0);
        command.Parameters.AddWithValue("@evidence", (int)result.Evidence);
        command.Parameters.AddWithValue("@rulesJson", JsonSerializer.Serialize(result.Rules));
        command.Parameters.AddWithValue("@rawModData", (object?)result.RawModData ?? DBNull.Value);
        command.Parameters.AddWithValue("@lastSeen", result.LastSeen.ToString("O"));
        command.Parameters.AddWithValue("@updatedDate", DateTime.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<CharacterValidationResult>> GetAllAsync()
    {
        List<CharacterValidationResult> results = new();
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            SELECT Id, Name, SourceModId, SourceModName, Score, Classification, Imported,
                   Evidence, RulesJson, RawModData, LastSeen
            FROM CharacterValidationResults
            ORDER BY Score DESC, Name;
            ";

        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            results.Add(Map(reader));

        return results;
    }

    private static CharacterValidationResult Map(SqliteDataReader reader)
    {
        string rulesJson = reader.IsDBNull(8) ? "[]" : reader.GetString(8);
        IReadOnlyList<ValidationRuleResult> rules =
            JsonSerializer.Deserialize<List<ValidationRuleResult>>(rulesJson) ?? new List<ValidationRuleResult>();

        return new CharacterValidationResult
        {
            Id = reader.GetInt64(0),
            Name = reader.GetString(1),
            SourceModId = reader.GetString(2),
            SourceModName = reader.IsDBNull(3) ? "" : reader.GetString(3),
            Score = reader.GetInt32(4),
            Classification = reader.GetString(5),
            Imported = reader.GetInt32(6) == 1,
            Evidence = (CharacterEvidence)reader.GetInt32(7),
            Rules = rules,
            RawModData = reader.IsDBNull(9) ? "" : reader.GetString(9),
            LastSeen = reader.IsDBNull(10) ? default : DateTime.Parse(reader.GetString(10))
        };
    }
}

using LivingLoreDialogue.Data;
using LivingLoreDialogue.Models;
using Microsoft.Data.Sqlite;

namespace LivingLoreDialogue.Repositories;

public sealed class GeneratedDialogueOverrideRepository
{
    private readonly SqliteConnectionFactory connectionFactory;

    public GeneratedDialogueOverrideRepository(SqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task<long> AddCandidateAsync(GeneratedDialogueOverride candidate)
    {
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        string now = DateTime.UtcNow.ToString("O");
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO GeneratedDialogueOverrides (
                CanonicalCharacterId, DialogueKey, OriginalDialogueSourceId, GeneratedText,
                PromptUsed, SaveContextSnapshot, IsEnabled, IsApproved, CreatedAt, UpdatedAt
            )
            VALUES (
                @canonicalCharacterId, @dialogueKey, @originalDialogueSourceId, @generatedText,
                @promptUsed, @saveContextSnapshot, @isEnabled, @isApproved, @now, @now
            );
            SELECT last_insert_rowid();
            ";
        command.Parameters.AddWithValue("@canonicalCharacterId", candidate.CanonicalCharacterId);
        command.Parameters.AddWithValue("@dialogueKey", candidate.DialogueKey);
        command.Parameters.AddWithValue("@originalDialogueSourceId", (object?)candidate.OriginalDialogueSourceId ?? DBNull.Value);
        command.Parameters.AddWithValue("@generatedText", candidate.GeneratedText);
        command.Parameters.AddWithValue("@promptUsed", candidate.PromptUsed);
        command.Parameters.AddWithValue("@saveContextSnapshot", candidate.SaveContextSnapshot);
        command.Parameters.AddWithValue("@isEnabled", candidate.IsEnabled ? 1 : 0);
        command.Parameters.AddWithValue("@isApproved", candidate.IsApproved ? 1 : 0);
        command.Parameters.AddWithValue("@now", now);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    public async Task<IReadOnlyList<GeneratedDialogueOverride>> GetAllAsync()
    {
        List<GeneratedDialogueOverride> overrides = new();
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            SELECT Id, CanonicalCharacterId, DialogueKey, OriginalDialogueSourceId, GeneratedText,
                   PromptUsed, SaveContextSnapshot, IsEnabled, IsApproved, CreatedAt, UpdatedAt
            FROM GeneratedDialogueOverrides
            ORDER BY CreatedAt DESC;
            ";

        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            overrides.Add(Map(reader));

        return overrides;
    }

    public async Task<IReadOnlyList<GeneratedDialogueOverride>> GetEnabledApprovedAsync()
    {
        List<GeneratedDialogueOverride> overrides = new();
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            SELECT Id, CanonicalCharacterId, DialogueKey, OriginalDialogueSourceId, GeneratedText,
                   PromptUsed, SaveContextSnapshot, IsEnabled, IsApproved, CreatedAt, UpdatedAt
            FROM GeneratedDialogueOverrides
            WHERE IsEnabled = 1 AND IsApproved = 1
            ORDER BY CanonicalCharacterId, DialogueKey, CreatedAt DESC;
            ";

        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            overrides.Add(Map(reader));

        return overrides;
    }

    public async Task SetApprovedAsync(long id, bool approved)
    {
        await this.SetFlagAsync(id, "IsApproved", approved);
    }

    public async Task SetEnabledAsync(long id, bool enabled)
    {
        await this.SetFlagAsync(id, "IsEnabled", enabled);
    }

    private async Task SetFlagAsync(long id, string fieldName, bool value)
    {
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"UPDATE GeneratedDialogueOverrides SET {fieldName} = @value, UpdatedAt = @updatedAt WHERE Id = @id;";
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@value", value ? 1 : 0);
        command.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync();
    }

    private static GeneratedDialogueOverride Map(SqliteDataReader reader)
    {
        return new GeneratedDialogueOverride
        {
            Id = reader.GetInt64(0),
            CanonicalCharacterId = reader.GetInt64(1),
            DialogueKey = reader.GetString(2),
            OriginalDialogueSourceId = reader.IsDBNull(3) ? null : reader.GetInt64(3),
            GeneratedText = reader.GetString(4),
            PromptUsed = reader.GetString(5),
            SaveContextSnapshot = reader.GetString(6),
            IsEnabled = reader.GetInt32(7) == 1,
            IsApproved = reader.GetInt32(8) == 1,
            CreatedAt = DateTime.Parse(reader.GetString(9)),
            UpdatedAt = DateTime.Parse(reader.GetString(10))
        };
    }
}

using LivingLoreDialogue.Data;
using LivingLoreDialogue.Models;
using Microsoft.Data.Sqlite;

namespace LivingLoreDialogue.Repositories;

public sealed class DialogueGenerationTraceRepository
{
    private readonly SqliteConnectionFactory connectionFactory;

    public DialogueGenerationTraceRepository(SqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task<long> AddAsync(DialogueGenerationTrace trace)
    {
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO DialogueGenerationTrace (
                GeneratedDialogueId, GeneratedAt, CharacterId, SaveContextSnapshot,
                MemoriesUsed, RelationshipsUsed, UserOverridesUsed, DialogueSourcesUsed,
                SourceModsUsed, PromptVersion, PromptText, ModelUsed,
                PlayerProfileUsed, PlayerRelationshipNotesUsed, PlayerMemoriesUsed, SaveFileLinkUsed
            )
            VALUES (
                @generatedDialogueId, @generatedAt, @characterId, @saveContextSnapshot,
                @memoriesUsed, @relationshipsUsed, @userOverridesUsed, @dialogueSourcesUsed,
                @sourceModsUsed, @promptVersion, @promptText, @modelUsed,
                @playerProfileUsed, @playerRelationshipNotesUsed, @playerMemoriesUsed, @saveFileLinkUsed
            );
            SELECT last_insert_rowid();
            ";
        command.Parameters.AddWithValue("@generatedDialogueId", trace.GeneratedDialogueId);
        command.Parameters.AddWithValue("@generatedAt", trace.GeneratedAt.ToString("O"));
        command.Parameters.AddWithValue("@characterId", trace.CharacterId);
        command.Parameters.AddWithValue("@saveContextSnapshot", trace.SaveContextSnapshot);
        command.Parameters.AddWithValue("@memoriesUsed", trace.MemoriesUsed);
        command.Parameters.AddWithValue("@relationshipsUsed", trace.RelationshipsUsed);
        command.Parameters.AddWithValue("@userOverridesUsed", trace.UserOverridesUsed);
        command.Parameters.AddWithValue("@dialogueSourcesUsed", trace.DialogueSourcesUsed);
        command.Parameters.AddWithValue("@sourceModsUsed", trace.SourceModsUsed);
        command.Parameters.AddWithValue("@promptVersion", trace.PromptVersion);
        command.Parameters.AddWithValue("@promptText", trace.PromptText);
        command.Parameters.AddWithValue("@modelUsed", trace.ModelUsed);
        command.Parameters.AddWithValue("@playerProfileUsed", trace.PlayerProfileUsed);
        command.Parameters.AddWithValue("@playerRelationshipNotesUsed", trace.PlayerRelationshipNotesUsed);
        command.Parameters.AddWithValue("@playerMemoriesUsed", trace.PlayerMemoriesUsed);
        command.Parameters.AddWithValue("@saveFileLinkUsed", (object?)trace.SaveFileLinkUsed ?? DBNull.Value);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    /// <summary>Returns the most recent trace for a generated dialogue line, or null if none exists.</summary>
    public async Task<DialogueGenerationTrace?> GetByGeneratedDialogueIdAsync(long generatedDialogueId)
    {
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            SELECT Id, GeneratedDialogueId, GeneratedAt, CharacterId, SaveContextSnapshot,
                   MemoriesUsed, RelationshipsUsed, UserOverridesUsed, DialogueSourcesUsed,
                   SourceModsUsed, PromptVersion, PromptText, ModelUsed,
                   PlayerProfileUsed, PlayerRelationshipNotesUsed, PlayerMemoriesUsed, SaveFileLinkUsed
            FROM DialogueGenerationTrace
            WHERE GeneratedDialogueId = @generatedDialogueId
            ORDER BY Id DESC
            LIMIT 1;
            ";
        command.Parameters.AddWithValue("@generatedDialogueId", generatedDialogueId);

        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Map(reader) : null;
    }

    private static DialogueGenerationTrace Map(SqliteDataReader reader)
    {
        return new DialogueGenerationTrace
        {
            Id = reader.GetInt64(0),
            GeneratedDialogueId = reader.GetInt64(1),
            GeneratedAt = DateTime.Parse(reader.GetString(2)),
            CharacterId = reader.GetInt64(3),
            SaveContextSnapshot = reader.GetString(4),
            MemoriesUsed = reader.GetString(5),
            RelationshipsUsed = reader.GetString(6),
            UserOverridesUsed = reader.GetString(7),
            DialogueSourcesUsed = reader.GetString(8),
            SourceModsUsed = reader.GetString(9),
            PromptVersion = reader.GetString(10),
            PromptText = reader.GetString(11),
            ModelUsed = reader.GetString(12),
            PlayerProfileUsed = reader.GetString(13),
            PlayerRelationshipNotesUsed = reader.GetString(14),
            PlayerMemoriesUsed = reader.GetString(15),
            SaveFileLinkUsed = reader.IsDBNull(16) ? null : reader.GetString(16)
        };
    }
}

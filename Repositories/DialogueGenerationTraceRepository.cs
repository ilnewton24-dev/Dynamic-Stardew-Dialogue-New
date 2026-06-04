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
                GeneratedDialogueId, GeneratedAt, CharacterId, InterceptedNpcName, CharacterName, ResolvedCharacterName, LocationName, InternalLocationId, DisplayLocationName, SaveContextSnapshot,
                MemoriesUsed, RelationshipsUsed, UserOverridesUsed, DialogueSourcesUsed,
                SourceModsUsed, PromptVersion, PromptText, ModelUsed,
                PlayerProfileUsed, PlayerRelationshipNotesUsed, PlayerMemoriesUsed, SaveFileLinkUsed, PlayerProfileMatchMethod, RequestSource
            )
            VALUES (
                @generatedDialogueId, @generatedAt, @characterId, @interceptedNpcName, @characterName, @resolvedCharacterName, @locationName, @internalLocationId, @displayLocationName, @saveContextSnapshot,
                @memoriesUsed, @relationshipsUsed, @userOverridesUsed, @dialogueSourcesUsed,
                @sourceModsUsed, @promptVersion, @promptText, @modelUsed,
                @playerProfileUsed, @playerRelationshipNotesUsed, @playerMemoriesUsed, @saveFileLinkUsed, @playerProfileMatchMethod, @requestSource
            );
            SELECT last_insert_rowid();
            ";
        command.Parameters.AddWithValue("@generatedDialogueId", trace.GeneratedDialogueId);
        command.Parameters.AddWithValue("@generatedAt", trace.GeneratedAt.ToString("O"));
        command.Parameters.AddWithValue("@characterId", trace.CharacterId);
        command.Parameters.AddWithValue("@interceptedNpcName", trace.InterceptedNpcName);
        command.Parameters.AddWithValue("@characterName", trace.CharacterName);
        command.Parameters.AddWithValue("@resolvedCharacterName", trace.ResolvedCharacterName);
        command.Parameters.AddWithValue("@locationName", trace.LocationName);
        command.Parameters.AddWithValue("@internalLocationId", trace.InternalLocationId);
        command.Parameters.AddWithValue("@displayLocationName", trace.DisplayLocationName);
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
        command.Parameters.AddWithValue("@playerProfileMatchMethod", trace.PlayerProfileMatchMethod);
        command.Parameters.AddWithValue("@requestSource", trace.RequestSource);
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
                   InterceptedNpcName, CharacterName, ResolvedCharacterName, LocationName, InternalLocationId, DisplayLocationName,
                   MemoriesUsed, RelationshipsUsed, UserOverridesUsed, DialogueSourcesUsed,
                   SourceModsUsed, PromptVersion, PromptText, ModelUsed,
                   PlayerProfileUsed, PlayerRelationshipNotesUsed, PlayerMemoriesUsed, SaveFileLinkUsed,
                   PlayerProfileMatchMethod, RequestSource
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
            InterceptedNpcName = reader.GetString(5),
            CharacterName = reader.GetString(6),
            ResolvedCharacterName = reader.GetString(7),
            LocationName = reader.GetString(8),
            InternalLocationId = reader.GetString(9),
            DisplayLocationName = reader.GetString(10),
            MemoriesUsed = reader.GetString(11),
            RelationshipsUsed = reader.GetString(12),
            UserOverridesUsed = reader.GetString(13),
            DialogueSourcesUsed = reader.GetString(14),
            SourceModsUsed = reader.GetString(15),
            PromptVersion = reader.GetString(16),
            PromptText = reader.GetString(17),
            ModelUsed = reader.GetString(18),
            PlayerProfileUsed = reader.GetString(19),
            PlayerRelationshipNotesUsed = reader.GetString(20),
            PlayerMemoriesUsed = reader.GetString(21),
            SaveFileLinkUsed = reader.IsDBNull(22) ? null : reader.GetString(22),
            PlayerProfileMatchMethod = reader.IsDBNull(23) ? "none" : reader.GetString(23),
            RequestSource = reader.IsDBNull(24) ? "" : reader.GetString(24)
        };
    }
}

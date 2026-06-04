using LivingLoreDialogue.Data;
using LivingLoreDialogue.Models;
using Microsoft.Data.Sqlite;

namespace LivingLoreDialogue.Repositories;

public sealed class GeneratedDialogueHistoryRepository
{
    private readonly SqliteConnectionFactory connectionFactory;

    public GeneratedDialogueHistoryRepository(SqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<GeneratedDialogueHistoryEntry>> GetRecentAsync(int limit)
    {
        List<GeneratedDialogueHistoryEntry> entries = new();
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            SELECT Id, CharacterId, CharacterName, Season, Weather, Location, FriendshipLevel,
                   RelationshipContext, Topic, Prompt, DialogueText, Emotion,
                   CharacterConsistencyScore, ContextRelevanceScore, RelationshipRelevanceScore,
                   DiversityScore, RepetitionRiskScore, CreatedDate
            FROM GeneratedDialogueHistory
            ORDER BY CreatedDate DESC
            LIMIT @limit;
            ";
        command.Parameters.AddWithValue("@limit", limit);

        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            entries.Add(Map(reader));

        return entries;
    }

    public async Task<IReadOnlyList<GeneratedDialogueHistoryEntry>> GetForCharacterAsync(long characterId)
    {
        List<GeneratedDialogueHistoryEntry> entries = new();
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            SELECT Id, CharacterId, CharacterName, Season, Weather, Location, FriendshipLevel,
                   RelationshipContext, Topic, Prompt, DialogueText, Emotion,
                   CharacterConsistencyScore, ContextRelevanceScore, RelationshipRelevanceScore,
                   DiversityScore, RepetitionRiskScore, CreatedDate
            FROM GeneratedDialogueHistory
            WHERE CharacterId = @characterId
            ORDER BY CreatedDate DESC;
            ";
        command.Parameters.AddWithValue("@characterId", characterId);

        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            entries.Add(Map(reader));

        return entries;
    }

    public async Task<IReadOnlyList<GeneratedDialogueHistoryEntry>> GetForCharacterIdsAsync(IEnumerable<long> characterIds, int limit)
    {
        long[] ids = characterIds.Distinct().ToArray();
        if (ids.Length == 0)
            return Array.Empty<GeneratedDialogueHistoryEntry>();

        List<GeneratedDialogueHistoryEntry> entries = new();
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        string[] parameterNames = ids.Select((_, index) => $"@id{index}").ToArray();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $@"
            SELECT Id, CharacterId, CharacterName, Season, Weather, Location, FriendshipLevel,
                   RelationshipContext, Topic, Prompt, DialogueText, Emotion,
                   CharacterConsistencyScore, ContextRelevanceScore, RelationshipRelevanceScore,
                   DiversityScore, RepetitionRiskScore, CreatedDate
            FROM GeneratedDialogueHistory
            WHERE CharacterId IN ({string.Join(", ", parameterNames)})
            ORDER BY CreatedDate DESC
            LIMIT @limit;
            ";
        for (int i = 0; i < ids.Length; i++)
            command.Parameters.AddWithValue(parameterNames[i], ids[i]);
        command.Parameters.AddWithValue("@limit", limit);

        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            entries.Add(Map(reader));

        return entries;
    }

    public async Task<GeneratedDialogueHistoryEntry?> GetByIdAsync(long id)
    {
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            SELECT Id, CharacterId, CharacterName, Season, Weather, Location, FriendshipLevel,
                   RelationshipContext, Topic, Prompt, DialogueText, Emotion,
                   CharacterConsistencyScore, ContextRelevanceScore, RelationshipRelevanceScore,
                   DiversityScore, RepetitionRiskScore, CreatedDate
            FROM GeneratedDialogueHistory
            WHERE Id = @id
            LIMIT 1;
            ";
        command.Parameters.AddWithValue("@id", id);

        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Map(reader) : null;
    }

    public async Task<long> AddAsync(
        Character character,
        DialogueContext context,
        string? relationshipContext,
        string prompt,
        GeneratedDialogue dialogue,
        DialogueQualityScores? qualityScores = null)
    {
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO GeneratedDialogueHistory (
                CharacterId, CharacterName, Season, Weather, Location, FriendshipLevel,
                RelationshipContext, Topic, Prompt, DialogueText, Emotion,
                CharacterConsistencyScore, ContextRelevanceScore, RelationshipRelevanceScore,
                DiversityScore, RepetitionRiskScore, CreatedDate
            )
            VALUES (
                @characterId, @characterName, @season, @weather, @location, @friendshipLevel,
                @relationshipContext, @topic, @prompt, @dialogueText, @emotion,
                @characterConsistencyScore, @contextRelevanceScore, @relationshipRelevanceScore,
                @diversityScore, @repetitionRiskScore, @createdDate
            );
            SELECT last_insert_rowid();
            ";
        command.Parameters.AddWithValue("@characterId", character.Id);
        command.Parameters.AddWithValue("@characterName", character.Name);
        command.Parameters.AddWithValue("@season", context.Season);
        command.Parameters.AddWithValue("@weather", context.Weather);
        command.Parameters.AddWithValue("@location", context.Location);
        command.Parameters.AddWithValue("@friendshipLevel", context.FriendshipLevel);
        command.Parameters.AddWithValue("@relationshipContext", (object?)relationshipContext ?? DBNull.Value);
        command.Parameters.AddWithValue("@topic", dialogue.Topic);
        command.Parameters.AddWithValue("@prompt", prompt);
        command.Parameters.AddWithValue("@dialogueText", dialogue.Dialogue);
        command.Parameters.AddWithValue("@emotion", dialogue.Emotion);
        command.Parameters.AddWithValue("@characterConsistencyScore", qualityScores?.CharacterConsistency ?? 0);
        command.Parameters.AddWithValue("@contextRelevanceScore", qualityScores?.ContextRelevance ?? 0);
        command.Parameters.AddWithValue("@relationshipRelevanceScore", qualityScores?.RelationshipRelevance ?? 0);
        command.Parameters.AddWithValue("@diversityScore", qualityScores?.Diversity ?? 0);
        command.Parameters.AddWithValue("@repetitionRiskScore", qualityScores?.RepetitionRisk ?? 0);
        command.Parameters.AddWithValue("@createdDate", DateTime.UtcNow.ToString("O"));
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static GeneratedDialogueHistoryEntry Map(SqliteDataReader reader)
    {
        return new GeneratedDialogueHistoryEntry
        {
            Id = reader.GetInt64(0),
            CharacterId = reader.GetInt64(1),
            CharacterName = reader.GetString(2),
            Season = reader.GetString(3),
            Weather = reader.GetString(4),
            Location = reader.GetString(5),
            FriendshipLevel = reader.GetInt32(6),
            RelationshipContext = reader.IsDBNull(7) ? null : reader.GetString(7),
            Topic = reader.GetString(8),
            Prompt = reader.GetString(9),
            DialogueText = reader.GetString(10),
            Emotion = reader.GetString(11),
            CharacterConsistencyScore = reader.GetInt32(12),
            ContextRelevanceScore = reader.GetInt32(13),
            RelationshipRelevanceScore = reader.GetInt32(14),
            DiversityScore = reader.GetInt32(15),
            RepetitionRiskScore = reader.GetInt32(16),
            CreatedDate = DateTime.Parse(reader.GetString(17))
        };
    }
}

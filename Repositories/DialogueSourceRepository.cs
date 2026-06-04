using LivingLoreDialogue.Data;
using LivingLoreDialogue.Models;
using Microsoft.Data.Sqlite;

namespace LivingLoreDialogue.Repositories;

public sealed class DialogueSourceRepository
{
    private readonly SqliteConnectionFactory connectionFactory;

    public DialogueSourceRepository(SqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task UpsertAsync(DialogueSource source)
    {
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        string now = DateTime.UtcNow.ToString("O");
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO DialogueSources (
                CanonicalCharacterId, SourceModId, FilePath, AssetName, DialogueKey, RawText,
                Conditions, Season, Weather, Location, HeartLevel, RelationshipState,
                SourcePriority, IsActive, LastSeen, CreatedAt, UpdatedAt
            )
            VALUES (
                @canonicalCharacterId, @sourceModId, @filePath, @assetName, @dialogueKey, @rawText,
                @conditions, @season, @weather, @location, @heartLevel, @relationshipState,
                @sourcePriority, 1, @lastSeen, @now, @now
            )
            ON CONFLICT(CanonicalCharacterId, SourceModId, FilePath, DialogueKey) DO UPDATE SET
                AssetName = excluded.AssetName,
                RawText = excluded.RawText,
                Conditions = excluded.Conditions,
                Season = excluded.Season,
                Weather = excluded.Weather,
                Location = excluded.Location,
                HeartLevel = excluded.HeartLevel,
                RelationshipState = excluded.RelationshipState,
                SourcePriority = excluded.SourcePriority,
                IsActive = 1,
                LastSeen = excluded.LastSeen,
                UpdatedAt = excluded.UpdatedAt;
            ";
        AddParameters(command, source, now);
        await command.ExecuteNonQueryAsync();
    }

    public async Task UpsertRangeAsync(IEnumerable<DialogueSource> sources)
    {
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        string now = DateTime.UtcNow.ToString("O");
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
            INSERT INTO DialogueSources (
                CanonicalCharacterId, SourceModId, FilePath, AssetName, DialogueKey, RawText,
                Conditions, Season, Weather, Location, HeartLevel, RelationshipState,
                SourcePriority, IsActive, LastSeen, CreatedAt, UpdatedAt
            )
            VALUES (
                @canonicalCharacterId, @sourceModId, @filePath, @assetName, @dialogueKey, @rawText,
                @conditions, @season, @weather, @location, @heartLevel, @relationshipState,
                @sourcePriority, 1, @lastSeen, @now, @now
            )
            ON CONFLICT(CanonicalCharacterId, SourceModId, FilePath, DialogueKey) DO UPDATE SET
                AssetName = excluded.AssetName,
                RawText = excluded.RawText,
                Conditions = excluded.Conditions,
                Season = excluded.Season,
                Weather = excluded.Weather,
                Location = excluded.Location,
                HeartLevel = excluded.HeartLevel,
                RelationshipState = excluded.RelationshipState,
                SourcePriority = excluded.SourcePriority,
                IsActive = 1,
                LastSeen = excluded.LastSeen,
                UpdatedAt = excluded.UpdatedAt;
            ";

        foreach (DialogueSource source in sources)
        {
            command.Parameters.Clear();
            AddParameters(command, source, now);
            await command.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }

    public async Task<IReadOnlyList<DialogueSource>> GetForCanonicalAsync(long canonicalCharacterId, bool activeOnly = true, int limit = 300)
    {
        List<DialogueSource> sources = new();
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = activeOnly
            ? @"
              SELECT Id, CanonicalCharacterId, SourceModId, FilePath, AssetName, DialogueKey, RawText,
                     Conditions, Season, Weather, Location, HeartLevel, RelationshipState,
                     SourcePriority, IsActive, LastSeen, CreatedAt, UpdatedAt
              FROM DialogueSources
              WHERE CanonicalCharacterId = @canonicalCharacterId AND IsActive = 1
              ORDER BY SourcePriority DESC, DialogueKey
              LIMIT @limit;
              "
            : @"
              SELECT Id, CanonicalCharacterId, SourceModId, FilePath, AssetName, DialogueKey, RawText,
                     Conditions, Season, Weather, Location, HeartLevel, RelationshipState,
                     SourcePriority, IsActive, LastSeen, CreatedAt, UpdatedAt
              FROM DialogueSources
              WHERE CanonicalCharacterId = @canonicalCharacterId
              ORDER BY IsActive DESC, SourcePriority DESC, DialogueKey
              LIMIT @limit;
              ";
        command.Parameters.AddWithValue("@canonicalCharacterId", canonicalCharacterId);
        command.Parameters.AddWithValue("@limit", limit);

        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            sources.Add(Map(reader));

        return sources;
    }

    public async Task<DialogueSourceSummary?> GetSummaryAsync(long canonicalCharacterId)
    {
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            SELECT Id, CanonicalCharacterId, SummaryText, ToneSummary, CommonTopics,
                   RelationshipPatterns, ImportantCanonFacts, LastGeneratedAt
            FROM DialogueSourceSummaries
            WHERE CanonicalCharacterId = @canonicalCharacterId
            LIMIT 1;
            ";
        command.Parameters.AddWithValue("@canonicalCharacterId", canonicalCharacterId);

        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        return new DialogueSourceSummary
        {
            Id = reader.GetInt64(0),
            CanonicalCharacterId = reader.GetInt64(1),
            SummaryText = reader.GetString(2),
            ToneSummary = reader.GetString(3),
            CommonTopics = reader.GetString(4),
            RelationshipPatterns = reader.GetString(5),
            ImportantCanonFacts = reader.GetString(6),
            LastGeneratedAt = DateTime.Parse(reader.GetString(7))
        };
    }

    public async Task UpsertSummaryAsync(DialogueSourceSummary summary)
    {
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO DialogueSourceSummaries (
                CanonicalCharacterId, SummaryText, ToneSummary, CommonTopics,
                RelationshipPatterns, ImportantCanonFacts, LastGeneratedAt
            )
            VALUES (
                @canonicalCharacterId, @summaryText, @toneSummary, @commonTopics,
                @relationshipPatterns, @importantCanonFacts, @lastGeneratedAt
            )
            ON CONFLICT(CanonicalCharacterId) DO UPDATE SET
                SummaryText = excluded.SummaryText,
                ToneSummary = excluded.ToneSummary,
                CommonTopics = excluded.CommonTopics,
                RelationshipPatterns = excluded.RelationshipPatterns,
                ImportantCanonFacts = excluded.ImportantCanonFacts,
                LastGeneratedAt = excluded.LastGeneratedAt;
            ";
        command.Parameters.AddWithValue("@canonicalCharacterId", summary.CanonicalCharacterId);
        command.Parameters.AddWithValue("@summaryText", summary.SummaryText);
        command.Parameters.AddWithValue("@toneSummary", summary.ToneSummary);
        command.Parameters.AddWithValue("@commonTopics", summary.CommonTopics);
        command.Parameters.AddWithValue("@relationshipPatterns", summary.RelationshipPatterns);
        command.Parameters.AddWithValue("@importantCanonFacts", summary.ImportantCanonFacts);
        command.Parameters.AddWithValue("@lastGeneratedAt", summary.LastGeneratedAt.ToString("O"));
        await command.ExecuteNonQueryAsync();
    }

    private static void AddParameters(SqliteCommand command, DialogueSource source, string now)
    {
        command.Parameters.AddWithValue("@canonicalCharacterId", source.CanonicalCharacterId);
        command.Parameters.AddWithValue("@sourceModId", (object?)source.SourceModId ?? DBNull.Value);
        command.Parameters.AddWithValue("@filePath", source.FilePath);
        command.Parameters.AddWithValue("@assetName", (object?)source.AssetName ?? DBNull.Value);
        command.Parameters.AddWithValue("@dialogueKey", source.DialogueKey);
        command.Parameters.AddWithValue("@rawText", source.RawText);
        command.Parameters.AddWithValue("@conditions", (object?)source.Conditions ?? DBNull.Value);
        command.Parameters.AddWithValue("@season", (object?)source.Season ?? DBNull.Value);
        command.Parameters.AddWithValue("@weather", (object?)source.Weather ?? DBNull.Value);
        command.Parameters.AddWithValue("@location", (object?)source.Location ?? DBNull.Value);
        command.Parameters.AddWithValue("@heartLevel", (object?)source.HeartLevel ?? DBNull.Value);
        command.Parameters.AddWithValue("@relationshipState", (object?)source.RelationshipState ?? DBNull.Value);
        command.Parameters.AddWithValue("@sourcePriority", source.SourcePriority);
        command.Parameters.AddWithValue("@lastSeen", source.LastSeen.ToString("O"));
        command.Parameters.AddWithValue("@now", now);
    }

    private static DialogueSource Map(SqliteDataReader reader)
    {
        return new DialogueSource
        {
            Id = reader.GetInt64(0),
            CanonicalCharacterId = reader.GetInt64(1),
            SourceModId = reader.IsDBNull(2) ? null : reader.GetString(2),
            FilePath = reader.GetString(3),
            AssetName = reader.IsDBNull(4) ? null : reader.GetString(4),
            DialogueKey = reader.GetString(5),
            RawText = reader.GetString(6),
            Conditions = reader.IsDBNull(7) ? null : reader.GetString(7),
            Season = reader.IsDBNull(8) ? null : reader.GetString(8),
            Weather = reader.IsDBNull(9) ? null : reader.GetString(9),
            Location = reader.IsDBNull(10) ? null : reader.GetString(10),
            HeartLevel = reader.IsDBNull(11) ? null : reader.GetInt32(11),
            RelationshipState = reader.IsDBNull(12) ? null : reader.GetString(12),
            SourcePriority = reader.GetInt32(13),
            IsActive = reader.GetInt32(14) == 1,
            LastSeen = DateTime.Parse(reader.GetString(15)),
            CreatedAt = DateTime.Parse(reader.GetString(16)),
            UpdatedAt = DateTime.Parse(reader.GetString(17))
        };
    }
}

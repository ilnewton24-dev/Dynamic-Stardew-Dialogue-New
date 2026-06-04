using LivingLoreDialogue.Data;
using LivingLoreDialogue.Models;
using Microsoft.Data.Sqlite;

namespace LivingLoreDialogue.Repositories;

public sealed class MemoryRepository
{
    private const string Columns = @"
        Id, CharacterId, SaveFileName, SaveFilePath, PlayerName, FarmName, PlayerProfileId, NpcName,
        MemoryType, Title, Summary, MemoryText, Importance, Season, Day, Year, Location, Source,
        CreatedDate, CreatedAt, LastSeenAt, IsActive, Tags, ReferenceId
        ";

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
        command.CommandText = $@"
            SELECT {Columns}
            FROM Memories
            WHERE CharacterId = @characterId
              AND IsActive = 1
            ORDER BY Importance DESC, CreatedDate DESC
            LIMIT @limit;
            ";
        command.Parameters.AddWithValue("@characterId", characterId);
        command.Parameters.AddWithValue("@limit", limit);

        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            memories.Add(Map(reader));

        return memories;
    }

    public async Task<IReadOnlyList<Memory>> GetRelevantForGenerationAsync(
        string? saveFileName,
        IEnumerable<long> characterIds,
        string? npcName,
        long? playerProfileId,
        int limit)
    {
        if (string.IsNullOrWhiteSpace(saveFileName))
            return Array.Empty<Memory>();

        List<long> ids = characterIds.Distinct().ToList();
        List<Memory> memories = new();
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        string idFilter = ids.Count == 0
            ? "0"
            : string.Join(", ", ids.Select((_, index) => $"@characterId{index}"));

        command.CommandText = $@"
            SELECT {Columns}
            FROM Memories
            WHERE SaveFileName = @saveFileName
              AND IsActive = 1
              AND (
                    (@npcName <> '' AND NpcName = @npcName COLLATE NOCASE)
                 OR CharacterId IN ({idFilter})
                 OR (@playerProfileId IS NOT NULL AND PlayerProfileId = @playerProfileId)
                  )
            ORDER BY
                CASE WHEN @npcName <> '' AND NpcName = @npcName COLLATE NOCASE THEN 0 ELSE 1 END,
                Importance DESC,
                COALESCE(LastSeenAt, CreatedAt, CreatedDate) DESC
            LIMIT @limit;
            ";
        command.Parameters.AddWithValue("@saveFileName", saveFileName.Trim());
        command.Parameters.AddWithValue("@npcName", npcName?.Trim() ?? "");
        command.Parameters.AddWithValue("@playerProfileId", playerProfileId is null ? DBNull.Value : playerProfileId.Value);
        command.Parameters.AddWithValue("@limit", limit);
        for (int i = 0; i < ids.Count; i++)
            command.Parameters.AddWithValue($"@characterId{i}", ids[i]);

        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            memories.Add(Map(reader));

        return memories;
    }

    public async Task<IReadOnlyList<Memory>> GetAllAsync(
        string? saveFileName = null,
        long? playerProfileId = null,
        string? npcName = null,
        bool includeInactive = true)
    {
        List<Memory> memories = new();
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        List<string> where = new();
        if (!string.IsNullOrWhiteSpace(saveFileName))
        {
            where.Add("SaveFileName = @saveFileName");
            command.Parameters.AddWithValue("@saveFileName", saveFileName.Trim());
        }
        if (playerProfileId is not null)
        {
            where.Add("PlayerProfileId = @playerProfileId");
            command.Parameters.AddWithValue("@playerProfileId", playerProfileId.Value);
        }
        if (!string.IsNullOrWhiteSpace(npcName))
        {
            where.Add("NpcName = @npcName COLLATE NOCASE");
            command.Parameters.AddWithValue("@npcName", npcName.Trim());
        }
        if (!includeInactive)
            where.Add("IsActive = 1");

        command.CommandText = $@"
            SELECT {Columns}
            FROM Memories
            {(where.Count == 0 ? "" : "WHERE " + string.Join(" AND ", where))}
            ORDER BY CreatedDate DESC;
            ";

        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            memories.Add(Map(reader));

        return memories;
    }

    public async Task<long> AddAsync(long characterId, string memoryText, int importance)
    {
        Memory memory = new()
        {
            CharacterId = characterId,
            MemoryText = memoryText,
            Summary = memoryText,
            Title = "Manual memory",
            Importance = importance,
            Source = "Manual",
            MemoryType = "Manual",
            IsActive = true
        };
        return await AddManualAsync(memory);
    }

    public async Task<long> AddManualAsync(Memory memory)
    {
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        string now = DateTime.UtcNow.ToString("O");
        string summary = FirstNonEmpty(memory.Summary, memory.MemoryText);
        string title = FirstNonEmpty(memory.Title, summary, "Manual memory");

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO Memories (
                CharacterId, SaveFileName, SaveFilePath, PlayerName, FarmName, PlayerProfileId, NpcName,
                MemoryType, Title, Summary, MemoryText, Importance, Season, Day, Year, Location, Source,
                CreatedDate, CreatedAt, LastSeenAt, IsActive, Tags, ReferenceId
            )
            VALUES (
                @characterId, @saveFileName, @saveFilePath, @playerName, @farmName, @playerProfileId, @npcName,
                @memoryType, @title, @summary, @memoryText, @importance, @season, @day, @year, @location, @source,
                @createdDate, @createdAt, @lastSeenAt, @isActive, @tags, @referenceId
            );
            SELECT last_insert_rowid();
            ";
        BindMemory(command, memory, title, summary, now);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    public async Task<AutomaticMemoryWriteResult> UpsertAutomaticAsync(Memory memory)
    {
        if (string.IsNullOrWhiteSpace(memory.SaveFileName))
            return new AutomaticMemoryWriteResult(false, false, null, "missing save file name");
        if (string.IsNullOrWhiteSpace(memory.ReferenceId))
            return new AutomaticMemoryWriteResult(false, false, null, "missing reference id");

        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using (SqliteCommand existing = connection.CreateCommand())
        {
            existing.CommandText = @"
                SELECT Id
                FROM Memories
                WHERE SaveFileName = @saveFileName
                  AND MemoryType = @memoryType
                  AND IFNULL(NpcName, '') = IFNULL(@npcName, '')
                  AND ReferenceId = @referenceId
                LIMIT 1;
                ";
            existing.Parameters.AddWithValue("@saveFileName", memory.SaveFileName.Trim());
            existing.Parameters.AddWithValue("@memoryType", memory.MemoryType);
            existing.Parameters.AddWithValue("@npcName", (object?)memory.NpcName ?? DBNull.Value);
            existing.Parameters.AddWithValue("@referenceId", memory.ReferenceId);

            object? existingId = await existing.ExecuteScalarAsync();
            if (existingId is not null)
            {
                long id = Convert.ToInt64(existingId);
                await using SqliteCommand update = connection.CreateCommand();
                update.CommandText = @"
                    UPDATE Memories
                    SET LastSeenAt = @now,
                        IsActive = 1
                    WHERE Id = @id;
                    ";
                update.Parameters.AddWithValue("@id", id);
                update.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("O"));
                await update.ExecuteNonQueryAsync();
                return new AutomaticMemoryWriteResult(false, true, id, "duplicate skipped");
            }
        }

        long insertedId = await InsertWithConnectionAsync(connection, memory);
        return new AutomaticMemoryWriteResult(true, false, insertedId, "inserted");
    }

    public async Task UpdateAsync(long id, Memory memory)
    {
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        string summary = FirstNonEmpty(memory.Summary, memory.MemoryText);
        string title = FirstNonEmpty(memory.Title, summary, "Manual memory");

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            UPDATE Memories
            SET CharacterId = @characterId,
                SaveFileName = @saveFileName,
                SaveFilePath = @saveFilePath,
                PlayerName = @playerName,
                FarmName = @farmName,
                PlayerProfileId = @playerProfileId,
                NpcName = @npcName,
                MemoryType = @memoryType,
                Title = @title,
                Summary = @summary,
                MemoryText = @memoryText,
                Importance = @importance,
                Season = @season,
                Day = @day,
                Year = @year,
                Location = @location,
                Source = @source,
                IsActive = @isActive,
                Tags = @tags,
                ReferenceId = @referenceId
            WHERE Id = @id;
            ";
        command.Parameters.AddWithValue("@id", id);
        BindMemory(command, memory, title, summary, DateTime.UtcNow.ToString("O"), includeCreatedFields: false);
        await command.ExecuteNonQueryAsync();
    }

    public async Task UpdateAsync(long id, long characterId, string memoryText, int importance)
    {
        await UpdateAsync(id, new Memory
        {
            CharacterId = characterId,
            MemoryText = memoryText,
            Summary = memoryText,
            Title = "Manual memory",
            Importance = importance,
            Source = "Manual",
            MemoryType = "Manual",
            IsActive = true
        });
    }

    public async Task SetActiveAsync(long id, bool isActive)
    {
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "UPDATE Memories SET IsActive = @isActive WHERE Id = @id;";
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@isActive", isActive ? 1 : 0);
        await command.ExecuteNonQueryAsync();
    }

    public async Task DeleteAsync(long id)
    {
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Memories WHERE Id = @id;";
        command.Parameters.AddWithValue("@id", id);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> InsertWithConnectionAsync(SqliteConnection connection, Memory memory)
    {
        string now = DateTime.UtcNow.ToString("O");
        string summary = FirstNonEmpty(memory.Summary, memory.MemoryText);
        string title = FirstNonEmpty(memory.Title, summary, memory.MemoryType);

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO Memories (
                CharacterId, SaveFileName, SaveFilePath, PlayerName, FarmName, PlayerProfileId, NpcName,
                MemoryType, Title, Summary, MemoryText, Importance, Season, Day, Year, Location, Source,
                CreatedDate, CreatedAt, LastSeenAt, IsActive, Tags, ReferenceId
            )
            VALUES (
                @characterId, @saveFileName, @saveFilePath, @playerName, @farmName, @playerProfileId, @npcName,
                @memoryType, @title, @summary, @memoryText, @importance, @season, @day, @year, @location, @source,
                @createdDate, @createdAt, @lastSeenAt, @isActive, @tags, @referenceId
            );
            SELECT last_insert_rowid();
            ";
        BindMemory(command, memory, title, summary, now);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static void BindMemory(
        SqliteCommand command,
        Memory memory,
        string title,
        string summary,
        string now,
        bool includeCreatedFields = true)
    {
        command.Parameters.AddWithValue("@characterId", memory.CharacterId is null ? DBNull.Value : memory.CharacterId.Value);
        command.Parameters.AddWithValue("@saveFileName", (object?)NullIfWhiteSpace(memory.SaveFileName) ?? DBNull.Value);
        command.Parameters.AddWithValue("@saveFilePath", (object?)NullIfWhiteSpace(memory.SaveFilePath) ?? DBNull.Value);
        command.Parameters.AddWithValue("@playerName", memory.PlayerName ?? "");
        command.Parameters.AddWithValue("@farmName", memory.FarmName ?? "");
        command.Parameters.AddWithValue("@playerProfileId", memory.PlayerProfileId is null ? DBNull.Value : memory.PlayerProfileId.Value);
        command.Parameters.AddWithValue("@npcName", (object?)NullIfWhiteSpace(memory.NpcName) ?? DBNull.Value);
        command.Parameters.AddWithValue("@memoryType", FirstNonEmpty(memory.MemoryType, "Manual"));
        command.Parameters.AddWithValue("@title", title);
        command.Parameters.AddWithValue("@summary", summary);
        command.Parameters.AddWithValue("@memoryText", summary);
        command.Parameters.AddWithValue("@importance", Math.Clamp(memory.Importance, 1, 5));
        command.Parameters.AddWithValue("@season", memory.Season ?? "");
        command.Parameters.AddWithValue("@day", memory.Day);
        command.Parameters.AddWithValue("@year", memory.Year);
        command.Parameters.AddWithValue("@location", memory.Location ?? "");
        command.Parameters.AddWithValue("@source", FirstNonEmpty(memory.Source, "Manual"));
        command.Parameters.AddWithValue("@isActive", memory.IsActive ? 1 : 0);
        command.Parameters.AddWithValue("@tags", memory.Tags ?? "");
        command.Parameters.AddWithValue("@referenceId", memory.ReferenceId ?? "");

        if (!includeCreatedFields)
            return;

        command.Parameters.AddWithValue("@createdDate", memory.CreatedDate == default ? now : memory.CreatedDate.ToString("O"));
        command.Parameters.AddWithValue("@createdAt", memory.CreatedAt == default ? now : memory.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("@lastSeenAt", memory.LastSeenAt is null ? DBNull.Value : memory.LastSeenAt.Value.ToString("O"));
    }

    private static Memory Map(SqliteDataReader reader)
    {
        string summary = GetString(reader, 10);
        string memoryText = GetString(reader, 11);
        DateTime createdDate = GetDate(reader, 18);
        DateTime createdAt = GetDate(reader, 19);

        if (string.IsNullOrWhiteSpace(summary))
            summary = memoryText;
        if (string.IsNullOrWhiteSpace(memoryText))
            memoryText = summary;
        if (createdAt == default)
            createdAt = createdDate;

        return new Memory
        {
            Id = reader.GetInt64(0),
            CharacterId = reader.IsDBNull(1) ? null : reader.GetInt64(1),
            SaveFileName = reader.IsDBNull(2) ? null : reader.GetString(2),
            SaveFilePath = reader.IsDBNull(3) ? null : reader.GetString(3),
            PlayerName = GetString(reader, 4),
            FarmName = GetString(reader, 5),
            PlayerProfileId = reader.IsDBNull(6) ? null : reader.GetInt64(6),
            NpcName = reader.IsDBNull(7) ? null : reader.GetString(7),
            MemoryType = GetString(reader, 8, "Manual"),
            Title = GetString(reader, 9),
            Summary = summary,
            MemoryText = memoryText,
            Importance = reader.GetInt32(12),
            Season = GetString(reader, 13),
            Day = reader.GetInt32(14),
            Year = reader.GetInt32(15),
            Location = GetString(reader, 16),
            Source = GetString(reader, 17, "Manual"),
            CreatedDate = createdDate,
            CreatedAt = createdAt,
            LastSeenAt = reader.IsDBNull(20) ? null : DateTime.Parse(reader.GetString(20)),
            IsActive = reader.GetInt32(21) != 0,
            Tags = GetString(reader, 22),
            ReferenceId = GetString(reader, 23)
        };
    }

    private static string GetString(SqliteDataReader reader, int ordinal, string fallback = "")
    {
        return reader.IsDBNull(ordinal) ? fallback : reader.GetString(ordinal);
    }

    private static DateTime GetDate(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) || string.IsNullOrWhiteSpace(reader.GetString(ordinal))
            ? default
            : DateTime.Parse(reader.GetString(ordinal));
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (string? value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return "";
    }

    private static string? NullIfWhiteSpace(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

public sealed record AutomaticMemoryWriteResult(bool Inserted, bool DuplicateSkipped, long? Id, string Message);

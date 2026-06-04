using LivingLoreDialogue.Data;
using LivingLoreDialogue.Models;
using Microsoft.Data.Sqlite;

namespace LivingLoreDialogue.Repositories;

/// <summary>
/// Persistence for player/farmer lore profiles and their relationship notes, memories, and save
/// links. Profiles are archived (IsActive = 0) rather than hard-deleted unless explicitly removed.
/// Exactly one profile is the "active" default at a time (set via <see cref="SetActiveAsync"/>).
/// </summary>
public sealed class PlayerProfileRepository
{
    private const string ProfileColumns = @"
        Id, ProfileName, FarmerName, FarmName, SaveFileName, SaveFilePath, Description, Backstory,
        Personality, RoleplayStyle, PreferredTone, ImportantHistory, CurrentGoals, RelationshipNotes,
        CustomLore, IsActive, CreatedAt, UpdatedAt
        ";

    private readonly SqliteConnectionFactory connectionFactory;

    public PlayerProfileRepository(SqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    // ---- Profiles ---------------------------------------------------------------------------

    public async Task<IReadOnlyList<PlayerProfile>> GetAllAsync()
    {
        List<PlayerProfile> profiles = new();
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT {ProfileColumns} FROM PlayerProfiles ORDER BY IsActive DESC, ProfileName;";

        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            profiles.Add(MapProfile(reader));
        return profiles;
    }

    public async Task<PlayerProfile?> GetByIdAsync(long id)
    {
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT {ProfileColumns} FROM PlayerProfiles WHERE Id = @id LIMIT 1;";
        command.Parameters.AddWithValue("@id", id);

        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapProfile(reader) : null;
    }

    /// <summary>The single active/default profile, used when no save-file match exists.</summary>
    public async Task<PlayerProfile?> GetActiveAsync()
    {
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT {ProfileColumns} FROM PlayerProfiles WHERE IsActive = 1 ORDER BY UpdatedAt DESC LIMIT 1;";

        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapProfile(reader) : null;
    }

    /// <summary>Resolves the profile linked to a save file (default link preferred), or null.</summary>
    public async Task<PlayerProfile?> GetBySaveFileAsync(string saveFileName)
    {
        if (string.IsNullOrWhiteSpace(saveFileName))
            return null;

        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $@"
            SELECT {string.Join(", ", PrefixColumns("p"))}
            FROM PlayerProfileSaveLinks l
            JOIN PlayerProfiles p ON p.Id = l.PlayerProfileId
            WHERE l.SaveFileName = @saveFileName
            ORDER BY l.IsDefaultForSave DESC, p.IsActive DESC, l.UpdatedAt DESC
            LIMIT 1;
            ";
        command.Parameters.AddWithValue("@saveFileName", saveFileName);

        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapProfile(reader) : null;
    }

    /// <summary>Resolves an active profile matching the live farmer and farm names, or null.</summary>
    public async Task<PlayerProfile?> GetByFarmerAndFarmAsync(string playerName, string farmName)
    {
        if (string.IsNullOrWhiteSpace(playerName) || string.IsNullOrWhiteSpace(farmName))
            return null;

        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $@"
            SELECT {ProfileColumns}
            FROM PlayerProfiles
            WHERE IsActive = 1
              AND FarmerName = @playerName COLLATE NOCASE
              AND FarmName = @farmName COLLATE NOCASE
            ORDER BY UpdatedAt DESC
            LIMIT 1;
            ";
        command.Parameters.AddWithValue("@playerName", playerName);
        command.Parameters.AddWithValue("@farmName", farmName);

        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapProfile(reader) : null;
    }

    public async Task<long> AddAsync(PlayerProfile profile)
    {
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO PlayerProfiles (
                ProfileName, FarmerName, FarmName, SaveFileName, SaveFilePath, Description, Backstory,
                Personality, RoleplayStyle, PreferredTone, ImportantHistory, CurrentGoals, RelationshipNotes,
                CustomLore, IsActive, CreatedAt, UpdatedAt
            )
            VALUES (
                @profileName, @farmerName, @farmName, @saveFileName, @saveFilePath, @description, @backstory,
                @personality, @roleplayStyle, @preferredTone, @importantHistory, @currentGoals, @relationshipNotes,
                @customLore, @isActive, @now, @now
            );
            SELECT last_insert_rowid();
            ";
        BindProfile(command, profile);
        command.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("O"));
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    public async Task UpdateAsync(PlayerProfile profile)
    {
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            UPDATE PlayerProfiles SET
                ProfileName = @profileName, FarmerName = @farmerName, FarmName = @farmName,
                SaveFileName = @saveFileName, SaveFilePath = @saveFilePath, Description = @description,
                Backstory = @backstory, Personality = @personality, RoleplayStyle = @roleplayStyle,
                PreferredTone = @preferredTone, ImportantHistory = @importantHistory, CurrentGoals = @currentGoals,
                RelationshipNotes = @relationshipNotes, CustomLore = @customLore, IsActive = @isActive,
                UpdatedAt = @now
            WHERE Id = @id;
            ";
        BindProfile(command, profile);
        command.Parameters.AddWithValue("@id", profile.Id);
        command.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>Marks the given profile as the single active/default profile.</summary>
    public async Task SetActiveAsync(long id)
    {
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            UPDATE PlayerProfiles SET IsActive = CASE WHEN Id = @id THEN 1 ELSE 0 END,
                UpdatedAt = CASE WHEN Id = @id THEN @now ELSE UpdatedAt END;
            ";
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>Archives (deactivates) a profile without deleting it.</summary>
    public async Task ArchiveAsync(long id)
    {
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "UPDATE PlayerProfiles SET IsActive = 0, UpdatedAt = @now WHERE Id = @id;";
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>Hard-deletes a profile and its child rows. Only call on explicit confirmation.</summary>
    public async Task DeleteAsync(long id)
    {
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "DELETE FROM PlayerProfiles WHERE Id = @id;";
        command.Parameters.AddWithValue("@id", id);
        await command.ExecuteNonQueryAsync();
    }

    // ---- Relationships ----------------------------------------------------------------------

    public async Task<long> AddRelationshipAsync(PlayerProfileRelationship relationship)
    {
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO PlayerProfileRelationships (
                PlayerProfileId, CanonicalCharacterId, RelationshipType, RelationshipDescription,
                RelationshipStrength, CustomNotes, CreatedAt, UpdatedAt
            )
            VALUES (
                @playerProfileId, @canonicalCharacterId, @relationshipType, @relationshipDescription,
                @relationshipStrength, @customNotes, @now, @now
            );
            SELECT last_insert_rowid();
            ";
        command.Parameters.AddWithValue("@playerProfileId", relationship.PlayerProfileId);
        command.Parameters.AddWithValue("@canonicalCharacterId", relationship.CanonicalCharacterId);
        command.Parameters.AddWithValue("@relationshipType", relationship.RelationshipType);
        command.Parameters.AddWithValue("@relationshipDescription", relationship.RelationshipDescription);
        command.Parameters.AddWithValue("@relationshipStrength", relationship.RelationshipStrength);
        command.Parameters.AddWithValue("@customNotes", relationship.CustomNotes ?? "");
        command.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("O"));
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    public async Task<IReadOnlyList<PlayerProfileRelationship>> GetRelationshipsAsync(long profileId, long? canonicalCharacterId = null)
    {
        List<PlayerProfileRelationship> relationships = new();
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            SELECT r.Id, r.PlayerProfileId, r.CanonicalCharacterId, c.CanonicalName, r.RelationshipType,
                   r.RelationshipDescription, r.RelationshipStrength, r.CustomNotes, r.CreatedAt, r.UpdatedAt
            FROM PlayerProfileRelationships r
            LEFT JOIN CanonicalCharacters c ON c.Id = r.CanonicalCharacterId
            WHERE r.PlayerProfileId = @profileId
              AND (@canonicalCharacterId IS NULL OR r.CanonicalCharacterId = @canonicalCharacterId)
            ORDER BY r.UpdatedAt DESC;
            ";
        command.Parameters.AddWithValue("@profileId", profileId);
        command.Parameters.AddWithValue("@canonicalCharacterId", (object?)canonicalCharacterId ?? DBNull.Value);

        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            relationships.Add(new PlayerProfileRelationship
            {
                Id = reader.GetInt64(0),
                PlayerProfileId = reader.GetInt64(1),
                CanonicalCharacterId = reader.GetInt64(2),
                CanonicalName = reader.IsDBNull(3) ? null : reader.GetString(3),
                RelationshipType = reader.GetString(4),
                RelationshipDescription = reader.GetString(5),
                RelationshipStrength = reader.GetInt32(6),
                CustomNotes = reader.GetString(7),
                CreatedAt = DateTime.Parse(reader.GetString(8)),
                UpdatedAt = DateTime.Parse(reader.GetString(9))
            });
        }
        return relationships;
    }

    // ---- Memories ---------------------------------------------------------------------------

    public async Task<long> AddMemoryAsync(PlayerProfileMemory memory)
    {
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO PlayerProfileMemories (
                PlayerProfileId, CanonicalCharacterId, MemoryText, Importance, CreatedAt, UpdatedAt
            )
            VALUES (@playerProfileId, @canonicalCharacterId, @memoryText, @importance, @now, @now);
            SELECT last_insert_rowid();
            ";
        command.Parameters.AddWithValue("@playerProfileId", memory.PlayerProfileId);
        command.Parameters.AddWithValue("@canonicalCharacterId", (object?)memory.CanonicalCharacterId ?? DBNull.Value);
        command.Parameters.AddWithValue("@memoryText", memory.MemoryText);
        command.Parameters.AddWithValue("@importance", memory.Importance);
        command.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("O"));
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    /// <summary>
    /// Memories for a profile. When <paramref name="canonicalCharacterId"/> is set, returns memories
    /// for that character plus general (character-less) memories.
    /// </summary>
    public async Task<IReadOnlyList<PlayerProfileMemory>> GetMemoriesAsync(long profileId, long? canonicalCharacterId = null, bool includeGeneral = true)
    {
        List<PlayerProfileMemory> memories = new();
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            SELECT m.Id, m.PlayerProfileId, m.CanonicalCharacterId, c.CanonicalName, m.MemoryText,
                   m.Importance, m.CreatedAt, m.UpdatedAt
            FROM PlayerProfileMemories m
            LEFT JOIN CanonicalCharacters c ON c.Id = m.CanonicalCharacterId
            WHERE m.PlayerProfileId = @profileId
              AND (
                    @canonicalCharacterId IS NULL
                 OR m.CanonicalCharacterId = @canonicalCharacterId
                 OR (@includeGeneral = 1 AND m.CanonicalCharacterId IS NULL)
              )
            ORDER BY m.Importance DESC, m.UpdatedAt DESC;
            ";
        command.Parameters.AddWithValue("@profileId", profileId);
        command.Parameters.AddWithValue("@canonicalCharacterId", (object?)canonicalCharacterId ?? DBNull.Value);
        command.Parameters.AddWithValue("@includeGeneral", includeGeneral ? 1 : 0);

        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            memories.Add(new PlayerProfileMemory
            {
                Id = reader.GetInt64(0),
                PlayerProfileId = reader.GetInt64(1),
                CanonicalCharacterId = reader.IsDBNull(2) ? null : reader.GetInt64(2),
                CanonicalName = reader.IsDBNull(3) ? null : reader.GetString(3),
                MemoryText = reader.GetString(4),
                Importance = reader.GetInt32(5),
                CreatedAt = DateTime.Parse(reader.GetString(6)),
                UpdatedAt = DateTime.Parse(reader.GetString(7))
            });
        }
        return memories;
    }

    // ---- Save links -------------------------------------------------------------------------

    public async Task<long> LinkSaveAsync(PlayerProfileSaveLink link)
    {
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        if (link.IsDefaultForSave)
        {
            await using SqliteCommand clearCommand = connection.CreateCommand();
            clearCommand.CommandText = @"
                UPDATE PlayerProfileSaveLinks
                SET IsDefaultForSave = 0, UpdatedAt = @now
                WHERE SaveFileName = @saveFileName;
                ";
            clearCommand.Parameters.AddWithValue("@saveFileName", link.SaveFileName);
            clearCommand.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("O"));
            await clearCommand.ExecuteNonQueryAsync();
        }

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO PlayerProfileSaveLinks (
                PlayerProfileId, SaveFileName, SaveFilePath, LastSeen, IsDefaultForSave, CreatedAt, UpdatedAt
            )
            VALUES (@playerProfileId, @saveFileName, @saveFilePath, @lastSeen, @isDefault, @now, @now);
            SELECT last_insert_rowid();
            ";
        command.Parameters.AddWithValue("@playerProfileId", link.PlayerProfileId);
        command.Parameters.AddWithValue("@saveFileName", link.SaveFileName);
        command.Parameters.AddWithValue("@saveFilePath", (object?)link.SaveFilePath ?? DBNull.Value);
        command.Parameters.AddWithValue("@lastSeen", (object?)link.LastSeen?.ToString("O") ?? DBNull.Value);
        command.Parameters.AddWithValue("@isDefault", link.IsDefaultForSave ? 1 : 0);
        command.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("O"));
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    public async Task<IReadOnlyList<PlayerProfileSaveLink>> GetSaveLinksAsync(long profileId)
    {
        List<PlayerProfileSaveLink> links = new();
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            SELECT Id, PlayerProfileId, SaveFileName, SaveFilePath, LastSeen, IsDefaultForSave, CreatedAt, UpdatedAt
            FROM PlayerProfileSaveLinks
            WHERE PlayerProfileId = @profileId
            ORDER BY UpdatedAt DESC;
            ";
        command.Parameters.AddWithValue("@profileId", profileId);

        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            links.Add(new PlayerProfileSaveLink
            {
                Id = reader.GetInt64(0),
                PlayerProfileId = reader.GetInt64(1),
                SaveFileName = reader.GetString(2),
                SaveFilePath = reader.IsDBNull(3) ? null : reader.GetString(3),
                LastSeen = reader.IsDBNull(4) ? null : DateTime.Parse(reader.GetString(4)),
                IsDefaultForSave = reader.GetInt32(5) == 1,
                CreatedAt = DateTime.Parse(reader.GetString(6)),
                UpdatedAt = DateTime.Parse(reader.GetString(7))
            });
        }
        return links;
    }

    // ---- Helpers ----------------------------------------------------------------------------

    private static IEnumerable<string> PrefixColumns(string alias) => new[]
    {
        "Id", "ProfileName", "FarmerName", "FarmName", "SaveFileName", "SaveFilePath", "Description",
        "Backstory", "Personality", "RoleplayStyle", "PreferredTone", "ImportantHistory", "CurrentGoals",
        "RelationshipNotes", "CustomLore", "IsActive", "CreatedAt", "UpdatedAt"
    }.Select(column => $"{alias}.{column}");

    private static void BindProfile(SqliteCommand command, PlayerProfile profile)
    {
        command.Parameters.AddWithValue("@profileName", profile.ProfileName);
        command.Parameters.AddWithValue("@farmerName", profile.FarmerName ?? "");
        command.Parameters.AddWithValue("@farmName", profile.FarmName ?? "");
        command.Parameters.AddWithValue("@saveFileName", (object?)profile.SaveFileName ?? DBNull.Value);
        command.Parameters.AddWithValue("@saveFilePath", (object?)profile.SaveFilePath ?? DBNull.Value);
        command.Parameters.AddWithValue("@description", profile.Description ?? "");
        command.Parameters.AddWithValue("@backstory", profile.Backstory ?? "");
        command.Parameters.AddWithValue("@personality", profile.Personality ?? "");
        command.Parameters.AddWithValue("@roleplayStyle", profile.RoleplayStyle ?? "");
        command.Parameters.AddWithValue("@preferredTone", profile.PreferredTone ?? "");
        command.Parameters.AddWithValue("@importantHistory", profile.ImportantHistory ?? "");
        command.Parameters.AddWithValue("@currentGoals", profile.CurrentGoals ?? "");
        command.Parameters.AddWithValue("@relationshipNotes", profile.RelationshipNotes ?? "");
        command.Parameters.AddWithValue("@customLore", profile.CustomLore ?? "");
        command.Parameters.AddWithValue("@isActive", profile.IsActive ? 1 : 0);
    }

    private static PlayerProfile MapProfile(SqliteDataReader reader)
    {
        return new PlayerProfile
        {
            Id = reader.GetInt64(0),
            ProfileName = reader.GetString(1),
            FarmerName = reader.GetString(2),
            FarmName = reader.GetString(3),
            SaveFileName = reader.IsDBNull(4) ? null : reader.GetString(4),
            SaveFilePath = reader.IsDBNull(5) ? null : reader.GetString(5),
            Description = reader.GetString(6),
            Backstory = reader.GetString(7),
            Personality = reader.GetString(8),
            RoleplayStyle = reader.GetString(9),
            PreferredTone = reader.GetString(10),
            ImportantHistory = reader.GetString(11),
            CurrentGoals = reader.GetString(12),
            RelationshipNotes = reader.GetString(13),
            CustomLore = reader.GetString(14),
            IsActive = reader.GetInt32(15) == 1,
            CreatedAt = DateTime.Parse(reader.GetString(16)),
            UpdatedAt = DateTime.Parse(reader.GetString(17))
        };
    }
}

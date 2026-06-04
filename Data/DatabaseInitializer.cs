using Microsoft.Data.Sqlite;

namespace LivingLoreDialogue.Data;

public sealed class DatabaseInitializer
{
    private readonly SqliteConnectionFactory connectionFactory;

    public DatabaseInitializer(SqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task InitializeAsync(string schemaPath, string seedPath, bool seedOnFirstRun)
    {
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        bool hasCharactersTable = await TableExistsAsync(connection, "Characters");
        if (hasCharactersTable)
        {
            await MigrateCharactersTableAsync(connection);
            await MigrateCharactersUniqueNameConstraintAsync(connection);
        }

        string schemaSql = await File.ReadAllTextAsync(schemaPath);
        await ExecuteScriptAsync(connection, schemaSql);
        await MigrateCharactersTableAsync(connection);
        await MigrateCharactersUniqueNameConstraintAsync(connection);
        await MigrateMemoriesTableAsync(connection);
        await MigrateGeneratedDialogueHistoryTableAsync(connection);
        await AddMissingColumnsAsync(connection, "Memories", MemoryColumns);
        await CreateMemoryIndexesAsync(connection);
        await AddMissingColumnsAsync(connection, "TestScenarios", new()
        {
            ["PlayerProfileId"] = "INTEGER NULL"
        });
        await AddMissingColumnsAsync(connection, "DialogueSources", new()
        {
            ["SourceRootPath"] = "TEXT NULL"
        });
        await AddMissingColumnsAsync(connection, "DialogueGenerationTrace", new()
        {
            ["InterceptedNpcName"] = "TEXT NOT NULL DEFAULT ''",
            ["CharacterName"] = "TEXT NOT NULL DEFAULT ''",
            ["ResolvedCharacterName"] = "TEXT NOT NULL DEFAULT ''",
            ["LocationName"] = "TEXT NOT NULL DEFAULT ''",
            ["InternalLocationId"] = "TEXT NOT NULL DEFAULT ''",
            ["DisplayLocationName"] = "TEXT NOT NULL DEFAULT ''",
            ["PlayerProfileUsed"] = "TEXT NOT NULL DEFAULT 'null'",
            ["PlayerRelationshipNotesUsed"] = "TEXT NOT NULL DEFAULT '[]'",
            ["PlayerMemoriesUsed"] = "TEXT NOT NULL DEFAULT '[]'",
            ["SaveFileLinkUsed"] = "TEXT NULL",
            ["PlayerProfileMatchMethod"] = "TEXT NOT NULL DEFAULT 'none'",
            ["RequestSource"] = "TEXT NOT NULL DEFAULT ''"
        });
        await BackfillCanonicalCharactersAsync(connection);

        if (seedOnFirstRun && !hasCharactersTable && File.Exists(seedPath))
        {
            string seedSql = await File.ReadAllTextAsync(seedPath);
            await ExecuteScriptAsync(connection, seedSql);
            await BackfillCanonicalCharactersAsync(connection);
        }
    }

    private static Dictionary<string, string> MemoryColumns { get; } = new()
    {
        ["SaveFileName"] = "TEXT NULL",
        ["SaveFilePath"] = "TEXT NULL",
        ["PlayerName"] = "TEXT NOT NULL DEFAULT ''",
        ["FarmName"] = "TEXT NOT NULL DEFAULT ''",
        ["PlayerProfileId"] = "INTEGER NULL",
        ["NpcName"] = "TEXT NULL",
        ["MemoryType"] = "TEXT NOT NULL DEFAULT 'Manual'",
        ["Title"] = "TEXT NOT NULL DEFAULT ''",
        ["Summary"] = "TEXT NOT NULL DEFAULT ''",
        ["Season"] = "TEXT NOT NULL DEFAULT ''",
        ["Day"] = "INTEGER NOT NULL DEFAULT 0",
        ["Year"] = "INTEGER NOT NULL DEFAULT 0",
        ["Location"] = "TEXT NOT NULL DEFAULT ''",
        ["Source"] = "TEXT NOT NULL DEFAULT 'Manual'",
        ["CreatedAt"] = "TEXT NOT NULL DEFAULT ''",
        ["LastSeenAt"] = "TEXT NULL",
        ["IsActive"] = "INTEGER NOT NULL DEFAULT 1",
        ["Tags"] = "TEXT NOT NULL DEFAULT ''",
        ["ReferenceId"] = "TEXT NOT NULL DEFAULT ''"
    };

    private static async Task<bool> TableExistsAsync(SqliteConnection connection, string tableName)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @name;";
        command.Parameters.AddWithValue("@name", tableName);
        object? result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result) > 0;
    }

    private static async Task ExecuteScriptAsync(SqliteConnection connection, string sql)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task MigrateCharactersTableAsync(SqliteConnection connection)
    {
        Dictionary<string, string> columns = new()
        {
            ["IsActive"] = "INTEGER NOT NULL DEFAULT 1",
            ["CanonicalCharacterId"] = "INTEGER NULL",
            ["InternalName"] = "TEXT NULL",
            ["DisplayName"] = "TEXT NULL",
            ["IsVanilla"] = "INTEGER NOT NULL DEFAULT 0",
            ["IsCustomNpc"] = "INTEGER NOT NULL DEFAULT 0",
            ["IsExtension"] = "INTEGER NOT NULL DEFAULT 0",
            ["LastSeen"] = "TEXT NULL",
            ["SourceModId"] = "TEXT NULL",
            ["SourceModName"] = "TEXT NULL",
            ["SourceModVersion"] = "TEXT NULL",
            ["SourceModAuthor"] = "TEXT NULL",
            ["CharacterFingerprint"] = "TEXT NULL",
            ["LastModified"] = "TEXT NULL",
            ["RawModData"] = "TEXT NULL"
        };

        HashSet<string> existingColumns = new(StringComparer.OrdinalIgnoreCase);
        await using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA table_info(Characters);";
            await using SqliteDataReader reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                existingColumns.Add(reader.GetString(1));
        }

        foreach ((string name, string definition) in columns)
        {
            if (existingColumns.Contains(name))
                continue;

            await using SqliteCommand alterCommand = connection.CreateCommand();
            alterCommand.CommandText = $"ALTER TABLE Characters ADD COLUMN {name} {definition};";
            await alterCommand.ExecuteNonQueryAsync();
        }
    }

    private static async Task MigrateGeneratedDialogueHistoryTableAsync(SqliteConnection connection)
    {
        if (!await TableExistsAsync(connection, "GeneratedDialogueHistory"))
            return;

        Dictionary<string, string> columns = new()
        {
            ["RelationshipContext"] = "TEXT NULL",
            ["CharacterConsistencyScore"] = "INTEGER NOT NULL DEFAULT 0",
            ["ContextRelevanceScore"] = "INTEGER NOT NULL DEFAULT 0",
            ["RelationshipRelevanceScore"] = "INTEGER NOT NULL DEFAULT 0",
            ["DiversityScore"] = "INTEGER NOT NULL DEFAULT 0",
            ["RepetitionRiskScore"] = "INTEGER NOT NULL DEFAULT 0"
        };

        HashSet<string> existingColumns = new(StringComparer.OrdinalIgnoreCase);
        await using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA table_info(GeneratedDialogueHistory);";
            await using SqliteDataReader reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                existingColumns.Add(reader.GetString(1));
        }

        foreach ((string name, string definition) in columns)
        {
            if (existingColumns.Contains(name))
                continue;

            await using SqliteCommand alterCommand = connection.CreateCommand();
            alterCommand.CommandText = $"ALTER TABLE GeneratedDialogueHistory ADD COLUMN {name} {definition};";
            await alterCommand.ExecuteNonQueryAsync();
        }
    }

    private static async Task MigrateMemoriesTableAsync(SqliteConnection connection)
    {
        if (!await TableExistsAsync(connection, "Memories"))
            return;

        bool characterIdIsNotNull = false;
        await using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA table_info(Memories);";
            await using SqliteDataReader reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                if (reader.GetString(1).Equals("CharacterId", StringComparison.OrdinalIgnoreCase))
                {
                    characterIdIsNotNull = reader.GetInt32(3) == 1;
                    break;
                }
            }
        }

        if (!characterIdIsNotNull)
            return;

        await ExecuteScriptAsync(connection, @"
            PRAGMA foreign_keys = OFF;
            PRAGMA legacy_alter_table = ON;

            ALTER TABLE Memories RENAME TO Memories_OldCharacterRequired;

            CREATE TABLE Memories (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                CharacterId INTEGER NULL,
                MemoryText TEXT NOT NULL,
                Importance INTEGER NOT NULL DEFAULT 1,
                CreatedDate TEXT NOT NULL,
                FOREIGN KEY (CharacterId) REFERENCES Characters(Id) ON DELETE CASCADE
            );

            INSERT INTO Memories (Id, CharacterId, MemoryText, Importance, CreatedDate)
            SELECT Id, CharacterId, MemoryText, Importance, CreatedDate
            FROM Memories_OldCharacterRequired;

            DROP TABLE Memories_OldCharacterRequired;
            PRAGMA legacy_alter_table = OFF;
            PRAGMA foreign_keys = ON;
            ");
    }

    private static async Task CreateMemoryIndexesAsync(SqliteConnection connection)
    {
        if (!await TableExistsAsync(connection, "Memories"))
            return;

        await ExecuteScriptAsync(connection, @"
            CREATE INDEX IF NOT EXISTS IX_Memories_SaveFile_Npc ON Memories(SaveFileName, NpcName, IsActive, Importance);
            CREATE UNIQUE INDEX IF NOT EXISTS IX_Memories_Automatic_Dedupe ON Memories(SaveFileName, MemoryType, IFNULL(NpcName, ''), ReferenceId)
            WHERE Source = 'Automatic' AND ReferenceId <> '';
            ");
    }

    /// <summary>Adds any missing columns to an existing table (no-op if the table is absent).</summary>
    private static async Task AddMissingColumnsAsync(SqliteConnection connection, string tableName, Dictionary<string, string> columns)
    {
        if (!await TableExistsAsync(connection, tableName))
            return;

        HashSet<string> existingColumns = new(StringComparer.OrdinalIgnoreCase);
        await using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = $"PRAGMA table_info({tableName});";
            await using SqliteDataReader reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                existingColumns.Add(reader.GetString(1));
        }

        foreach ((string name, string definition) in columns)
        {
            if (existingColumns.Contains(name))
                continue;

            await using SqliteCommand alterCommand = connection.CreateCommand();
            alterCommand.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {name} {definition};";
            await alterCommand.ExecuteNonQueryAsync();
        }
    }

    private static async Task MigrateCharactersUniqueNameConstraintAsync(SqliteConnection connection)
    {
        if (!await TableExistsAsync(connection, "Characters"))
            return;

        string? createSql;
        await using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'Characters' LIMIT 1;";
            createSql = (await command.ExecuteScalarAsync())?.ToString();
        }

        if (string.IsNullOrWhiteSpace(createSql) || !createSql.Contains("Name TEXT NOT NULL UNIQUE", StringComparison.OrdinalIgnoreCase))
            return;

        await ExecuteScriptAsync(connection, @"
            PRAGMA foreign_keys = OFF;
            PRAGMA legacy_alter_table = ON;

            ALTER TABLE Characters RENAME TO Characters_OldUniqueName;

            CREATE TABLE Characters (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                CanonicalCharacterId INTEGER NULL,
                Name TEXT NOT NULL,
                InternalName TEXT NULL,
                DisplayName TEXT NULL,
                Description TEXT NOT NULL,
                Personality TEXT NOT NULL,
                Occupation TEXT NOT NULL,
                HomeLocation TEXT NOT NULL,
                IsActive INTEGER NOT NULL DEFAULT 1,
                IsVanilla INTEGER NOT NULL DEFAULT 0,
                IsCustomNpc INTEGER NOT NULL DEFAULT 0,
                IsExtension INTEGER NOT NULL DEFAULT 0,
                LastSeen TEXT NULL,
                SourceModId TEXT NULL,
                SourceModName TEXT NULL,
                SourceModVersion TEXT NULL,
                SourceModAuthor TEXT NULL,
                CharacterFingerprint TEXT NULL,
                LastModified TEXT NULL,
                RawModData TEXT NULL,
                FOREIGN KEY (CanonicalCharacterId) REFERENCES CanonicalCharacters(Id)
            );

            INSERT INTO Characters (
                Id, CanonicalCharacterId, Name, InternalName, DisplayName, Description, Personality, Occupation, HomeLocation,
                IsActive, IsVanilla, IsCustomNpc, IsExtension, LastSeen,
                SourceModId, SourceModName, SourceModVersion, SourceModAuthor, CharacterFingerprint,
                LastModified, RawModData
            )
            SELECT
                Id, CanonicalCharacterId, Name, InternalName, DisplayName, Description, Personality, Occupation, HomeLocation,
                IsActive, IsVanilla, IsCustomNpc, IsExtension, LastSeen,
                SourceModId, SourceModName, SourceModVersion, SourceModAuthor, CharacterFingerprint,
                LastModified, RawModData
            FROM Characters_OldUniqueName;

            DROP TABLE Characters_OldUniqueName;
            PRAGMA legacy_alter_table = OFF;
            PRAGMA foreign_keys = ON;
            ");
    }

    private static async Task BackfillCanonicalCharactersAsync(SqliteConnection connection)
    {
        if (!await TableExistsAsync(connection, "CanonicalCharacters") || !await TableExistsAsync(connection, "Characters"))
            return;

        await using SqliteCommand selectCommand = connection.CreateCommand();
        selectCommand.CommandText = @"
            SELECT Id, Name, SourceModId
            FROM Characters
            WHERE CanonicalCharacterId IS NULL;
            ";

        List<(long Id, string Name, string? SourceModId)> characters = new();
        await using (SqliteDataReader reader = await selectCommand.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                characters.Add((
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2)));
            }
        }

        foreach ((long id, string name, string? sourceModId) in characters)
        {
            long canonicalId = await EnsureCanonicalCharacterAsync(connection, name);

            await using SqliteCommand updateCommand = connection.CreateCommand();
            updateCommand.CommandText = @"
                UPDATE Characters
                SET CanonicalCharacterId = @canonicalId,
                    InternalName = COALESCE(InternalName, Name),
                    DisplayName = COALESCE(DisplayName, Name),
                    IsCustomNpc = CASE WHEN SourceModId IS NULL OR SourceModId = '' THEN IsCustomNpc ELSE 1 END
                WHERE Id = @id;
                ";
            updateCommand.Parameters.AddWithValue("@canonicalId", canonicalId);
            updateCommand.Parameters.AddWithValue("@id", id);
            await updateCommand.ExecuteNonQueryAsync();

            if (!string.IsNullOrWhiteSpace(sourceModId))
                await UpsertCharacterSourceAsync(connection, canonicalId, sourceModId, "BaseDefinition", 50, "Backfilled from existing character row.");
        }
    }

    private static async Task<long> EnsureCanonicalCharacterAsync(SqliteConnection connection, string name)
    {
        await using (SqliteCommand selectCommand = connection.CreateCommand())
        {
            selectCommand.CommandText = "SELECT Id FROM CanonicalCharacters WHERE CanonicalName = @name LIMIT 1;";
            selectCommand.Parameters.AddWithValue("@name", name);
            object? existingId = await selectCommand.ExecuteScalarAsync();
            if (existingId is not null)
                return Convert.ToInt64(existingId);
        }

        string now = DateTime.UtcNow.ToString("O");
        await using SqliteCommand insertCommand = connection.CreateCommand();
        insertCommand.CommandText = @"
            INSERT INTO CanonicalCharacters (CanonicalName, DisplayName, IsActive, CreatedAt, UpdatedAt, CanonPriority, UserLocked)
            VALUES (@name, @name, 1, @now, @now, 0, 0);
            SELECT last_insert_rowid();
            ";
        insertCommand.Parameters.AddWithValue("@name", name);
        insertCommand.Parameters.AddWithValue("@now", now);
        return Convert.ToInt64(await insertCommand.ExecuteScalarAsync());
    }

    private static async Task UpsertCharacterSourceAsync(
        SqliteConnection connection,
        long canonicalCharacterId,
        string sourceModId,
        string sourceType,
        int priority,
        string notes)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            INSERT OR IGNORE INTO CharacterSources (CanonicalCharacterId, SourceModId, SourceType, Priority, Notes)
            VALUES (@canonicalCharacterId, @sourceModId, @sourceType, @priority, @notes);
            ";
        command.Parameters.AddWithValue("@canonicalCharacterId", canonicalCharacterId);
        command.Parameters.AddWithValue("@sourceModId", sourceModId);
        command.Parameters.AddWithValue("@sourceType", sourceType);
        command.Parameters.AddWithValue("@priority", priority);
        command.Parameters.AddWithValue("@notes", notes);
        await command.ExecuteNonQueryAsync();
    }
}

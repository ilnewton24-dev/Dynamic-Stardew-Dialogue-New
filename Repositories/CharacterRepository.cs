using LivingLoreDialogue.Data;
using LivingLoreDialogue.Models;
using Microsoft.Data.Sqlite;

namespace LivingLoreDialogue.Repositories;

public sealed class CharacterRepository
{
    private readonly SqliteConnectionFactory connectionFactory;

    public CharacterRepository(SqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task<Character?> GetByNameAsync(string name)
    {
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            SELECT Id, CanonicalCharacterId, Name, InternalName, DisplayName, Description, Personality, Occupation, HomeLocation,
                   IsActive, IsVanilla, IsCustomNpc, IsExtension, LastSeen,
                   SourceModId, SourceModName, SourceModVersion, SourceModAuthor, CharacterFingerprint,
                   LastModified, RawModData
            FROM Characters
            WHERE Name = @name COLLATE NOCASE
               OR InternalName = @name COLLATE NOCASE
               OR DisplayName = @name COLLATE NOCASE
            ORDER BY IsActive DESC, IsExtension ASC, SourceModName
            LIMIT 1;
            ";
        command.Parameters.AddWithValue("@name", name);

        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Map(reader) : null;
    }

    public async Task<IReadOnlyList<Character>> GetForCanonicalAsync(long canonicalCharacterId, bool activeOnly = true)
    {
        List<Character> characters = new();
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = activeOnly
            ? @"
              SELECT Id, CanonicalCharacterId, Name, InternalName, DisplayName, Description, Personality, Occupation, HomeLocation,
                     IsActive, IsVanilla, IsCustomNpc, IsExtension, LastSeen,
                     SourceModId, SourceModName, SourceModVersion, SourceModAuthor, CharacterFingerprint,
                     LastModified, RawModData
              FROM Characters
              WHERE CanonicalCharacterId = @canonicalCharacterId AND IsActive = 1
              ORDER BY IsExtension ASC, SourceModName, Name;
              "
            : @"
              SELECT Id, CanonicalCharacterId, Name, InternalName, DisplayName, Description, Personality, Occupation, HomeLocation,
                     IsActive, IsVanilla, IsCustomNpc, IsExtension, LastSeen,
                     SourceModId, SourceModName, SourceModVersion, SourceModAuthor, CharacterFingerprint,
                     LastModified, RawModData
              FROM Characters
              WHERE CanonicalCharacterId = @canonicalCharacterId
              ORDER BY IsActive DESC, IsExtension ASC, SourceModName, Name;
              ";
        command.Parameters.AddWithValue("@canonicalCharacterId", canonicalCharacterId);

        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            characters.Add(Map(reader));

        return characters;
    }

    public async Task<Character?> GetByIdAsync(long id)
    {
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            SELECT Id, CanonicalCharacterId, Name, InternalName, DisplayName, Description, Personality, Occupation, HomeLocation,
                   IsActive, IsVanilla, IsCustomNpc, IsExtension, LastSeen,
                   SourceModId, SourceModName, SourceModVersion, SourceModAuthor, CharacterFingerprint,
                   LastModified, RawModData
            FROM Characters
            WHERE Id = @id
            LIMIT 1;
            ";
        command.Parameters.AddWithValue("@id", id);

        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Map(reader) : null;
    }

    public async Task<IReadOnlyList<Character>> GetAllAsync(bool includeInactive = true)
    {
        List<Character> characters = new();
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $@"
            SELECT Id, CanonicalCharacterId, Name, InternalName, DisplayName, Description, Personality, Occupation, HomeLocation,
                   IsActive, IsVanilla, IsCustomNpc, IsExtension, LastSeen,
                   SourceModId, SourceModName, SourceModVersion, SourceModAuthor, CharacterFingerprint,
                   LastModified, RawModData
            FROM Characters
            {(includeInactive ? "" : "WHERE IsActive = 1")}
            ORDER BY Name;
            ";

        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            characters.Add(Map(reader));

        return characters;
    }

    public async Task<IReadOnlyList<Character>> GetAllWithSourceAsync()
    {
        return await this.GetAllAsync();
    }

    public async Task<int> CountByActiveStatusAsync(bool isActive)
    {
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Characters WHERE IsActive = @isActive;";
        command.Parameters.AddWithValue("@isActive", isActive ? 1 : 0);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    /// <summary>True if an active character row exists with the given name (case-insensitive).</summary>
    public async Task<bool> IsActiveCharacterAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Characters WHERE Name = @name COLLATE NOCASE AND IsActive = 1;";
        command.Parameters.AddWithValue("@name", name);
        return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
    }

    public async Task<Character?> GetByFingerprintAsync(string fingerprint)
    {
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            SELECT Id, CanonicalCharacterId, Name, InternalName, DisplayName, Description, Personality, Occupation, HomeLocation,
                   IsActive, IsVanilla, IsCustomNpc, IsExtension, LastSeen,
                   SourceModId, SourceModName, SourceModVersion, SourceModAuthor, CharacterFingerprint,
                   LastModified, RawModData
            FROM Characters
            WHERE CharacterFingerprint = @fingerprint
            LIMIT 1;
            ";
        command.Parameters.AddWithValue("@fingerprint", fingerprint);

        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Map(reader) : null;
    }

    public async Task<long> AddFromScanAsync(ScannedCharacter scanned)
    {
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO Characters (
                CanonicalCharacterId, Name, InternalName, DisplayName, Description, Personality, Occupation, HomeLocation,
                IsActive, IsVanilla, IsCustomNpc, IsExtension, LastSeen,
                SourceModId, SourceModName, SourceModVersion, SourceModAuthor, CharacterFingerprint,
                LastModified, RawModData
            )
            VALUES (
                @canonicalCharacterId, @name, @internalName, @displayName, @description, @personality, @occupation, @homeLocation,
                1, @isVanilla, @isCustomNpc, @isExtension, @lastSeen,
                @sourceModId, @sourceModName, @sourceModVersion, @sourceModAuthor, @fingerprint,
                @lastModified, @rawModData
            );
            SELECT last_insert_rowid();
            ";
        AddScannedParameters(command, scanned);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    public async Task UpdateFromScanAsync(long characterId, ScannedCharacter scanned)
    {
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            UPDATE Characters
            SET Name = @name,
                CanonicalCharacterId = @canonicalCharacterId,
                InternalName = @internalName,
                DisplayName = @displayName,
                Description = @description,
                Personality = @personality,
                Occupation = @occupation,
                HomeLocation = @homeLocation,
                IsActive = 1,
                IsVanilla = @isVanilla,
                IsCustomNpc = @isCustomNpc,
                IsExtension = @isExtension,
                LastSeen = @lastSeen,
                SourceModId = @sourceModId,
                SourceModName = @sourceModName,
                SourceModVersion = @sourceModVersion,
                SourceModAuthor = @sourceModAuthor,
                CharacterFingerprint = @fingerprint,
                LastModified = @lastModified,
                RawModData = @rawModData
            WHERE Id = @id;
            ";
        command.Parameters.AddWithValue("@id", characterId);
        AddScannedParameters(command, scanned);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<ClearCharactersSummary> ClearAllForRescanAsync()
    {
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteTransaction transaction = connection.BeginTransaction();
        try
        {
            int reviewItemsDeleted = await ExecuteDeleteAsync(connection, transaction, "DELETE FROM CharacterMergeReviewQueue;");
            int validationRowsDeleted = await ExecuteDeleteAsync(connection, transaction, "DELETE FROM CharacterValidationResults;");
            int charactersDeleted = await ExecuteDeleteAsync(connection, transaction, "DELETE FROM Characters;");
            int canonicalCharactersDeleted = await ExecuteDeleteAsync(connection, transaction, "DELETE FROM CanonicalCharacters;");

            await transaction.CommitAsync();

            return new ClearCharactersSummary
            {
                CharactersDeleted = charactersDeleted,
                CanonicalCharactersDeleted = canonicalCharactersDeleted,
                ReviewItemsDeleted = reviewItemsDeleted,
                ValidationRowsDeleted = validationRowsDeleted
            };
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task MarkInactiveAsync(long characterId, DateTime timestamp)
    {
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            UPDATE Characters
            SET IsActive = 0,
                LastModified = @lastModified
            WHERE Id = @id;
            ";
        command.Parameters.AddWithValue("@id", characterId);
        command.Parameters.AddWithValue("@lastModified", timestamp.ToString("O"));
        await command.ExecuteNonQueryAsync();
    }

    private static Character Map(SqliteDataReader reader)
    {
        return new Character
        {
            Id = reader.GetInt64(0),
            CanonicalCharacterId = reader.IsDBNull(1) ? null : reader.GetInt64(1),
            Name = reader.GetString(2),
            InternalName = ReadNullableString(reader, 3),
            DisplayName = ReadNullableString(reader, 4),
            Description = reader.GetString(5),
            Personality = reader.GetString(6),
            Occupation = reader.GetString(7),
            HomeLocation = reader.GetString(8),
            IsActive = reader.GetInt32(9) == 1,
            IsVanilla = reader.GetInt32(10) == 1,
            IsCustomNpc = reader.GetInt32(11) == 1,
            IsExtension = reader.GetInt32(12) == 1,
            LastSeen = ReadNullableDate(reader, 13),
            SourceModId = ReadNullableString(reader, 14),
            SourceModName = ReadNullableString(reader, 15),
            SourceModVersion = ReadNullableString(reader, 16),
            SourceModAuthor = ReadNullableString(reader, 17),
            CharacterFingerprint = ReadNullableString(reader, 18),
            LastModified = ReadNullableDate(reader, 19),
            RawModData = ReadNullableString(reader, 20)
        };
    }

    private static void AddScannedParameters(SqliteCommand command, ScannedCharacter scanned)
    {
        command.Parameters.AddWithValue("@canonicalCharacterId", (object?)scanned.CanonicalCharacterId ?? DBNull.Value);
        command.Parameters.AddWithValue("@name", scanned.Name);
        command.Parameters.AddWithValue("@internalName", string.IsNullOrWhiteSpace(scanned.InternalName) ? scanned.Name : scanned.InternalName);
        command.Parameters.AddWithValue("@displayName", string.IsNullOrWhiteSpace(scanned.DisplayName) ? scanned.Name : scanned.DisplayName);
        command.Parameters.AddWithValue("@description", scanned.Description);
        command.Parameters.AddWithValue("@personality", scanned.Personality);
        command.Parameters.AddWithValue("@occupation", scanned.Occupation);
        command.Parameters.AddWithValue("@homeLocation", scanned.HomeLocation);
        command.Parameters.AddWithValue("@isVanilla", scanned.IsVanilla ? 1 : 0);
        command.Parameters.AddWithValue("@isCustomNpc", scanned.IsCustomNpc ? 1 : 0);
        command.Parameters.AddWithValue("@isExtension", scanned.IsExtension ? 1 : 0);
        command.Parameters.AddWithValue("@lastSeen", scanned.LastSeen.ToString("O"));
        command.Parameters.AddWithValue("@sourceModId", scanned.SourceModId);
        command.Parameters.AddWithValue("@sourceModName", scanned.SourceModName);
        command.Parameters.AddWithValue("@sourceModVersion", scanned.SourceModVersion);
        command.Parameters.AddWithValue("@sourceModAuthor", scanned.SourceModAuthor);
        command.Parameters.AddWithValue("@fingerprint", scanned.CharacterFingerprint);
        command.Parameters.AddWithValue("@lastModified", scanned.LastSeen.ToString("O"));
        command.Parameters.AddWithValue("@rawModData", scanned.RawModData);
    }

    private static string? ReadNullableString(SqliteDataReader reader, int index)
    {
        return reader.IsDBNull(index) ? null : reader.GetString(index);
    }

    private static DateTime? ReadNullableDate(SqliteDataReader reader, int index)
    {
        if (reader.IsDBNull(index))
            return null;

        return DateTime.TryParse(reader.GetString(index), out DateTime parsed) ? parsed : null;
    }

    private static async Task<int> ExecuteDeleteAsync(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return await command.ExecuteNonQueryAsync();
    }
}

public sealed class ClearCharactersSummary
{
    public int CharactersDeleted { get; set; }
    public int CanonicalCharactersDeleted { get; set; }
    public int ReviewItemsDeleted { get; set; }
    public int ValidationRowsDeleted { get; set; }
}

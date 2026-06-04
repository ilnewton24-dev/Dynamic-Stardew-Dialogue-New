using LivingLoreDialogue.Data;
using LivingLoreDialogue.Models;
using Microsoft.Data.Sqlite;

namespace LivingLoreDialogue.Repositories;

public sealed class CanonicalCharacterRepository
{
    private readonly SqliteConnectionFactory connectionFactory;

    public CanonicalCharacterRepository(SqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<CanonicalCharacter>> GetAllAsync()
    {
        List<CanonicalCharacter> characters = new();
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            SELECT Id, CanonicalName, DisplayName, IsActive, CreatedAt, UpdatedAt, CanonPriority, UserLocked
            FROM CanonicalCharacters
            ORDER BY DisplayName;
            ";

        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            characters.Add(MapCanonical(reader));

        return characters;
    }

    public async Task<CanonicalCharacter?> GetByNameOrAliasAsync(string name)
    {
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();
        return await GetByNameOrAliasAsync(connection, name);
    }

    public async Task<IReadOnlyList<CharacterSource>> GetSourcesAsync(long canonicalCharacterId)
    {
        List<CharacterSource> sources = new();
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            SELECT Id, CanonicalCharacterId, SourceModId, SourceType, Priority, Notes
            FROM CharacterSources
            WHERE CanonicalCharacterId = @canonicalCharacterId
            ORDER BY Priority DESC, SourceType;
            ";
        command.Parameters.AddWithValue("@canonicalCharacterId", canonicalCharacterId);

        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            sources.Add(new CharacterSource
            {
                Id = reader.GetInt64(0),
                CanonicalCharacterId = reader.GetInt64(1),
                SourceModId = reader.GetString(2),
                SourceType = reader.GetString(3),
                Priority = reader.GetInt32(4),
                Notes = reader.IsDBNull(5) ? null : reader.GetString(5)
            });
        }

        return sources;
    }

    public async Task<long> EnsureCanonicalAsync(string name)
    {
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();
        return await EnsureCanonicalAsync(connection, name);
    }

    public async Task<CanonicalMatchResult> ResolveCandidateAsync(CharacterCandidate candidate)
    {
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        CanonicalCharacter? ruleMatch = await MatchUserRuleAsync(connection, candidate);
        if (ruleMatch is not null)
            return new CanonicalMatchResult(ruleMatch.Id, 100, "User-confirmed merge rule.", "UserConfirmed");

        CanonicalCharacter? directMatch = await GetByNameOrAliasAsync(connection, candidate.Name);
        if (directMatch is not null)
        {
            bool strongTargetEvidence = candidate.Evidence.HasFlag(CharacterEvidence.DataCharacters)
                || candidate.Evidence.HasFlag(CharacterEvidence.NpcDisposition)
                || candidate.Evidence.HasFlag(CharacterEvidence.DialogueAsset)
                || candidate.Evidence.HasFlag(CharacterEvidence.ScheduleAsset)
                || candidate.Evidence.HasFlag(CharacterEvidence.PortraitAsset)
                || candidate.Evidence.HasFlag(CharacterEvidence.CharacterAsset);
            int confidence = strongTargetEvidence ? 96 : 74;
            string reason = strongTargetEvidence
                ? "Same internal/display name and file or patch targets this NPC."
                : "Same display name with weaker mod evidence.";
            return new CanonicalMatchResult(directMatch.Id, confidence, reason, "ExactName");
        }

        return new CanonicalMatchResult(null, 0, "No canonical character or alias matched.", "NewCandidate");
    }

    public async Task RecordSourceAsync(long canonicalCharacterId, CharacterCandidate candidate, string sourceType, int priority, string notes)
    {
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();
        await UpsertSourceAsync(connection, canonicalCharacterId, candidate.SourceModId, sourceType, priority, notes);
    }

    public async Task QueueReviewAsync(CharacterCandidate candidate, CanonicalMatchResult match)
    {
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        string? suggestedName = null;
        if (match.CanonicalCharacterId is long canonicalId)
            suggestedName = await GetCanonicalNameAsync(connection, canonicalId);

        string now = DateTime.UtcNow.ToString("O");
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO CharacterMergeReviewQueue (
                CandidateName, CandidateInternalName, SourceModId, SourceModName,
                SuggestedCanonicalCharacterId, SuggestedCanonicalName, Confidence, Evidence,
                Reason, Status, CreatedAt, UpdatedAt
            )
            VALUES (
                @candidateName, @candidateInternalName, @sourceModId, @sourceModName,
                @suggestedCanonicalCharacterId, @suggestedCanonicalName, @confidence, @evidence,
                @reason, 'Pending', @now, @now
            );
            ";
        command.Parameters.AddWithValue("@candidateName", candidate.Name);
        command.Parameters.AddWithValue("@candidateInternalName", candidate.Name);
        command.Parameters.AddWithValue("@sourceModId", candidate.SourceModId);
        command.Parameters.AddWithValue("@sourceModName", candidate.SourceModName);
        command.Parameters.AddWithValue("@suggestedCanonicalCharacterId", (object?)match.CanonicalCharacterId ?? DBNull.Value);
        command.Parameters.AddWithValue("@suggestedCanonicalName", (object?)suggestedName ?? DBNull.Value);
        command.Parameters.AddWithValue("@confidence", match.Confidence);
        command.Parameters.AddWithValue("@evidence", candidate.Evidence.ToString());
        command.Parameters.AddWithValue("@reason", match.Reason);
        command.Parameters.AddWithValue("@now", now);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>Returns groups of character rows that share the same name (potential duplicates).</summary>
    public async Task<IReadOnlyList<DuplicateCharacterGroup>> GetDuplicateNameGroupsAsync()
    {
        Dictionary<string, List<DuplicateCharacterEntry>> groups = new(StringComparer.OrdinalIgnoreCase);
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            SELECT Id, Name, CanonicalCharacterId, SourceModId, SourceModName, IsActive
            FROM Characters
            WHERE Name COLLATE NOCASE IN (
                SELECT Name FROM Characters GROUP BY Name COLLATE NOCASE HAVING COUNT(*) > 1
            )
            ORDER BY Name COLLATE NOCASE, IsActive DESC, Id;
            ";

        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            DuplicateCharacterEntry entry = new()
            {
                Id = reader.GetInt64(0),
                Name = reader.GetString(1),
                CanonicalCharacterId = reader.IsDBNull(2) ? null : reader.GetInt64(2),
                SourceModId = reader.IsDBNull(3) ? null : reader.GetString(3),
                SourceModName = reader.IsDBNull(4) ? null : reader.GetString(4),
                IsActive = reader.GetInt32(5) == 1
            };
            if (!groups.TryGetValue(entry.Name, out List<DuplicateCharacterEntry>? list))
            {
                list = new List<DuplicateCharacterEntry>();
                groups[entry.Name] = list;
            }
            list.Add(entry);
        }

        return groups
            .Select(group => new DuplicateCharacterGroup { Name = group.Value[0].Name, Characters = group.Value })
            .OrderBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Merges every character row sharing <paramref name="name"/> into the chosen keeper row:
    /// repoints canonical-keyed data to the keeper's canonical character, deletes the redundant
    /// character rows, records merge rules/aliases, and refreshes canonical activity.
    /// </summary>
    public async Task<int> MergeByNameAsync(string name, long primaryCharacterId)
    {
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        List<(long Id, long? Canonical, string CharName, string? SourceModId)> rows = new();
        await using (SqliteCommand select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = "SELECT Id, CanonicalCharacterId, Name, SourceModId FROM Characters WHERE Name COLLATE NOCASE = @name;";
            select.Parameters.AddWithValue("@name", name);
            await using SqliteDataReader reader = await select.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                rows.Add((reader.GetInt64(0), reader.IsDBNull(1) ? null : reader.GetInt64(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3)));
        }

        (long Id, long? Canonical, string CharName, string? SourceModId) primary = rows.FirstOrDefault(row => row.Id == primaryCharacterId);
        if (primary.Id == 0)
        {
            await transaction.RollbackAsync();
            return 0;
        }

        long targetCanonical = primary.Canonical ?? await EnsureCanonicalAsync(connection, primary.CharName, transaction);
        int mergedCount = 0;

        foreach ((long Id, long? Canonical, string CharName, string? SourceModId) row in rows)
        {
            if (row.Id == primary.Id)
                continue;

            if (row.Canonical is long sourceCanonical && sourceCanonical != targetCanonical)
                await RepointCanonicalAsync(connection, transaction, sourceCanonical, targetCanonical);

            // Record the merge so future scans resolve this name/mod to the keeper's canonical.
            await AddMergeRuleAsync(connection, targetCanonical, row.CharName, row.SourceModId ?? "", row.CharName, "Merge", 100, "User", transaction);
            await AddAliasAsync(connection, targetCanonical, row.CharName, row.SourceModId, 100, transaction);

            await using SqliteCommand delete = connection.CreateCommand();
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM Characters WHERE Id = @id;";
            delete.Parameters.AddWithValue("@id", row.Id);
            await delete.ExecuteNonQueryAsync();
            mergedCount++;
        }

        // Ensure the keeper points at the target canonical.
        await using (SqliteCommand update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = "UPDATE Characters SET CanonicalCharacterId = @target WHERE Id = @id;";
            update.Parameters.AddWithValue("@target", targetCanonical);
            update.Parameters.AddWithValue("@id", primary.Id);
            await update.ExecuteNonQueryAsync();
        }

        await RefreshCanonicalActivityAsync(connection, transaction);
        await transaction.CommitAsync();
        return mergedCount;
    }

    private static async Task RefreshCanonicalActivityAsync(SqliteConnection connection, SqliteTransaction? transaction = null)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
            UPDATE CanonicalCharacters
            SET IsActive = CASE
                    WHEN EXISTS (
                        SELECT 1 FROM Characters
                        WHERE Characters.CanonicalCharacterId = CanonicalCharacters.Id
                          AND Characters.IsActive = 1
                    )
                    THEN 1 ELSE 0 END,
                UpdatedAt = @updatedAt;
            ";
        command.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync();
    }

    // Repoints all canonical-keyed data from a source canonical to the target, then drops any rows
    // that could not move because of a unique constraint (they are now redundant duplicates).
    private static async Task RepointCanonicalAsync(SqliteConnection connection, SqliteTransaction transaction, long source, long target)
    {
        (string Table, bool MayConflict)[] tables =
        {
            ("CharacterSources", true),
            ("DialogueSources", true),
            ("CharacterAliases", true),
            ("GeneratedDialogueOverrides", false),
            ("DialogueSourceSummaries", false),
            ("CharacterMergeRules", false),
            ("PlayerProfileRelationships", false),
            ("PlayerProfileMemories", false)
        };

        foreach ((string table, bool mayConflict) in tables)
        {
            string verb = mayConflict ? "UPDATE OR IGNORE" : "UPDATE";
            await using (SqliteCommand move = connection.CreateCommand())
            {
                move.Transaction = transaction;
                move.CommandText = $"{verb} {table} SET CanonicalCharacterId = @target WHERE CanonicalCharacterId = @source;";
                move.Parameters.AddWithValue("@target", target);
                move.Parameters.AddWithValue("@source", source);
                await move.ExecuteNonQueryAsync();
            }

            if (mayConflict)
            {
                await using SqliteCommand cleanup = connection.CreateCommand();
                cleanup.Transaction = transaction;
                cleanup.CommandText = $"DELETE FROM {table} WHERE CanonicalCharacterId = @source;";
                cleanup.Parameters.AddWithValue("@source", source);
                await cleanup.ExecuteNonQueryAsync();
            }
        }

        await using SqliteCommand queue = connection.CreateCommand();
        queue.Transaction = transaction;
        queue.CommandText = "UPDATE CharacterMergeReviewQueue SET SuggestedCanonicalCharacterId = @target WHERE SuggestedCanonicalCharacterId = @source;";
        queue.Parameters.AddWithValue("@target", target);
        queue.Parameters.AddWithValue("@source", source);
        await queue.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<CharacterMergeReviewItem>> GetMergeReviewQueueAsync()
    {
        List<CharacterMergeReviewItem> items = new();
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            SELECT Id, CandidateName, CandidateInternalName, SourceModId, SourceModName,
                   SuggestedCanonicalCharacterId, SuggestedCanonicalName, Confidence, Evidence,
                   Reason, Status, CreatedAt, UpdatedAt
            FROM CharacterMergeReviewQueue
            ORDER BY Status, Confidence DESC, CandidateName;
            ";

        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            items.Add(new CharacterMergeReviewItem
            {
                Id = reader.GetInt64(0),
                CandidateName = reader.GetString(1),
                CandidateInternalName = reader.IsDBNull(2) ? null : reader.GetString(2),
                SourceModId = reader.GetString(3),
                SourceModName = reader.IsDBNull(4) ? null : reader.GetString(4),
                SuggestedCanonicalCharacterId = reader.IsDBNull(5) ? null : reader.GetInt64(5),
                SuggestedCanonicalName = reader.IsDBNull(6) ? null : reader.GetString(6),
                Confidence = reader.GetInt32(7),
                Evidence = reader.GetString(8),
                Reason = reader.GetString(9),
                Status = reader.GetString(10),
                CreatedAt = DateTime.Parse(reader.GetString(11)),
                UpdatedAt = DateTime.Parse(reader.GetString(12))
            });
        }

        return items;
    }

    public async Task RefreshActivityFromCharactersAsync()
    {
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            UPDATE CanonicalCharacters
            SET IsActive = CASE
                    WHEN EXISTS (
                        SELECT 1 FROM Characters
                        WHERE Characters.CanonicalCharacterId = CanonicalCharacters.Id
                          AND Characters.IsActive = 1
                    )
                    THEN 1 ELSE 0 END,
                UpdatedAt = @updatedAt;
            ";
        command.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync();
    }

    public async Task ApplyDecisionAsync(long reviewId, CanonicalMergeDecision decision)
    {
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        CharacterMergeReviewItem? item = (await this.GetMergeReviewQueueAsync()).FirstOrDefault(x => x.Id == reviewId);
        if (item is null)
            return;

        string action = decision.Action.Trim();
        long? canonicalId = decision.CanonicalCharacterId;

        if (action.Equals("CreateNew", StringComparison.OrdinalIgnoreCase))
            canonicalId = await EnsureCanonicalAsync(connection, item.CandidateName);

        if ((action.Equals("Merge", StringComparison.OrdinalIgnoreCase)
                || action.Equals("MarkExtension", StringComparison.OrdinalIgnoreCase)
                || action.Equals("CreateAlias", StringComparison.OrdinalIgnoreCase))
            && canonicalId is long resolvedCanonicalId)
        {
            await AddMergeRuleAsync(connection, resolvedCanonicalId, item.CandidateName, item.SourceModId, item.CandidateInternalName, action, 100, "User");
            await UpsertSourceAsync(connection, resolvedCanonicalId, item.SourceModId,
                action.Equals("MarkExtension", StringComparison.OrdinalIgnoreCase) ? "DialogueExpansion" : "BaseDefinition",
                action.Equals("MarkExtension", StringComparison.OrdinalIgnoreCase) ? 80 : 50,
                "User merge review decision.");

            if (action.Equals("CreateAlias", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrWhiteSpace(decision.Alias))
                await AddAliasAsync(connection, resolvedCanonicalId, decision.Alias ?? item.CandidateName, item.SourceModId, 100);

            if (decision.LockDecision)
                await LockCanonicalAsync(connection, resolvedCanonicalId);
        }

        await UpdateReviewStatusAsync(connection, reviewId, action.Equals("Ignore", StringComparison.OrdinalIgnoreCase) ? "Ignored" : "Resolved");
    }

    private static async Task<CanonicalCharacter?> MatchUserRuleAsync(SqliteConnection connection, CharacterCandidate candidate)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            SELECT c.Id, c.CanonicalName, c.DisplayName, c.IsActive, c.CreatedAt, c.UpdatedAt, c.CanonPriority, c.UserLocked
            FROM CharacterMergeRules r
            INNER JOIN CanonicalCharacters c ON c.Id = r.CanonicalCharacterId
            WHERE (r.MatchName IS NULL OR r.MatchName = @name)
              AND (r.MatchSourceModId IS NULL OR r.MatchSourceModId = @sourceModId)
              AND (r.MatchInternalName IS NULL OR r.MatchInternalName = @name)
            ORDER BY r.Confidence DESC
            LIMIT 1;
            ";
        command.Parameters.AddWithValue("@name", candidate.Name);
        command.Parameters.AddWithValue("@sourceModId", candidate.SourceModId);

        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapCanonical(reader) : null;
    }

    private static async Task<CanonicalCharacter?> GetByNameOrAliasAsync(SqliteConnection connection, string name, SqliteTransaction? transaction = null)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
            SELECT Id, CanonicalName, DisplayName, IsActive, CreatedAt, UpdatedAt, CanonPriority, UserLocked
            FROM CanonicalCharacters
            WHERE CanonicalName = @name COLLATE NOCASE OR DisplayName = @name COLLATE NOCASE
            UNION
            SELECT c.Id, c.CanonicalName, c.DisplayName, c.IsActive, c.CreatedAt, c.UpdatedAt, c.CanonPriority, c.UserLocked
            FROM CharacterAliases a
            INNER JOIN CanonicalCharacters c ON c.Id = a.CanonicalCharacterId
            WHERE a.Alias = @name COLLATE NOCASE
            LIMIT 1;
            ";
        command.Parameters.AddWithValue("@name", name);

        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapCanonical(reader) : null;
    }

    private static async Task<long> EnsureCanonicalAsync(SqliteConnection connection, string name, SqliteTransaction? transaction = null)
    {
        CanonicalCharacter? existing = await GetByNameOrAliasAsync(connection, name, transaction);
        if (existing is not null)
            return existing.Id;

        string now = DateTime.UtcNow.ToString("O");
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
            INSERT INTO CanonicalCharacters (CanonicalName, DisplayName, IsActive, CreatedAt, UpdatedAt, CanonPriority, UserLocked)
            VALUES (@name, @name, 1, @now, @now, 0, 0);
            SELECT last_insert_rowid();
            ";
        command.Parameters.AddWithValue("@name", name);
        command.Parameters.AddWithValue("@now", now);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task UpsertSourceAsync(SqliteConnection connection, long canonicalCharacterId, string sourceModId, string sourceType, int priority, string notes)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO CharacterSources (CanonicalCharacterId, SourceModId, SourceType, Priority, Notes)
            VALUES (@canonicalCharacterId, @sourceModId, @sourceType, @priority, @notes)
            ON CONFLICT(CanonicalCharacterId, SourceModId, SourceType) DO UPDATE SET
                Priority = MAX(Priority, excluded.Priority),
                Notes = excluded.Notes;
            ";
        command.Parameters.AddWithValue("@canonicalCharacterId", canonicalCharacterId);
        command.Parameters.AddWithValue("@sourceModId", sourceModId);
        command.Parameters.AddWithValue("@sourceType", sourceType);
        command.Parameters.AddWithValue("@priority", priority);
        command.Parameters.AddWithValue("@notes", notes);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task AddMergeRuleAsync(SqliteConnection connection, long canonicalCharacterId, string matchName, string sourceModId, string? internalName, string ruleType, int confidence, string createdBy, SqliteTransaction? transaction = null)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
            INSERT INTO CharacterMergeRules (CanonicalCharacterId, MatchName, MatchSourceModId, MatchUniqueId, MatchInternalName, RuleType, Confidence, CreatedBy, CreatedAt)
            VALUES (@canonicalCharacterId, @matchName, @sourceModId, NULL, @internalName, @ruleType, @confidence, @createdBy, @createdAt);
            ";
        command.Parameters.AddWithValue("@canonicalCharacterId", canonicalCharacterId);
        command.Parameters.AddWithValue("@matchName", matchName);
        command.Parameters.AddWithValue("@sourceModId", sourceModId);
        command.Parameters.AddWithValue("@internalName", (object?)internalName ?? DBNull.Value);
        command.Parameters.AddWithValue("@ruleType", ruleType);
        command.Parameters.AddWithValue("@confidence", confidence);
        command.Parameters.AddWithValue("@createdBy", createdBy);
        command.Parameters.AddWithValue("@createdAt", DateTime.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task AddAliasAsync(SqliteConnection connection, long canonicalCharacterId, string alias, string? sourceModId, int confidence, SqliteTransaction? transaction = null)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
            INSERT OR IGNORE INTO CharacterAliases (CanonicalCharacterId, Alias, SourceModId, Confidence)
            VALUES (@canonicalCharacterId, @alias, @sourceModId, @confidence);
            ";
        command.Parameters.AddWithValue("@canonicalCharacterId", canonicalCharacterId);
        command.Parameters.AddWithValue("@alias", alias);
        command.Parameters.AddWithValue("@sourceModId", (object?)sourceModId ?? DBNull.Value);
        command.Parameters.AddWithValue("@confidence", confidence);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task LockCanonicalAsync(SqliteConnection connection, long canonicalCharacterId)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "UPDATE CanonicalCharacters SET UserLocked = 1, UpdatedAt = @updatedAt WHERE Id = @id;";
        command.Parameters.AddWithValue("@id", canonicalCharacterId);
        command.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task UpdateReviewStatusAsync(SqliteConnection connection, long reviewId, string status)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "UPDATE CharacterMergeReviewQueue SET Status = @status, UpdatedAt = @updatedAt WHERE Id = @id;";
        command.Parameters.AddWithValue("@id", reviewId);
        command.Parameters.AddWithValue("@status", status);
        command.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string?> GetCanonicalNameAsync(SqliteConnection connection, long canonicalCharacterId)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT CanonicalName FROM CanonicalCharacters WHERE Id = @id LIMIT 1;";
        command.Parameters.AddWithValue("@id", canonicalCharacterId);
        return (await command.ExecuteScalarAsync())?.ToString();
    }

    private static CanonicalCharacter MapCanonical(SqliteDataReader reader)
    {
        return new CanonicalCharacter
        {
            Id = reader.GetInt64(0),
            CanonicalName = reader.GetString(1),
            DisplayName = reader.GetString(2),
            IsActive = reader.GetInt32(3) == 1,
            CreatedAt = DateTime.Parse(reader.GetString(4)),
            UpdatedAt = DateTime.Parse(reader.GetString(5)),
            CanonPriority = reader.GetInt32(6),
            UserLocked = reader.GetInt32(7) == 1
        };
    }
}

public sealed record CanonicalMatchResult(long? CanonicalCharacterId, int Confidence, string Reason, string RuleType);

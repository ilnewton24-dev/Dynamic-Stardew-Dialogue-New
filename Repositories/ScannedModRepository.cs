using LivingLoreDialogue.Data;
using LivingLoreDialogue.Models;
using Microsoft.Data.Sqlite;

namespace LivingLoreDialogue.Repositories;

public sealed class ScannedModRepository
{
    private readonly SqliteConnectionFactory connectionFactory;

    public ScannedModRepository(SqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<ScannedMod>> GetAllAsync(bool includeInactive = true)
    {
        List<ScannedMod> mods = new();
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $@"
            SELECT Id, UniqueId, Name, Version, Author, IsActive, LastScanTime
            FROM ScannedMods
            {(includeInactive ? "" : "WHERE IsActive = 1")}
            ORDER BY Name;
            ";

        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            mods.Add(new ScannedMod
            {
                Id = reader.GetInt64(0),
                UniqueId = reader.GetString(1),
                Name = reader.GetString(2),
                Version = reader.IsDBNull(3) ? null : reader.GetString(3),
                Author = reader.IsDBNull(4) ? null : reader.GetString(4),
                IsActive = reader.GetInt32(5) == 1,
                LastScanTime = DateTime.Parse(reader.GetString(6))
            });
        }

        return mods;
    }

    public async Task<int> CountAsync()
    {
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM ScannedMods;";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    public async Task<int> CountActiveAsync()
    {
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM ScannedMods WHERE IsActive = 1;";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    public async Task UpsertAsync(ScannedMod mod)
    {
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO ScannedMods (UniqueId, Name, Version, Author, IsActive, LastScanTime)
            VALUES (@uniqueId, @name, @version, @author, @isActive, @lastScanTime)
            ON CONFLICT(UniqueId) DO UPDATE SET
                Name = excluded.Name,
                Version = excluded.Version,
                Author = excluded.Author,
                IsActive = excluded.IsActive,
                LastScanTime = excluded.LastScanTime;
            ";
        BindMod(command, mod);
        await command.ExecuteNonQueryAsync();
    }

    public async Task UpsertRangeAsync(IEnumerable<ScannedMod> mods)
    {
        List<ScannedMod> list = mods.ToList();
        if (list.Count == 0)
            return;

        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();
        await using SqliteTransaction transaction = connection.BeginTransaction();

        try
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
                INSERT INTO ScannedMods (UniqueId, Name, Version, Author, IsActive, LastScanTime)
                VALUES (@uniqueId, @name, @version, @author, @isActive, @lastScanTime)
                ON CONFLICT(UniqueId) DO UPDATE SET
                    Name = excluded.Name,
                    Version = excluded.Version,
                    Author = excluded.Author,
                    IsActive = excluded.IsActive,
                    LastScanTime = excluded.LastScanTime;
                ";

            foreach (ScannedMod mod in list)
            {
                command.Parameters.Clear();
                BindMod(command, mod);
                await command.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    /// <summary>Marks any previously-active mod that is no longer present as inactive. Returns the count deactivated.</summary>
    public async Task<int> MarkMissingInactiveAsync(IEnumerable<string> activeUniqueIds, DateTime timestamp)
    {
        HashSet<string> active = new(activeUniqueIds, StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<ScannedMod> existing = await this.GetAllAsync();

        List<ScannedMod> toDeactivate = existing
            .Where(mod => mod.IsActive && !active.Contains(mod.UniqueId))
            .ToList();

        if (toDeactivate.Count == 0)
            return 0;

        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();
        await using SqliteTransaction transaction = connection.BeginTransaction();

        try
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
                UPDATE ScannedMods
                SET IsActive = 0,
                    LastScanTime = @lastScanTime
                WHERE UniqueId = @uniqueId;
                ";

            foreach (ScannedMod mod in toDeactivate)
            {
                command.Parameters.Clear();
                command.Parameters.AddWithValue("@lastScanTime", timestamp.ToString("O"));
                command.Parameters.AddWithValue("@uniqueId", mod.UniqueId);
                await command.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }

        return toDeactivate.Count;
    }

    private static void BindMod(SqliteCommand command, ScannedMod mod)
    {
        command.Parameters.AddWithValue("@uniqueId", mod.UniqueId);
        command.Parameters.AddWithValue("@name", mod.Name);
        command.Parameters.AddWithValue("@version", (object?)mod.Version ?? DBNull.Value);
        command.Parameters.AddWithValue("@author", (object?)mod.Author ?? DBNull.Value);
        command.Parameters.AddWithValue("@isActive", mod.IsActive ? 1 : 0);
        command.Parameters.AddWithValue("@lastScanTime", mod.LastScanTime.ToString("O"));
    }
}

using LivingLoreDialogue.Data;
using LivingLoreDialogue.Models;
using Microsoft.Data.Sqlite;

namespace LivingLoreDialogue.Repositories;

public sealed class ScanFileCacheRepository
{
    private readonly SqliteConnectionFactory connectionFactory;

    public ScanFileCacheRepository(SqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task<ScanFileCacheEntry?> GetAsync(string cacheKind, string filePath)
    {
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            SELECT CacheKind, FilePath, SourceModId, LastWriteUtcTicks, FileSize, ContentHash, PayloadJson, UpdatedAt
            FROM ScanFileCache
            WHERE CacheKind = @cacheKind AND FilePath = @filePath
            LIMIT 1;
            ";
        command.Parameters.AddWithValue("@cacheKind", cacheKind);
        command.Parameters.AddWithValue("@filePath", filePath);

        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        return new ScanFileCacheEntry
        {
            CacheKind = reader.GetString(0),
            FilePath = reader.GetString(1),
            SourceModId = reader.IsDBNull(2) ? null : reader.GetString(2),
            LastWriteUtcTicks = reader.GetInt64(3),
            FileSize = reader.GetInt64(4),
            ContentHash = reader.GetString(5),
            PayloadJson = reader.GetString(6),
            UpdatedAt = DateTime.Parse(reader.GetString(7))
        };
    }

    public async Task UpsertAsync(ScanFileCacheEntry entry)
    {
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO ScanFileCache (
                CacheKind, FilePath, SourceModId, LastWriteUtcTicks, FileSize, ContentHash, PayloadJson, UpdatedAt
            )
            VALUES (
                @cacheKind, @filePath, @sourceModId, @lastWriteUtcTicks, @fileSize, @contentHash, @payloadJson, @updatedAt
            )
            ON CONFLICT(CacheKind, FilePath) DO UPDATE SET
                SourceModId = excluded.SourceModId,
                LastWriteUtcTicks = excluded.LastWriteUtcTicks,
                FileSize = excluded.FileSize,
                ContentHash = excluded.ContentHash,
                PayloadJson = excluded.PayloadJson,
                UpdatedAt = excluded.UpdatedAt;
            ";
        command.Parameters.AddWithValue("@cacheKind", entry.CacheKind);
        command.Parameters.AddWithValue("@filePath", entry.FilePath);
        command.Parameters.AddWithValue("@sourceModId", (object?)entry.SourceModId ?? DBNull.Value);
        command.Parameters.AddWithValue("@lastWriteUtcTicks", entry.LastWriteUtcTicks);
        command.Parameters.AddWithValue("@fileSize", entry.FileSize);
        command.Parameters.AddWithValue("@contentHash", entry.ContentHash);
        command.Parameters.AddWithValue("@payloadJson", entry.PayloadJson);
        command.Parameters.AddWithValue("@updatedAt", entry.UpdatedAt.ToString("O"));
        await command.ExecuteNonQueryAsync();
    }

    public async Task<int> DeleteMissingAsync(string cacheKind, IEnumerable<string> activeFilePaths)
    {
        HashSet<string> active = activeFilePaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        List<string> cached = new();
        await using (SqliteCommand select = connection.CreateCommand())
        {
            select.CommandText = "SELECT FilePath FROM ScanFileCache WHERE CacheKind = @cacheKind;";
            select.Parameters.AddWithValue("@cacheKind", cacheKind);
            await using SqliteDataReader reader = await select.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                cached.Add(reader.GetString(0));
        }

        int deleted = 0;
        foreach (string filePath in cached.Where(path => !active.Contains(path)))
        {
            await using SqliteCommand delete = connection.CreateCommand();
            delete.CommandText = "DELETE FROM ScanFileCache WHERE CacheKind = @cacheKind AND FilePath = @filePath;";
            delete.Parameters.AddWithValue("@cacheKind", cacheKind);
            delete.Parameters.AddWithValue("@filePath", filePath);
            deleted += await delete.ExecuteNonQueryAsync();
        }

        return deleted;
    }
}

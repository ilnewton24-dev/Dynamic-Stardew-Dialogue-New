using LivingLoreDialogue.Data;
using LivingLoreDialogue.Models;
using Microsoft.Data.Sqlite;

namespace LivingLoreDialogue.Repositories;

public sealed class ScanHistoryRepository
{
    private readonly SqliteConnectionFactory connectionFactory;

    public ScanHistoryRepository(SqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task<long> AddAsync(string triggerSource, ModScanSummary summary)
    {
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO ScanHistory (
                TriggerSource, StartedAt, CompletedAt, Success, ModsScanned, CharactersFound,
                CharactersAdded, CharactersUpdated, CharactersReactivated, CharactersMarkedInactive,
                ConflictsFound, ErrorMessage
            )
            VALUES (
                @triggerSource, @startedAt, @completedAt, @success, @modsScanned, @charactersFound,
                @charactersAdded, @charactersUpdated, @charactersReactivated, @charactersMarkedInactive,
                @conflictsFound, @errorMessage
            );
            SELECT last_insert_rowid();
            ";
        command.Parameters.AddWithValue("@triggerSource", triggerSource);
        command.Parameters.AddWithValue("@startedAt", summary.StartedAt.ToString("O"));
        command.Parameters.AddWithValue("@completedAt", summary.CompletedAt.ToString("O"));
        command.Parameters.AddWithValue("@success", summary.Success ? 1 : 0);
        command.Parameters.AddWithValue("@modsScanned", summary.ModsScanned);
        command.Parameters.AddWithValue("@charactersFound", summary.CharactersFound);
        command.Parameters.AddWithValue("@charactersAdded", summary.CharactersAdded);
        command.Parameters.AddWithValue("@charactersUpdated", summary.CharactersUpdated);
        command.Parameters.AddWithValue("@charactersReactivated", summary.CharactersReactivated);
        command.Parameters.AddWithValue("@charactersMarkedInactive", summary.CharactersMarkedInactive);
        command.Parameters.AddWithValue("@conflictsFound", summary.ConflictsFound);
        command.Parameters.AddWithValue("@errorMessage", summary.Errors.Count > 0 ? string.Join(Environment.NewLine, summary.Errors) : DBNull.Value);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    public async Task<IReadOnlyList<ScanHistoryEntry>> GetRecentAsync(int limit)
    {
        List<ScanHistoryEntry> entries = new();
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            SELECT Id, TriggerSource, StartedAt, CompletedAt, Success, ModsScanned, CharactersFound,
                   CharactersAdded, CharactersUpdated, CharactersReactivated, CharactersMarkedInactive,
                   ConflictsFound, ErrorMessage
            FROM ScanHistory
            ORDER BY StartedAt DESC
            LIMIT @limit;
            ";
        command.Parameters.AddWithValue("@limit", limit);

        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            entries.Add(new ScanHistoryEntry
            {
                Id = reader.GetInt64(0),
                TriggerSource = reader.GetString(1),
                StartedAt = DateTime.Parse(reader.GetString(2)),
                CompletedAt = DateTime.Parse(reader.GetString(3)),
                Success = reader.GetInt32(4) == 1,
                ModsScanned = reader.GetInt32(5),
                CharactersFound = reader.GetInt32(6),
                CharactersAdded = reader.GetInt32(7),
                CharactersUpdated = reader.GetInt32(8),
                CharactersReactivated = reader.GetInt32(9),
                CharactersMarkedInactive = reader.GetInt32(10),
                ConflictsFound = reader.GetInt32(11),
                ErrorMessage = reader.IsDBNull(12) ? null : reader.GetString(12)
            });
        }

        return entries;
    }
}

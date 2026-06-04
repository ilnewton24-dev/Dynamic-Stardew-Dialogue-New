using LivingLoreDialogue.Data;
using LivingLoreDialogue.Models;
using Microsoft.Data.Sqlite;

namespace LivingLoreDialogue.Repositories;

public sealed class EventRepository
{
    private readonly SqliteConnectionFactory connectionFactory;

    public EventRepository(SqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<LoreEvent>> GetRecentAsync(int limit = 10)
    {
        List<LoreEvent> events = new();
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            SELECT Id, Title, Description, DateOccurred
            FROM Events
            ORDER BY Id DESC
            LIMIT @limit;
            ";
        command.Parameters.AddWithValue("@limit", limit);

        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            events.Add(new LoreEvent
            {
                Id = reader.GetInt64(0),
                Title = reader.GetString(1),
                Description = reader.GetString(2),
                DateOccurred = reader.GetString(3)
            });
        }

        return events;
    }
}

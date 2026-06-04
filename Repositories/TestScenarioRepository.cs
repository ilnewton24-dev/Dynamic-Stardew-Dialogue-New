using LivingLoreDialogue.Data;
using LivingLoreDialogue.Models;
using Microsoft.Data.Sqlite;

namespace LivingLoreDialogue.Repositories;

public sealed class TestScenarioRepository
{
    private readonly SqliteConnectionFactory connectionFactory;

    public TestScenarioRepository(SqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<TestScenario>> GetAllAsync()
    {
        List<TestScenario> scenarios = new();
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            SELECT Id, Name, PlayerName, FarmName, Year, Season, Weather, Location, FriendshipHearts,
                   RelationshipState, SeenEvents, CompletedQuests, CommunityCenterState, PlayerProfileId,
                   IsBuiltIn, CreatedAt, UpdatedAt
            FROM TestScenarios
            ORDER BY IsBuiltIn DESC, Name;
            ";

        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            scenarios.Add(Map(reader));

        return scenarios;
    }

    public async Task<TestScenario?> GetByIdAsync(long id)
    {
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            SELECT Id, Name, PlayerName, FarmName, Year, Season, Weather, Location, FriendshipHearts,
                   RelationshipState, SeenEvents, CompletedQuests, CommunityCenterState, PlayerProfileId,
                   IsBuiltIn, CreatedAt, UpdatedAt
            FROM TestScenarios
            WHERE Id = @id
            LIMIT 1;
            ";
        command.Parameters.AddWithValue("@id", id);

        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Map(reader) : null;
    }

    public async Task<long> AddAsync(TestScenario scenario)
    {
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO TestScenarios (
                Name, PlayerName, FarmName, Year, Season, Weather, Location, FriendshipHearts,
                RelationshipState, SeenEvents, CompletedQuests, CommunityCenterState, PlayerProfileId, IsBuiltIn,
                CreatedAt, UpdatedAt
            )
            VALUES (
                @name, @playerName, @farmName, @year, @season, @weather, @location, @friendshipHearts,
                @relationshipState, @seenEvents, @completedQuests, @communityCenterState, @playerProfileId, @isBuiltIn,
                @createdAt, @updatedAt
            );
            SELECT last_insert_rowid();
            ";
        BindScenario(command, scenario);
        command.Parameters.AddWithValue("@createdAt", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow.ToString("O"));
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    public async Task UpdateAsync(TestScenario scenario)
    {
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            UPDATE TestScenarios SET
                Name = @name, PlayerName = @playerName, FarmName = @farmName, Year = @year,
                Season = @season, Weather = @weather, Location = @location, FriendshipHearts = @friendshipHearts,
                RelationshipState = @relationshipState, SeenEvents = @seenEvents, CompletedQuests = @completedQuests,
                CommunityCenterState = @communityCenterState, PlayerProfileId = @playerProfileId, UpdatedAt = @updatedAt
            WHERE Id = @id;
            ";
        BindScenario(command, scenario);
        command.Parameters.AddWithValue("@id", scenario.Id);
        command.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync();
    }

    public async Task DeleteAsync(long id)
    {
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "DELETE FROM TestScenarios WHERE Id = @id;";
        command.Parameters.AddWithValue("@id", id);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>Inserts the built-in scenarios once, if the table is empty.</summary>
    public async Task SeedDefaultsAsync()
    {
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using (SqliteCommand countCommand = connection.CreateCommand())
        {
            countCommand.CommandText = "SELECT COUNT(*) FROM TestScenarios;";
            if (Convert.ToInt32(await countCommand.ExecuteScalarAsync()) > 0)
                return;
        }

        foreach (TestScenario scenario in BuiltInScenarios())
            await this.AddAsync(scenario);
    }

    private static IEnumerable<TestScenario> BuiltInScenarios() => new[]
    {
        new TestScenario
        {
            Name = "New Farmer", PlayerName = "Farmer", FarmName = "Green Acres", Year = 1,
            Season = "spring", Weather = "clear", Location = "Town", FriendshipHearts = 0,
            RelationshipState = "Stranger", CommunityCenterState = "Not started", IsBuiltIn = true
        },
        new TestScenario
        {
            Name = "Year 1 Friend", PlayerName = "Farmer", FarmName = "Green Acres", Year = 1,
            Season = "summer", Weather = "sunny", Location = "Town", FriendshipHearts = 6,
            RelationshipState = "Friend", CommunityCenterState = "In progress", IsBuiltIn = true
        },
        new TestScenario
        {
            Name = "Year 2 Dating", PlayerName = "Farmer", FarmName = "Green Acres", Year = 2,
            Season = "fall", Weather = "rain", Location = "Town", FriendshipHearts = 8,
            RelationshipState = "Dating", CommunityCenterState = "In progress", IsBuiltIn = true
        },
        new TestScenario
        {
            Name = "Year 4 Married", PlayerName = "Farmer", FarmName = "Green Acres", Year = 4,
            Season = "winter", Weather = "snow", Location = "Farmhouse", FriendshipHearts = 12,
            RelationshipState = "Married", CommunityCenterState = "Completed", IsBuiltIn = true
        },
        new TestScenario
        {
            Name = "Endgame Farmer", PlayerName = "Farmer", FarmName = "Green Acres", Year = 5,
            Season = "spring", Weather = "clear", Location = "Town", FriendshipHearts = 10,
            RelationshipState = "Friend", CommunityCenterState = "Completed", IsBuiltIn = true
        }
    };

    private static void BindScenario(SqliteCommand command, TestScenario scenario)
    {
        command.Parameters.AddWithValue("@name", scenario.Name);
        command.Parameters.AddWithValue("@playerName", scenario.PlayerName);
        command.Parameters.AddWithValue("@farmName", scenario.FarmName);
        command.Parameters.AddWithValue("@year", scenario.Year);
        command.Parameters.AddWithValue("@season", scenario.Season);
        command.Parameters.AddWithValue("@weather", scenario.Weather);
        command.Parameters.AddWithValue("@location", scenario.Location);
        command.Parameters.AddWithValue("@friendshipHearts", scenario.FriendshipHearts);
        command.Parameters.AddWithValue("@relationshipState", scenario.RelationshipState);
        command.Parameters.AddWithValue("@seenEvents", scenario.SeenEvents ?? "");
        command.Parameters.AddWithValue("@completedQuests", scenario.CompletedQuests ?? "");
        command.Parameters.AddWithValue("@communityCenterState", scenario.CommunityCenterState);
        command.Parameters.AddWithValue("@playerProfileId", (object?)scenario.PlayerProfileId ?? DBNull.Value);
        command.Parameters.AddWithValue("@isBuiltIn", scenario.IsBuiltIn ? 1 : 0);
    }

    private static TestScenario Map(SqliteDataReader reader)
    {
        return new TestScenario
        {
            Id = reader.GetInt64(0),
            Name = reader.GetString(1),
            PlayerName = reader.GetString(2),
            FarmName = reader.GetString(3),
            Year = reader.GetInt32(4),
            Season = reader.GetString(5),
            Weather = reader.GetString(6),
            Location = reader.GetString(7),
            FriendshipHearts = reader.GetInt32(8),
            RelationshipState = reader.GetString(9),
            SeenEvents = reader.GetString(10),
            CompletedQuests = reader.GetString(11),
            CommunityCenterState = reader.GetString(12),
            PlayerProfileId = reader.IsDBNull(13) ? null : reader.GetInt64(13),
            IsBuiltIn = reader.GetInt32(14) == 1,
            CreatedAt = DateTime.Parse(reader.GetString(15)),
            UpdatedAt = DateTime.Parse(reader.GetString(16))
        };
    }
}

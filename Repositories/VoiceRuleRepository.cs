using LivingLoreDialogue.Data;
using LivingLoreDialogue.Models;
using Microsoft.Data.Sqlite;

namespace LivingLoreDialogue.Repositories;

public sealed class VoiceRuleRepository
{
    private readonly SqliteConnectionFactory connectionFactory;

    public VoiceRuleRepository(SqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<VoiceRule>> GetForCharacterAsync(long characterId)
    {
        List<VoiceRule> voiceRules = new();
        await using SqliteConnection connection = this.connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            SELECT Id, CharacterId, RuleText
            FROM VoiceRules
            WHERE CharacterId = @characterId
            ORDER BY Id;
            ";
        command.Parameters.AddWithValue("@characterId", characterId);

        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            voiceRules.Add(new VoiceRule
            {
                Id = reader.GetInt64(0),
                CharacterId = reader.GetInt64(1),
                RuleText = reader.GetString(2)
            });
        }

        return voiceRules;
    }
}

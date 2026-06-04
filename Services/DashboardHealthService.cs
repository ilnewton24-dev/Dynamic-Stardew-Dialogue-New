using System.Reflection;
using LivingLoreDialogue.Data;
using Microsoft.Data.Sqlite;

namespace LivingLoreDialogue.Services;

/// <summary>
/// Backs the dashboard's <c>GET /api/health</c> endpoint. Reports whether the SQLite database
/// is reachable and the running assembly version, so the SMAPI mod can confirm the local
/// dashboard is up before sending dialogue requests.
/// </summary>
public sealed class DashboardHealthService
{
    private readonly SqliteConnectionFactory connectionFactory;

    public DashboardHealthService(SqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task<DashboardHealth> CheckAsync()
    {
        bool databaseConnected = false;
        try
        {
            await using SqliteConnection connection = this.connectionFactory.CreateConnection();
            await connection.OpenAsync();
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT 1;";
            await command.ExecuteScalarAsync();
            databaseConnected = true;
        }
        catch
        {
            databaseConnected = false;
        }

        string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
        return new DashboardHealth("ok", databaseConnected, version);
    }
}

public sealed record DashboardHealth(string Status, bool DatabaseConnected, string Version);

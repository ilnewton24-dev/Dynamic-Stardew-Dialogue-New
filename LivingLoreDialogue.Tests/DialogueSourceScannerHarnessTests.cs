using LivingLoreDialogue.Data;
using LivingLoreDialogue.Models;
using LivingLoreDialogue.Repositories;
using LivingLoreDialogue.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LivingLoreDialogue.Tests;

public sealed class DialogueSourceScannerHarnessTests
{
    [Fact]
    public async Task LargeModFolderScanContinuesPastMalformedJsonAndUsesCache()
    {
        using TempWorkspace workspace = TempWorkspace.Create();
        await workspace.InitializeDatabaseAsync();
        await workspace.InsertCanonicalAsync("Abigail");

        for (int i = 0; i < 205; i++)
        {
            string mod = workspace.CreateMod($"Author.Mod{i:000}", $"Mod {i:000}");
            File.WriteAllText(Path.Combine(mod, "content.json"), $$"""
            {
              "Changes": [
                {
                  "Action": "EditData",
                  "Target": "Characters/Dialogue/Abigail",
                  "Entries": {
                    "spring_{{i}}": "Line {{i}} from a large mod set."
                  }
                }
              ]
            }
            """);
        }

        string malformed = workspace.CreateMod("Author.Malformed", "Malformed");
        File.WriteAllText(Path.Combine(malformed, "content.json"), "{ \"Changes\": [ { \"Target\": \"Characters/Dialogue/Abigail\", \"Entries\": { \"bad\": ");

        string large = workspace.CreateMod("Author.Large", "Large");
        File.WriteAllText(Path.Combine(large, "content.json"), $$"""
        {
          "Changes": [
            {
              "Action": "EditData",
              "Target": "Characters/Dialogue/Abigail",
              "Entries": {
                "big": "{{new string('x', 250_000)}}"
              }
            }
          ]
        }
        """);

        DialogueSourceScannerService scanner = workspace.CreateScanner(new ScanOptions
        {
            ScanTimeoutSeconds = 30,
            PerFileParseTimeoutMs = 1000,
            EnableScanCache = true
        });

        DialogueSourceScanSummary first = await scanner.ScanAsync(workspace.ModsPath);
        Assert.False(first.TimedOut);
        Assert.True(first.TotalFilesQueued >= 207);
        Assert.True(first.FilesFailed >= 1);
        Assert.Contains(first.Errors, error => error.Contains("Malformed", StringComparison.OrdinalIgnoreCase));
        Assert.True(first.SourcesFound >= 205);

        DialogueSourceScanSummary second = await scanner.ScanAsync(workspace.ModsPath);
        Assert.False(second.TimedOut);
        Assert.True(second.FilesSkippedFromCache >= 205);
    }

    [Fact]
    public async Task TimeoutReturnsPartialProgressWithoutThrowing()
    {
        using TempWorkspace workspace = TempWorkspace.Create();
        await workspace.InitializeDatabaseAsync();
        await workspace.InsertCanonicalAsync("Abigail");

        for (int i = 0; i < 25; i++)
        {
            string mod = workspace.CreateMod($"Author.Timeout{i:000}", $"Timeout {i:000}");
            File.WriteAllText(Path.Combine(mod, "content.json"), $$"""
            {
              "Changes": [
                {
                  "Action": "EditData",
                  "Target": "Characters/Dialogue/Abigail",
                  "Entries": { "spring_{{i}}": "Timeout recovery line {{i}}." }
                }
              ]
            }
            """);
        }

        DialogueSourceScannerService scanner = workspace.CreateScanner(new ScanOptions
        {
            ScanTimeoutSeconds = 1,
            PerFileParseTimeoutMs = 1000,
            EnableScanCache = true,
            MaxDialogueFilesPerScan = 25
        });

        DialogueSourceScanSummary summary = await scanner.ScanAsync(workspace.ModsPath);
        Assert.False(summary.DatabaseStatePartial && string.IsNullOrWhiteSpace(summary.TimedOutPhase));
        Assert.True(summary.TotalFilesQueued <= 25);
    }

    private sealed class TempWorkspace : IDisposable
    {
        private TempWorkspace(string root)
        {
            this.Root = root;
            this.ModsPath = Path.Combine(root, "Mods");
            Directory.CreateDirectory(this.ModsPath);
            this.ConnectionFactory = new SqliteConnectionFactory(Path.Combine(root, "test.db"));
        }

        public string Root { get; }
        public string ModsPath { get; }
        public SqliteConnectionFactory ConnectionFactory { get; }

        public static TempWorkspace Create()
        {
            return new TempWorkspace(Path.Combine(Path.GetTempPath(), "LivingLoreScanTests", Guid.NewGuid().ToString("N")));
        }

        public async Task InitializeDatabaseAsync()
        {
            string schemaPath = FindSchemaPath();
            DatabaseInitializer initializer = new(this.ConnectionFactory);
            await initializer.InitializeAsync(schemaPath, Path.Combine(this.Root, "missing-seed.sql"), seedOnFirstRun: false);
        }

        public async Task InsertCanonicalAsync(string name)
        {
            await using SqliteConnection connection = this.ConnectionFactory.CreateConnection();
            await connection.OpenAsync();
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO CanonicalCharacters (CanonicalName, DisplayName, IsActive, CreatedAt, UpdatedAt, CanonPriority, UserLocked)
                VALUES (@name, @name, 1, @now, @now, 0, 0);
                ";
            command.Parameters.AddWithValue("@name", name);
            command.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("O"));
            await command.ExecuteNonQueryAsync();
        }

        public string CreateMod(string uniqueId, string name)
        {
            string folder = Path.Combine(this.ModsPath, uniqueId);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "manifest.json"), $$"""
            {
              "Name": "{{name}}",
              "UniqueID": "{{uniqueId}}",
              "Version": "1.0.0",
              "Author": "Tests"
            }
            """);
            return folder;
        }

        public DialogueSourceScannerService CreateScanner(ScanOptions options)
        {
            CanonicalCharacterRepository canonical = new(this.ConnectionFactory);
            DialogueSourceRepository sources = new(this.ConnectionFactory);
            ScanFileCacheRepository cache = new(this.ConnectionFactory);
            return new DialogueSourceScannerService(canonical, sources, cache, options);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(this.Root, recursive: true);
            }
            catch
            {
                // Best-effort cleanup for test temp files.
            }
        }

        private static string FindSchemaPath()
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            while (directory is not null)
            {
                string candidate = Path.Combine(directory.FullName, "Data", "schema.sql");
                if (File.Exists(candidate))
                    return candidate;

                directory = directory.Parent;
            }

            throw new FileNotFoundException("Could not locate Data/schema.sql.");
        }
    }
}

using System.Text.Json;
using System.Threading.Channels;
using LivingLoreDialogue.Data;
using LivingLoreDialogue.Models;
using LivingLoreDialogue.Repositories;
using LivingLoreDialogue.Services;
using LivingLoreDialogue.Web;

var dashboardStartupSw = System.Diagnostics.Stopwatch.StartNew();
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Port is configurable so the SMAPI mod can pass LocalDashboardPort, but the dashboard always
// binds to localhost only (never exposed off-machine).
int dashboardPort = 5077;
string? portSetting = Environment.GetEnvironmentVariable("LIVINGLORE_DASHBOARD_PORT")
    ?? builder.Configuration["LivingLore:DashboardPort"];
if (int.TryParse(portSetting, out int parsedPort) && parsedPort is > 0 and < 65536)
    dashboardPort = parsedPort;

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(dashboardPort);
});

builder.Services.AddRazorPages();
builder.Services.AddHttpClient();

LivingLoreWebOptions options = builder.Configuration.GetSection("LivingLore").Get<LivingLoreWebOptions>() ?? new();
string contentRoot = builder.Environment.ContentRootPath;
string databasePath = Path.GetFullPath(options.DatabasePath, contentRoot);
string schemaPath = Path.GetFullPath(options.SchemaPath, contentRoot);
string seedPath = Path.GetFullPath(options.SeedPath, contentRoot);

options.DatabasePath = databasePath;
options.SchemaPath = schemaPath;
options.SeedPath = seedPath;
options.ApiKeyFilePath = Path.GetFullPath(options.ApiKeyFilePath, contentRoot);
options.ResolvedOpenAiApiKey = ResolveOpenAiApiKey(options, builder.Configuration);
builder.Services.AddSingleton(options);
builder.Services.AddSingleton(new ScanOptions
{
    ScanTimeoutSeconds = options.ScanTimeoutSeconds,
    PerFileParseTimeoutMs = options.PerFileParseTimeoutMs,
    EnableScanCache = options.EnableScanCache,
    MaxDialogueFilesPerScan = options.MaxDialogueFilesPerScan
});
builder.Services.AddSingleton(new SqliteConnectionFactory(databasePath));
builder.Services.AddScoped<DatabaseInitializer>();
builder.Services.AddScoped<CharacterRepository>();
builder.Services.AddScoped<RelationshipRepository>();
builder.Services.AddScoped<EventRepository>();
builder.Services.AddScoped<MemoryRepository>();
builder.Services.AddScoped<VoiceRuleRepository>();
builder.Services.AddScoped<UserLoreOverrideRepository>();
builder.Services.AddScoped<LoreChangeLogRepository>();
builder.Services.AddScoped<GeneratedDialogueHistoryRepository>();
builder.Services.AddScoped<ScannedModRepository>();
builder.Services.AddScoped<LoreConflictRepository>();
builder.Services.AddScoped<AppSettingsRepository>();
builder.Services.AddScoped<CharacterHistoryRepository>();
builder.Services.AddScoped<ScanHistoryRepository>();
builder.Services.AddScoped<CharacterValidationRepository>();
builder.Services.AddScoped<CanonicalCharacterRepository>();
builder.Services.AddScoped<DialogueSourceRepository>();
builder.Services.AddScoped<ScanFileCacheRepository>();
builder.Services.AddScoped<GeneratedDialogueOverrideRepository>();
builder.Services.AddScoped<DialogueGenerationTraceRepository>();
builder.Services.AddScoped<TestScenarioRepository>();
builder.Services.AddScoped<PlayerProfileRepository>();
builder.Services.AddScoped<DialogueExplanationService>();
builder.Services.AddScoped<GameSimulationService>();
builder.Services.AddScoped<DashboardHealthService>();
builder.Services.AddSingleton<DashboardScanRunService>();
builder.Services.AddScoped<PromptBuilder>();
builder.Services.AddScoped<ModScannerService>();
builder.Services.AddScoped<VanillaCharacterScannerService>();
builder.Services.AddScoped<CharacterValidationService>();
builder.Services.AddScoped<CharacterSyncService>();
builder.Services.AddScoped<DialogueSourceScannerService>();
builder.Services.AddScoped<SaveFileContextService>();
builder.Services.AddScoped<DialogueContextSelectionService>();
builder.Services.AddScoped<DialogueQualityService>();
builder.Services.AddScoped<DialogueExportService>();
builder.Services.AddScoped<DialogueContextBuilderService>(sp => new DialogueContextBuilderService(
    sp.GetRequiredService<CharacterRepository>(),
    sp.GetRequiredService<CanonicalCharacterRepository>(),
    sp.GetRequiredService<DialogueSourceRepository>(),
    sp.GetRequiredService<RelationshipRepository>(),
    sp.GetRequiredService<EventRepository>(),
    sp.GetRequiredService<MemoryRepository>(),
    sp.GetRequiredService<VoiceRuleRepository>(),
    sp.GetRequiredService<UserLoreOverrideRepository>(),
    sp.GetRequiredService<LoreChangeLogRepository>(),
    sp.GetRequiredService<GeneratedDialogueHistoryRepository>(),
    sp.GetRequiredService<SaveFileContextService>(),
    sp.GetRequiredService<DialogueContextSelectionService>(),
    sp.GetRequiredService<PlayerProfileRepository>(),
    sp.GetRequiredService<LivingLoreWebOptions>().MaxRecentMemories,
    sp.GetRequiredService<LivingLoreWebOptions>().ModsFolderPath));
builder.Services.AddScoped<ModScanCoordinator>(sp => new ModScanCoordinator(
    async () =>
    {
        AppSettingsRepository settings = sp.GetRequiredService<AppSettingsRepository>();
        LivingLoreWebOptions webOptions = sp.GetRequiredService<LivingLoreWebOptions>();
        IReadOnlyList<LocalAppSetting> saved = await settings.GetAllAsync();
        return saved.FirstOrDefault(setting => setting.Key == "ModsFolderPath")?.Value ?? webOptions.ModsFolderPath;
    },
    async () =>
    {
        AppSettingsRepository settings = sp.GetRequiredService<AppSettingsRepository>();
        LivingLoreWebOptions webOptions = sp.GetRequiredService<LivingLoreWebOptions>();
        IReadOnlyList<LocalAppSetting> saved = await settings.GetAllAsync();
        return saved.FirstOrDefault(setting => setting.Key == "GamePath")?.Value ?? webOptions.GamePath;
    },
    sp.GetRequiredService<ModScannerService>(),
    sp.GetRequiredService<VanillaCharacterScannerService>(),
    sp.GetRequiredService<CharacterValidationService>(),
    sp.GetRequiredService<CharacterValidationRepository>(),
    sp.GetRequiredService<CanonicalCharacterRepository>(),
    sp.GetRequiredService<CharacterSyncService>(),
    sp.GetRequiredService<ScannedModRepository>(),
    sp.GetRequiredService<LoreConflictRepository>(),
    sp.GetRequiredService<ScanHistoryRepository>(),
    sp.GetRequiredService<DialogueSourceScannerService>(),
    message => sp.GetRequiredService<ILoggerFactory>().CreateLogger("LivingLore.ModScan").LogInformation("{ScanMessage}", message)));
builder.Services.AddScoped(sp =>
{
    LivingLoreWebOptions webOptions = sp.GetRequiredService<LivingLoreWebOptions>();
    return new OpenAiDialogueService(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient(),
        sp.GetRequiredService<PromptBuilder>(),
        webOptions.ResolvedOpenAiApiKey,
        webOptions.OpenAiModel);
});
builder.Services.AddScoped<DialogueGenerationService>(sp => new DialogueGenerationService(
    sp.GetRequiredService<DialogueContextBuilderService>(),
    sp.GetRequiredService<GeneratedDialogueHistoryRepository>(),
    sp.GetRequiredService<GeneratedDialogueOverrideRepository>(),
    sp.GetRequiredService<PromptBuilder>(),
    sp.GetRequiredService<OpenAiDialogueService>(),
    sp.GetRequiredService<DialogueExplanationService>(),
    sp.GetRequiredService<DialogueQualityService>()));
builder.Services.AddScoped<BranchingDialogueGenerationService>();

// Live log sink — captures .NET logger messages for the /Logs dashboard page.
LogSink logSink = new();
builder.Services.AddSingleton(logSink);
builder.Logging.AddProvider(new LogSinkProvider(logSink));

WebApplication app = builder.Build();
Console.WriteLine($"[Dashboard Startup] Build service provider/app: {dashboardStartupSw.ElapsedMilliseconds} ms");

using (IServiceScope scope = app.Services.CreateScope())
{
    LivingLoreWebOptions webOptions = scope.ServiceProvider.GetRequiredService<LivingLoreWebOptions>();
    var databaseInitSw = System.Diagnostics.Stopwatch.StartNew();
    await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>()
        .InitializeAsync(webOptions.SchemaPath, webOptions.SeedPath, seedOnFirstRun: true);
    databaseInitSw.Stop();
    Console.WriteLine($"[Dashboard Startup] Database initialization: {databaseInitSw.ElapsedMilliseconds} ms");
    var seedScenarioSw = System.Diagnostics.Stopwatch.StartNew();
    await scope.ServiceProvider.GetRequiredService<TestScenarioRepository>().SeedDefaultsAsync();
    seedScenarioSw.Stop();
    Console.WriteLine($"[Dashboard Startup] Test scenario seed: {seedScenarioSw.ElapsedMilliseconds} ms");
    var settingsReadSw = System.Diagnostics.Stopwatch.StartNew();
    IReadOnlyDictionary<string, string?> savedSettings = (await scope.ServiceProvider.GetRequiredService<AppSettingsRepository>().GetAllAsync())
        .ToDictionary(setting => setting.Key, setting => setting.Value);
    settingsReadSw.Stop();
    Console.WriteLine($"[Dashboard Startup] Settings read: {settingsReadSw.ElapsedMilliseconds} ms");
    webOptions.OpenAiModel = savedSettings.GetValueOrDefault("OpenAiModel") ?? webOptions.OpenAiModel;
    webOptions.GamePath = savedSettings.GetValueOrDefault("GamePath") ?? webOptions.GamePath;
    webOptions.ModsFolderPath = savedSettings.GetValueOrDefault("ModsFolderPath") ?? webOptions.ModsFolderPath;
    if (bool.TryParse(savedSettings.GetValueOrDefault("EnableLiveInGameDialogueGeneration"), out bool liveGenerationEnabled))
        webOptions.EnableLiveInGameDialogueGeneration = liveGenerationEnabled;
}
dashboardStartupSw.Stop();
Console.WriteLine($"[Dashboard Startup] Total pre-listen startup: {dashboardStartupSw.ElapsedMilliseconds} ms");

app.UseStaticFiles();
app.MapRazorPages();

// Lightweight health probe used by the SMAPI mod to confirm the dashboard is up.
app.MapGet("/api/health", async (DashboardHealthService health) => Results.Ok(await health.CheckAsync()));

app.MapGet("/api/dashboard", async (
    CharacterRepository characters,
    ScannedModRepository mods,
    LoreConflictRepository conflicts,
    LoreChangeLogRepository changes,
    ScanHistoryRepository scanHistory,
    GeneratedDialogueHistoryRepository dialogueHistory,
    PlayerProfileRepository playerProfiles) =>
{
    PlayerProfile? activeProfile = await playerProfiles.GetActiveAsync();
    return Results.Ok(new
    {
        databaseStatus = File.Exists(databasePath) ? "Ready" : "Missing",
        databasePath,
        activeCharacters = await characters.CountByActiveStatusAsync(true),
        inactiveCharacters = await characters.CountByActiveStatusAsync(false),
        detectedMods = await mods.CountActiveAsync(),
        conflictsFound = await conflicts.CountUnreviewedAsync(),
        activePlayerProfile = activeProfile is null ? null : new
        {
            activeProfile.Id,
            activeProfile.ProfileName,
            activeProfile.FarmerName,
            activeProfile.FarmName,
            linkedSaveFile = activeProfile.SaveFileName
        },
        recentScanHistory = await scanHistory.GetRecentAsync(5),
        recentLoreChanges = await changes.GetRecentAsync(10),
        recentGeneratedDialogue = await dialogueHistory.GetRecentAsync(10)
    });
});

app.MapGet("/api/characters", async (CharacterRepository repo, bool? includeInactive) =>
{
    // Active-only by default so historical (inactive) rows from removed mods are not shown as current.
    IReadOnlyList<Character> characters = await repo.GetAllAsync(includeInactive == true);
    return characters.Select(character => new
    {
        character.Id,
        character.Name,
        character.IsActive,
        character.SourceModId,
        character.SourceModName,
        kind = CharacterKind(character)
    });
});

app.MapGet("/api/canonical-characters", async (CanonicalCharacterRepository repo) =>
    Results.Ok(await repo.GetAllAsync()));

app.MapDelete("/api/characters", async (CharacterRepository repo) =>
{
    ClearCharactersSummary summary = await repo.ClearAllForRescanAsync();
    return Results.Ok(summary);
});

app.MapGet("/api/characters/{id:long}", async (
    long id,
    CharacterRepository characters,
    CanonicalCharacterRepository canonicalCharacters,
    RelationshipRepository relationships,
    MemoryRepository memories,
    VoiceRuleRepository voiceRules,
    UserLoreOverrideRepository overrides,
    GeneratedDialogueHistoryRepository dialogueHistory) =>
{
      Character? character = await characters.GetByIdAsync(id);
      if (character is null)
          return Results.NotFound();

      IReadOnlyList<Character> characterInstances = character.CanonicalCharacterId is long canonicalId
          ? await characters.GetForCanonicalAsync(canonicalId, activeOnly: false)
          : new[] { character };
      IReadOnlyList<CharacterSource> characterSources = character.CanonicalCharacterId is long sourceCanonicalId
          ? await canonicalCharacters.GetSourcesAsync(sourceCanonicalId)
          : Array.Empty<CharacterSource>();

      return Results.Ok(new
      {
          character,
          kind = CharacterKind(character),
          characterInstances,
          characterSources,
          relationships = await relationships.GetForCharacterAsync(id),
          memories = await memories.GetRecentForCharacterAsync(id, 100),
        voiceRules = await voiceRules.GetForCharacterAsync(id),
        userOverrides = await overrides.GetForCharacterAsync(id),
        dialogueHistory = await dialogueHistory.GetForCharacterAsync(id),
        modScanMetadata = new
        {
            character.SourceModId,
            character.SourceModName,
            character.SourceModVersion,
            character.SourceModAuthor,
            character.CharacterFingerprint,
            character.LastSeen,
            character.LastModified,
            character.RawModData
        }
    });
});

app.MapPost("/api/characters/{id:long}/overrides", async (
    long id,
    LoreOverrideRequest request,
    CharacterRepository characters,
    UserLoreOverrideRepository repo,
    LoreConflictRepository conflicts) =>
{
    long overrideId = await repo.AddOrUpdateAsync(id, request.OverrideType, request.FieldName, request.OverrideValue, request.Notes);
    Character? character = await characters.GetByIdAsync(id);
    string? existingValue = GetCharacterField(character, request.FieldName);
    if (character is not null
        && existingValue is not null
        && !string.Equals(existingValue, request.OverrideValue, StringComparison.Ordinal))
    {
        await conflicts.AddAsync(id, character.SourceModId, request.FieldName, existingValue, request.OverrideValue);
    }

    return Results.Ok(new { id = overrideId });
});

app.MapGet("/api/validation", async (CharacterValidationRepository repo) =>
{
    IReadOnlyList<CharacterValidationResult> results = await repo.GetAllAsync();

    static object Project(CharacterValidationResult result) => new
    {
        result.Name,
        result.SourceModName,
        result.Score,
        result.Classification,
        result.Imported,
        evidence = DescribeEvidence(result.Evidence),
        rules = result.Rules.Select(rule => new { rule.Name, rule.Passed, rule.Points })
    };

    return Results.Ok(new
    {
        confirmed = results.Where(r => r.Classification == CharacterValidationClassification.Confirmed).Select(Project),
        probable = results.Where(r => r.Classification == CharacterValidationClassification.Probable).Select(Project),
        rejected = results.Where(r => r.Classification == CharacterValidationClassification.Rejected).Select(Project),
        counts = new
        {
            confirmed = results.Count(r => r.Classification == CharacterValidationClassification.Confirmed),
            probable = results.Count(r => r.Classification == CharacterValidationClassification.Probable),
            rejected = results.Count(r => r.Classification == CharacterValidationClassification.Rejected)
        }
    });
});

// Duplicate-name characters in the character list, grouped so the user can merge them.
app.MapGet("/api/merge-review", async (CanonicalCharacterRepository repo) =>
    Results.Ok(await repo.GetDuplicateNameGroupsAsync()));

app.MapPost("/api/merge-review/merge", async (MergeDuplicatesRequest request, CanonicalCharacterRepository repo) =>
{
    if (string.IsNullOrWhiteSpace(request.Name) || request.PrimaryCharacterId <= 0)
        return Results.BadRequest(new { error = "A name and a primary character id are required." });

    int merged = await repo.MergeByNameAsync(request.Name, request.PrimaryCharacterId);
    return Results.Ok(new { merged });
});

app.MapGet("/api/memories", async (HttpRequest http, MemoryRepository repo) =>
{
    string? saveFileName = http.Query["saveFileName"].FirstOrDefault();
    string? npcName = http.Query["npcName"].FirstOrDefault();
    bool includeInactive = !bool.TryParse(http.Query["includeInactive"].FirstOrDefault(), out bool parsedIncludeInactive) || parsedIncludeInactive;
    long? playerProfileId = long.TryParse(http.Query["playerProfileId"].FirstOrDefault(), out long parsedProfileId)
        ? parsedProfileId
        : null;
    return Results.Ok(await repo.GetAllAsync(saveFileName, playerProfileId, npcName, includeInactive));
});
app.MapPost("/api/memories", async (MemoryRequest request, MemoryRepository repo) =>
{
    Memory memory = request.ToMemory();
    memory.Source = string.IsNullOrWhiteSpace(memory.Source) ? "Manual" : memory.Source;
    long id = await repo.AddManualAsync(memory);
    return Results.Ok(new { id });
});
app.MapPut("/api/memories/{id:long}", async (long id, MemoryRequest request, MemoryRepository repo) =>
{
    await repo.UpdateAsync(id, request.ToMemory());
    return Results.NoContent();
});
app.MapPost("/api/memories/{id:long}/deactivate", async (long id, MemoryRepository repo) =>
{
    await repo.SetActiveAsync(id, false);
    return Results.NoContent();
});
app.MapDelete("/api/memories/{id:long}", async (long id, MemoryRepository repo) =>
{
    await repo.DeleteAsync(id);
    return Results.NoContent();
});

app.MapGet("/api/relationships", async (RelationshipRepository repo) => Results.Ok(await repo.GetAllAsync()));
app.MapPost("/api/relationships", async (RelationshipRequest request, RelationshipRepository repo) =>
{
    long id = await repo.UpsertAsync(request.CharacterA, request.CharacterB, request.RelationshipType, request.Strength);
    return Results.Ok(new { id });
});
app.MapPut("/api/relationships/{id:long}", async (long id, RelationshipRequest request, RelationshipRepository repo) =>
{
    await repo.UpdateAsync(id, request.CharacterA, request.CharacterB, request.RelationshipType, request.Strength);
    return Results.NoContent();
});

app.MapGet("/api/mods", async (
    ScannedModRepository mods,
    CharacterRepository characters,
    bool? includeInactive) =>
{
    // Active-only by default; historical mod records stay in the DB but are hidden unless requested.
    IReadOnlyList<ScannedMod> scannedMods = await mods.GetAllAsync(includeInactive == true);
    IReadOnlyList<Character> allCharacters = await characters.GetAllAsync();
    return scannedMods.Select(mod => new
    {
        mod.Id,
        mod.UniqueId,
        mod.Name,
        mod.Version,
        mod.Author,
        mod.IsActive,
        mod.LastScanTime,
        characters = allCharacters.Where(character => character.SourceModId == mod.UniqueId).Select(character => character.Name)
    });
});
app.MapGet("/api/mods/status", async (
    CharacterRepository characters,
    CanonicalCharacterRepository canonicalCharacters,
    ScannedModRepository mods,
    LoreConflictRepository conflicts,
    ScanHistoryRepository scanHistory) =>
{
    IReadOnlyList<ScanHistoryEntry> history = await scanHistory.GetRecentAsync(10);
    ScanHistoryEntry? lastScan = history.FirstOrDefault();
    IReadOnlyList<Character> allCharacters = await characters.GetAllAsync();
    return Results.Ok(new
    {
        lastScanTime = lastScan?.CompletedAt,
        lastTriggerSource = lastScan?.TriggerSource,
        activeMods = await mods.CountActiveAsync(),
        activeCharacters = await characters.CountByActiveStatusAsync(true),
        inactiveCharacters = await characters.CountByActiveStatusAsync(false),
        vanillaCharacters = allCharacters.Count(character => character.IsVanilla),
        moddedCharacters = allCharacters.Count(character => !character.IsVanilla),
        mergedCanonicalCharacters = (await canonicalCharacters.GetAllAsync()).Count,
        conflictsFound = await conflicts.CountUnreviewedAsync(),
        latestScanSummary = lastScan,
        recentScanHistory = history
    });
});
app.MapPost("/api/mods/scan", (DashboardScanRunService scans, ILoggerFactory loggerFactory) =>
{
    ILogger logger = loggerFactory.CreateLogger("DashboardScanEndpoint");
    logger.LogInformation("Dashboard scan request received.");
    DashboardScanRunStatus status = scans.StartScan();
    return Results.Accepted($"/api/mods/scan/status/{status.ScanRunId}", new
    {
        status.ScanRunId,
        status.State,
        status.Message,
        status.StartedAt
    });
});

app.MapGet("/api/mods/scan/status/{scanRunId}", (string scanRunId, DashboardScanRunService scans) =>
{
    DashboardScanRunStatus? status = scans.GetStatus(scanRunId);
    return status is null ? Results.NotFound(new { error = "Scan run was not found." }) : Results.Ok(status);
});

// Registers vanilla dialogue for one character extracted by the SMAPI mod at save-load time.
// Sources are stored with SourceModId = "StardewValley.Vanilla" which is exempt from scan deactivation.
app.MapPost("/api/dialogue/register-vanilla", async (
    VanillaDialogueRequest request,
    CanonicalCharacterRepository canonicalRepo,
    DialogueSourceRepository dialogueSourceRepo,
    ILoggerFactory loggerFactory) =>
{
    ILogger logger = loggerFactory.CreateLogger("VanillaDialogueEndpoint");

    if (string.IsNullOrWhiteSpace(request.CharacterName) || request.Entries is null || request.Entries.Count == 0)
        return Results.BadRequest(new { error = "CharacterName and at least one entry are required." });

    CanonicalCharacter? canonical = await canonicalRepo.GetByNameOrAliasAsync(request.CharacterName);
    if (canonical is null)
    {
        logger.LogDebug("[VanillaDialogue] Canonical character '{CharacterName}' not found; skipping.", request.CharacterName);
        return Results.Ok(new { registered = 0, skipped = true, reason = "Character not in canonical database" });
    }

    DateTime now = DateTime.UtcNow;
    string assetPath = $"Characters/Dialogue/{request.CharacterName}";
    List<DialogueSource> sources = request.Entries
        .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
        .Select(kv => new DialogueSource
        {
            CanonicalCharacterId = canonical.Id,
            SourceModId = "StardewValley.Vanilla",
            FilePath = assetPath,
            AssetName = assetPath,
            DialogueKey = kv.Key,
            RawText = kv.Value,
            Season = InferVanillaSeason(kv.Key),
            HeartLevel = InferVanillaHeartLevel(kv.Key),
            RelationshipState = InferVanillaRelationship(kv.Key),
            SourcePriority = InferVanillaPriority(kv.Key),
            IsActive = true,
            LastSeen = now,
            SourceRootPath = null   // not from Mods folder; path filter keeps null rows
        })
        .ToList();

    await dialogueSourceRepo.UpsertRangeAsync(sources);
    logger.LogInformation("[VanillaDialogue] Registered {Count} line(s) for '{CharacterName}' (canonical id={CanonicalId}).",
        sources.Count, request.CharacterName, canonical.Id);
    return Results.Ok(new { registered = sources.Count });
});

app.MapPost("/api/dialogue-sources/scan", async (DialogueSourceScannerService scanner, LivingLoreWebOptions webOptions) =>
{
    if (string.IsNullOrWhiteSpace(webOptions.ModsFolderPath) || !Directory.Exists(webOptions.ModsFolderPath))
        return Results.BadRequest(new { error = "Configure a valid Mods folder path first." });

    return Results.Ok(await scanner.ScanAsync(webOptions.ModsFolderPath));
});

app.MapGet("/api/conflicts", async (LoreConflictRepository repo) => Results.Ok(await repo.GetAllAsync()));
app.MapPost("/api/conflicts/{id:long}/reviewed", async (long id, LoreConflictRepository repo) =>
{
    await repo.MarkReviewedAsync(id);
    return Results.NoContent();
});

app.MapPost("/api/dialogue/test", GenerateDialogue);
app.MapPost("/api/dialogue/generate", GenerateDialogue);
app.MapPost("/api/dialogue/branching", GenerateBranchingDialogue);

// Recent generated dialogue lines for the explanation/review list.
app.MapGet("/api/dialogue/history", async (GeneratedDialogueHistoryRepository repo) =>
{
    IReadOnlyList<GeneratedDialogueHistoryEntry> entries = await repo.GetRecentAsync(50);
    return Results.Ok(entries.Select(entry => new
    {
        entry.Id,
        entry.CharacterName,
        entry.Topic,
        entry.DialogueText,
        entry.Emotion,
        qualityScores = new
        {
            characterConsistency = entry.CharacterConsistencyScore,
            contextRelevance = entry.ContextRelevanceScore,
            relationshipRelevance = entry.RelationshipRelevanceScore,
            diversity = entry.DiversityScore,
            repetitionRisk = entry.RepetitionRiskScore
        },
        entry.CreatedDate
    }));
});

// Full explainability trace for one generated dialogue line.
app.MapGet("/api/dialogue/explain/{generatedDialogueId:long}", async (long generatedDialogueId, DialogueExplanationService explanations) =>
{
    DialogueExplanationResult? result = await explanations.GetAsync(generatedDialogueId);
    if (result is null)
        return Results.NotFound(new { error = "No explanation trace exists for this dialogue line." });

    DialogueGenerationTrace trace = result.Trace;
    return Results.Ok(new
    {
        generatedDialogue = result.Line is null ? null : new
        {
            result.Line.Id,
            result.Line.CharacterName,
            result.Line.Topic,
            result.Line.DialogueText,
            result.Line.Emotion,
            result.Line.CreatedDate
        },
        trace = new
        {
            trace.GeneratedDialogueId,
            trace.GeneratedAt,
            trace.CharacterId,
            trace.InterceptedNpcName,
            trace.CharacterName,
            trace.ResolvedCharacterName,
            location = trace.LocationName,
            internalLocation = trace.InternalLocationId,
            displayLocation = trace.DisplayLocationName,
            trace.PromptVersion,
            trace.ModelUsed,
            promptText = trace.PromptText,
            requestSource = trace.RequestSource,
            saveContext = ParseJson(trace.SaveContextSnapshot),
            memoriesUsed = ParseJson(trace.MemoriesUsed),
            relationshipsUsed = ParseJson(trace.RelationshipsUsed),
            userOverridesUsed = ParseJson(trace.UserOverridesUsed),
            dialogueSourcesUsed = ParseJson(trace.DialogueSourcesUsed),
            sourceModsUsed = ParseJson(trace.SourceModsUsed),
            playerProfileUsed = ParseJson(trace.PlayerProfileUsed),
            playerRelationshipNotesUsed = ParseJson(trace.PlayerRelationshipNotesUsed),
            playerMemoriesUsed = ParseJson(trace.PlayerMemoriesUsed),
            saveFileLinkUsed = trace.SaveFileLinkUsed,
            playerProfileMatchMethod = trace.PlayerProfileMatchMethod
        }
    });
});

// ---- Game Simulation Mode -------------------------------------------------------------------
app.MapGet("/api/scenarios", async (TestScenarioRepository repo) => Results.Ok(await repo.GetAllAsync()));

app.MapGet("/api/scenarios/{id:long}", async (long id, TestScenarioRepository repo) =>
{
    TestScenario? scenario = await repo.GetByIdAsync(id);
    return scenario is null ? Results.NotFound() : Results.Ok(scenario);
});

app.MapPost("/api/scenarios", async (ScenarioRequest request, TestScenarioRepository repo) =>
{
    long id = await repo.AddAsync(ToScenario(request, 0));
    return Results.Ok(new { id });
});

app.MapPut("/api/scenarios/{id:long}", async (long id, ScenarioRequest request, TestScenarioRepository repo) =>
{
    if (await repo.GetByIdAsync(id) is null)
        return Results.NotFound();
    await repo.UpdateAsync(ToScenario(request, id));
    return Results.NoContent();
});

app.MapDelete("/api/scenarios/{id:long}", async (long id, TestScenarioRepository repo) =>
{
    await repo.DeleteAsync(id);
    return Results.NoContent();
});

app.MapPost("/api/simulate", async (SimulateRequest request, GameSimulationService simulation, DialogueExplanationService explanations) =>
{
    SimulationReport report = await simulation.SimulateAsync(request.ScenarioId, request.CharacterName, request.Topic);

    // Attach the explainability trace if a line was generated and recorded.
    object? explanation = null;
    if (report.HistoryId is long historyId)
    {
        DialogueExplanationResult? trace = await explanations.GetAsync(historyId);
        if (trace is not null)
        {
            explanation = new
            {
                trace.Trace.PromptVersion,
                trace.Trace.ModelUsed,
                memoriesUsed = ParseJson(trace.Trace.MemoriesUsed),
                relationshipsUsed = ParseJson(trace.Trace.RelationshipsUsed),
                userOverridesUsed = ParseJson(trace.Trace.UserOverridesUsed),
                dialogueSourcesUsed = ParseJson(trace.Trace.DialogueSourcesUsed),
                sourceModsUsed = ParseJson(trace.Trace.SourceModsUsed)
            };
        }
    }

    return Results.Ok(new { report, explanation });
});

// ---- Player Profiles ------------------------------------------------------------------------
app.MapGet("/api/player-profiles", async (PlayerProfileRepository repo) => Results.Ok(await repo.GetAllAsync()));

app.MapGet("/api/player-profiles/{id:long}", async (long id, PlayerProfileRepository repo) =>
{
    PlayerProfile? profile = await repo.GetByIdAsync(id);
    if (profile is null)
        return Results.NotFound();
    return Results.Ok(new
    {
        profile,
        relationships = await repo.GetRelationshipsAsync(id),
        memories = await repo.GetMemoriesAsync(id, canonicalCharacterId: null, includeGeneral: true),
        saveLinks = await repo.GetSaveLinksAsync(id)
    });
});

app.MapPost("/api/player-profiles", async (PlayerProfileRequest request, PlayerProfileRepository repo) =>
{
    long id = await repo.AddAsync(ToPlayerProfile(request, 0));
    return Results.Ok(new { id });
});

app.MapPost("/api/player-profiles/autocomplete", async (
    PlayerProfileAutocompleteRequest request,
    OpenAiDialogueService openAi,
    LivingLoreWebOptions webOptions,
    ILoggerFactory loggerFactory,
    CancellationToken cancellationToken) =>
{
    ILogger logger = loggerFactory.CreateLogger("PlayerProfileAutocompleteEndpoint");
    string concept = request.Concept?.Trim() ?? "";
    if (string.IsNullOrWhiteSpace(concept))
        return Results.BadRequest(new { error = "Profile concept is required." });
    if (concept.Length > 500)
        return Results.BadRequest(new { error = "Profile concept must be 500 characters or fewer." });
    if (!openAi.HasApiKey)
        return Results.BadRequest(new { error = "OpenAI API key is not configured." });

    try
    {
        PlayerProfileAutocompleteResult generated = await openAi.GeneratePlayerProfileAsync(concept, cancellationToken);
        PlayerProfileAutocompleteResult merged = MergeGeneratedProfile(generated, request.ExistingProfile, request.OverwriteExisting);

        logger.LogInformation(
            "Generated player profile draft. model={Model}; timestamp={Timestamp:o}; concept={Concept}; generatedFields={GeneratedFields}",
            webOptions.OpenAiModel,
            DateTime.UtcNow,
            concept,
            JsonSerializer.Serialize(generated));

        return Results.Ok(new
        {
            success = true,
            profile = merged,
            warnings = Array.Empty<string>()
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Player profile autocomplete failed for concept: {Concept}", concept);
        return Results.BadRequest(new { error = $"Profile generation failed: {ex.Message}" });
    }
});

app.MapPut("/api/player-profiles/{id:long}", async (long id, PlayerProfileRequest request, PlayerProfileRepository repo) =>
{
    PlayerProfile? existing = await repo.GetByIdAsync(id);
    if (existing is null)
        return Results.NotFound();
    PlayerProfile updated = ToPlayerProfile(request, id);
    updated.IsActive = existing.IsActive; // active state is managed via set-active/archive, not edits.
    await repo.UpdateAsync(updated);
    return Results.NoContent();
});

app.MapPost("/api/player-profiles/{id:long}/relationships", async (long id, PlayerProfileRelationshipRequest request, PlayerProfileRepository repo) =>
{
    long relationshipId = await repo.AddRelationshipAsync(new PlayerProfileRelationship
    {
        PlayerProfileId = id,
        CanonicalCharacterId = request.CanonicalCharacterId,
        RelationshipType = request.RelationshipType,
        RelationshipDescription = request.RelationshipDescription,
        RelationshipStrength = request.RelationshipStrength,
        CustomNotes = request.CustomNotes ?? ""
    });
    return Results.Ok(new { id = relationshipId });
});

app.MapPost("/api/player-profiles/{id:long}/memories", async (long id, PlayerProfileMemoryRequest request, PlayerProfileRepository repo) =>
{
    long memoryId = await repo.AddMemoryAsync(new PlayerProfileMemory
    {
        PlayerProfileId = id,
        CanonicalCharacterId = request.CanonicalCharacterId,
        MemoryText = request.MemoryText,
        Importance = request.Importance
    });
    return Results.Ok(new { id = memoryId });
});

app.MapPost("/api/player-profiles/{id:long}/link-save", async (long id, SaveLinkRequest request, PlayerProfileRepository repo) =>
{
    long linkId = await repo.LinkSaveAsync(new PlayerProfileSaveLink
    {
        PlayerProfileId = id,
        SaveFileName = request.SaveFileName,
        SaveFilePath = request.SaveFilePath,
        LastSeen = DateTime.UtcNow,
        IsDefaultForSave = request.IsDefaultForSave
    });
    return Results.Ok(new { id = linkId });
});

app.MapPost("/api/player-profiles/{id:long}/set-active", async (long id, PlayerProfileRepository repo) =>
{
    await repo.SetActiveAsync(id);
    return Results.NoContent();
});

app.MapPost("/api/player-profiles/{id:long}/archive", async (long id, PlayerProfileRepository repo) =>
{
    await repo.ArchiveAsync(id);
    return Results.NoContent();
});

// Hard delete is only performed when the client explicitly confirms (?confirm=true).
app.MapDelete("/api/player-profiles/{id:long}", async (long id, bool? confirm, PlayerProfileRepository repo) =>
{
    if (confirm == true)
        await repo.DeleteAsync(id);
    else
        await repo.ArchiveAsync(id);
    return Results.NoContent();
});

app.MapGet("/api/dialogue/context/{canonicalId:long}", async (long canonicalId, DialogueSourceRepository sources) =>
{
    return Results.Ok(new
    {
        sources = await sources.GetForCanonicalAsync(canonicalId, activeOnly: false, limit: 500),
        summary = await sources.GetSummaryAsync(canonicalId)
    });
});

app.MapGet("/api/dialogue/overrides", async (GeneratedDialogueOverrideRepository repo) =>
    Results.Ok(await repo.GetAllAsync()));

app.MapPost("/api/dialogue/overrides/{id:long}/approve", async (long id, GeneratedDialogueOverrideRepository repo) =>
{
    await repo.SetApprovedAsync(id, true);
    return Results.NoContent();
});

app.MapPost("/api/dialogue/overrides/{id:long}/enable", async (long id, GeneratedDialogueOverrideRepository repo) =>
{
    await repo.SetEnabledAsync(id, true);
    return Results.NoContent();
});

app.MapPost("/api/dialogue/export", async (DialogueExportService exporter, LivingLoreWebOptions webOptions) =>
{
    string outputDirectory = Path.Combine(Path.GetDirectoryName(webOptions.DatabasePath) ?? AppContext.BaseDirectory, "DialogueOverrideContentPack");
    return Results.Ok(await exporter.ExportAsync(outputDirectory));
});

app.MapGet("/api/openai/status", async (OpenAiDialogueService openAi, LivingLoreWebOptions webOptions) =>
{
    bool hasApiKey = !string.IsNullOrWhiteSpace(webOptions.ResolvedOpenAiApiKey);
    (bool connected, string? error) = hasApiKey
        ? await openAi.TestConnectionAsync()
        : (false, "No API key configured.");

    return Results.Ok(new
    {
        hasApiKey,
        connected,
        model = webOptions.OpenAiModel,
        error,
        checkedAt = DateTime.UtcNow
    });
});

app.MapGet("/api/openai/models", (LivingLoreWebOptions webOptions) => Results.Ok(new
{
    current = webOptions.OpenAiModel,
    available = KnownOpenAiModels()
}));

app.MapPost("/api/openai/model", async (ModelRequest request, AppSettingsRepository repo, LivingLoreWebOptions webOptions) =>
{
    if (string.IsNullOrWhiteSpace(request.Model))
        return Results.BadRequest(new { error = "Model name is required." });

    string model = request.Model.Trim();
    await repo.SetAsync("OpenAiModel", model);
    webOptions.OpenAiModel = model;
    return Results.Ok(new { model });
});

app.MapGet("/api/settings", async (LivingLoreWebOptions webOptions, AppSettingsRepository repo) =>
{
    IReadOnlyDictionary<string, string?> saved = (await repo.GetAllAsync()).ToDictionary(setting => setting.Key, setting => setting.Value);
    return Results.Ok(new
    {
        databasePath = webOptions.DatabasePath,
        openAiApiKeyEnvironmentVariable = webOptions.OpenAiApiKeyEnvironmentVariable,
        apiKeyFilePath = webOptions.ApiKeyFilePath,
        hasOpenAiApiKey = !string.IsNullOrWhiteSpace(webOptions.ResolvedOpenAiApiKey),
        openAiModel = saved.GetValueOrDefault("OpenAiModel") ?? webOptions.OpenAiModel,
        availableModels = KnownOpenAiModels(),
        gamePath = saved.GetValueOrDefault("GamePath") ?? webOptions.GamePath,
        modsFolderPath = saved.GetValueOrDefault("ModsFolderPath") ?? webOptions.ModsFolderPath,
        scanTimeoutSeconds = int.TryParse(saved.GetValueOrDefault("ScanTimeoutSeconds"), out int scanTimeoutSeconds)
            ? scanTimeoutSeconds
            : webOptions.ScanTimeoutSeconds,
        perFileParseTimeoutMs = int.TryParse(saved.GetValueOrDefault("PerFileParseTimeoutMs"), out int perFileParseTimeoutMs)
            ? perFileParseTimeoutMs
            : webOptions.PerFileParseTimeoutMs,
        enableScanCache = bool.TryParse(saved.GetValueOrDefault("EnableScanCache"), out bool enableScanCache)
            ? enableScanCache
            : webOptions.EnableScanCache,
        maxDialogueFilesPerScan = int.TryParse(saved.GetValueOrDefault("MaxDialogueFilesPerScan"), out int maxDialogueFilesPerScan)
            ? maxDialogueFilesPerScan
            : webOptions.MaxDialogueFilesPerScan,
        enableLiveInGameDialogueGeneration = bool.TryParse(saved.GetValueOrDefault("EnableLiveInGameDialogueGeneration"), out bool enabled)
            ? enabled
            : webOptions.EnableLiveInGameDialogueGeneration
    });
});
app.MapPost("/api/settings", async (SettingsRequest request, AppSettingsRepository repo, LivingLoreWebOptions webOptions, ScanOptions scanOptions) =>
{
    await repo.SetAsync("OpenAiModel", request.OpenAiModel);
    await repo.SetAsync("GamePath", request.GamePath ?? "");
    await repo.SetAsync("ModsFolderPath", request.ModsFolderPath);
    await repo.SetAsync("EnableLiveInGameDialogueGeneration", request.EnableLiveInGameDialogueGeneration.ToString());
    await repo.SetAsync("ScanTimeoutSeconds", request.ScanTimeoutSeconds.ToString());
    await repo.SetAsync("PerFileParseTimeoutMs", request.PerFileParseTimeoutMs.ToString());
    await repo.SetAsync("EnableScanCache", request.EnableScanCache.ToString());
    await repo.SetAsync("MaxDialogueFilesPerScan", request.MaxDialogueFilesPerScan?.ToString() ?? "");
    webOptions.OpenAiModel = request.OpenAiModel;
    webOptions.GamePath = request.GamePath ?? "";
    webOptions.ModsFolderPath = request.ModsFolderPath;
    webOptions.EnableLiveInGameDialogueGeneration = request.EnableLiveInGameDialogueGeneration;
    webOptions.ScanTimeoutSeconds = request.ScanTimeoutSeconds;
    webOptions.PerFileParseTimeoutMs = request.PerFileParseTimeoutMs;
    webOptions.EnableScanCache = request.EnableScanCache;
    webOptions.MaxDialogueFilesPerScan = request.MaxDialogueFilesPerScan;
    scanOptions.ScanTimeoutSeconds = request.ScanTimeoutSeconds;
    scanOptions.PerFileParseTimeoutMs = request.PerFileParseTimeoutMs;
    scanOptions.EnableScanCache = request.EnableScanCache;
    scanOptions.MaxDialogueFilesPerScan = request.MaxDialogueFilesPerScan;
    return Results.NoContent();
});

// Live log stream — Server-Sent Events feed for the /Logs dashboard page.
app.MapGet("/api/logs/stream", async (LogSink sink, HttpContext ctx, CancellationToken ct) =>
{
    ctx.Response.ContentType = "text/event-stream";
    ctx.Response.Headers["Cache-Control"] = "no-cache";
    ctx.Response.Headers["X-Accel-Buffering"] = "no";
    await ctx.Response.Body.FlushAsync(ct);

    foreach (LogEntry entry in sink.GetHistory())
    {
        await ctx.Response.WriteAsync($"data: {JsonSerializer.Serialize(entry)}\n\n", ct);
        await ctx.Response.Body.FlushAsync(ct);
    }

    Channel<LogEntry> channel = sink.Subscribe();
    try
    {
        await foreach (LogEntry entry in channel.Reader.ReadAllAsync(ct))
        {
            await ctx.Response.WriteAsync($"data: {JsonSerializer.Serialize(entry)}\n\n", ct);
            await ctx.Response.Body.FlushAsync(ct);
        }
    }
    catch (OperationCanceledException) { }
    finally
    {
        sink.Unsubscribe(channel);
    }
});

app.Run();

static async Task<IResult> GenerateDialogue(
    DialogueTestRequest request,
    DialogueGenerationService service,
    LivingLoreWebOptions options,
    ILoggerFactory loggerFactory)
{
    ILogger logger = loggerFactory.CreateLogger("DialogueTestEndpoint");

    bool isSmapiRequest = request.RequestSource?.Contains("SMAPI", StringComparison.OrdinalIgnoreCase) == true;
    if (isSmapiRequest)
        logger.LogInformation("Received dialogue request from SMAPI (source={RequestSource}).", request.RequestSource);
    else
        logger.LogInformation("Dialogue test endpoint called for character '{CharacterName}'.", request.CharacterName);

    logger.LogInformation("Received characterName={CharacterName}, rawLocation={RawLocation}.", request.CharacterName, request.InternalLocationId ?? request.LocationName ?? request.Location);

    // When SMAPI provides a save context, use it directly so live game state reaches the prompt.
    SaveFileContextSnapshot? saveContextOverride = null;
    if (isSmapiRequest && request.SaveContext is SaveFileContextSnapshot sc)
    {
        saveContextOverride = sc;
        logger.LogInformation(
            "[SMAPI] Save context received: saveFileName={SaveFileName}, playerName={PlayerName}, farmName={FarmName}, location={Location}, season={Season}, day={Day}, year={Year}.",
            sc.SaveFileName ?? "(none)", sc.PlayerName, sc.FarmName, sc.Location, sc.Season, sc.Day, sc.Year);
        if (sc.PlayerName is "Unknown" or "")
            logger.LogWarning("[SMAPI] Save context has Unknown playerName — live game state may not be available.");
        if (sc.FarmName is "Unknown" or "")
            logger.LogWarning("[SMAPI] Save context has Unknown farmName — live game state may not be available.");
        if (string.IsNullOrWhiteSpace(sc.SaveFileName))
            logger.LogWarning("[SMAPI] Save context is missing saveFileName — save-file profile mapping will be skipped.");
    }
    else if (isSmapiRequest)
    {
        logger.LogWarning("[SMAPI] Request did not include a save context; falling back to defaults.");
    }

    // ActivePlayerProfileId (from SMAPI hint or future use) falls back to explicit PlayerProfileId.
    long? resolvedProfileId = request.PlayerProfileId ?? request.ActivePlayerProfileId;
    if (resolvedProfileId is not null)
        logger.LogInformation("[SMAPI] Explicit player profile id hint received: {ProfileId}.", resolvedProfileId);
    else
        logger.LogInformation("[SMAPI] No explicit player profile id; server will auto-resolve from save context (saveFileName / playerName+farmName / globally active).");

    string rawLocation = FirstNonEmpty(request.InternalLocationId, request.LocationName, request.Location);
    DialogueContext context = new()
    {
        CharacterName = request.CharacterName,
        DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? request.CharacterName : request.DisplayName,
        InterceptedNpcName = string.IsNullOrWhiteSpace(request.InterceptedNpcName) ? request.CharacterName : request.InterceptedNpcName,
        Topic = request.Topic,
        Season = request.Season,
        Weather = request.Weather,
        InternalLocationId = rawLocation,
        DisplayLocation = request.DisplayLocation ?? "",
        Location = rawLocation,
        FriendshipLevel = request.FriendshipLevel,
        RequestSource = request.RequestSource ?? "Dashboard"
    };

    logger.LogInformation("[LivingLore] Intercepted NPC: {InterceptedNpc}", context.InterceptedNpcName);
    logger.LogInformation("[LivingLore] CharacterName: {CharacterName}", context.CharacterName);
    logger.LogInformation("[LivingLore] Raw Location ID: {RawLocationId}", context.InternalLocationId);
    logger.LogInformation("Save context source: {Source}.", saveContextOverride is not null ? "SMAPI" : "fallback defaults");

    try
    {
        GeneratedDialogueResult result = await service.GenerateAsync(context, request.RelationshipContext, saveContextOverride, resolvedProfileId);
        logger.LogInformation("[LivingLore] Resolved Character: {ResolvedCharacter}", result.ResolvedCharacterName);
        logger.LogInformation("[LivingLore] Resolved Display Location: {DisplayLocation}", result.DisplayLocation);
        bool profileResolved = !string.IsNullOrWhiteSpace(result.ActivePlayerProfileName);
        logger.LogInformation(
            "[ProfileResolution] Result: profileIncluded={ProfileIncluded}, profileName={ProfileName}, matchMethod={MatchMethod}.",
            profileResolved,
            profileResolved ? result.ActivePlayerProfileName : "(none)",
            result.PlayerProfileMatchMethod);
        if (!profileResolved && isSmapiRequest)
            logger.LogWarning(
                "[ProfileResolution] No player profile resolved for SMAPI request. " +
                "Create a profile with farmerName='{PlayerName}' and farmName='{FarmName}', or link saveFile='{SaveFile}' to a profile.",
                saveContextOverride?.PlayerName ?? "(unknown)",
                saveContextOverride?.FarmName ?? "(unknown)",
                saveContextOverride?.SaveFileName ?? "(unknown)");
        if (IsIdentityError(result.Error))
        {
            logger.LogWarning("[LivingLore] WARNING: Character/location mismatch detected.");
            logger.LogWarning("CharacterName={CharacterName}", context.CharacterName);
            logger.LogWarning("LocationName={LocationName}", context.Location);
        }
        logger.LogInformation(
            "Dialogue test result for '{CharacterName}': promptBuilt={PromptBuilt}, dialogueReturned={DialogueReturned}, error={Error}",
            request.CharacterName,
            !string.IsNullOrWhiteSpace(result.PromptUsed),
            !string.IsNullOrWhiteSpace(result.ReturnedDialogue),
            result.Error);

        return Results.Ok(new
        {
            saveContext = result.SaveContext,
            interceptedNpcName = result.InterceptedNpcName,
            characterName = result.CharacterName,
            displayName = result.DisplayName,
            resolvedCharacterName = result.ResolvedCharacterName,
            locationName = result.LocationName,
            internalLocationId = result.InternalLocationId,
            displayLocation = result.DisplayLocation,
            activePlayerProfileName = result.ActivePlayerProfileName,
            playerProfileMatchMethod = result.PlayerProfileMatchMethod,
            promptUsed = result.PromptUsed,
            returnedDialogue = result.ReturnedDialogue,
            error = result.Error,
            qualityScores = result.QualityScores,
            prompt = result.Prompt,
            dialogue = result.Dialogue,
            historyId = result.HistoryId
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Dialogue test endpoint failed for character '{CharacterName}'.", request.CharacterName);
        return Results.Ok(new
        {
            saveContext = new
            {
                context.Season,
                context.Weather,
                context.Location,
                context.FriendshipLevel,
                request.RelationshipContext
            },
            interceptedNpcName = context.InterceptedNpcName,
            characterName = context.CharacterName,
            displayName = context.DisplayName,
            resolvedCharacterName = context.ResolvedCharacterName,
            locationName = context.Location,
            internalLocationId = context.InternalLocationId,
            displayLocation = context.DisplayLocation,
            activePlayerProfileName = "",
            playerProfileMatchMethod = "none",
            promptUsed = "",
            returnedDialogue = "",
            error = ex.Message,
            prompt = "",
            dialogue = new GeneratedDialogue
            {
                Character = request.CharacterName,
                Topic = request.Topic,
                Emotion = "neutral",
                Dialogue = ""
            },
            historyId = 0L
        });
    }
}

static async Task<IResult> GenerateBranchingDialogue(
    BranchingDialogueRequest request,
    BranchingDialogueGenerationService service,
    ILoggerFactory loggerFactory)
{
    ILogger logger = loggerFactory.CreateLogger("BranchingDialogueEndpoint");
    request.Context.RequestSource = string.IsNullOrWhiteSpace(request.Context.RequestSource)
        ? "SMAPI-Branching"
        : request.Context.RequestSource;

    if (request.SaveContext is not null)
        logger.LogInformation(
            "[Branching] Save context received: saveFileName={SaveFileName}, playerName={PlayerName}, farmName={FarmName}, location={Location}, season={Season}, day={Day}.",
            request.SaveContext.SaveFileName ?? "(none)",
            request.SaveContext.PlayerName,
            request.SaveContext.FarmName,
            request.SaveContext.Location,
            request.SaveContext.Season,
            request.SaveContext.Day);

    logger.LogInformation(
        "[Branching] Request session={SessionId}, mode={Mode}, npc={Npc}, turn={Turn}/{MaxTurn}, history={HistoryCount}.",
        string.IsNullOrWhiteSpace(request.SessionId) ? "(none)" : request.SessionId,
        request.Mode,
        request.Context.CharacterName,
        request.TurnCount,
        request.MaxTurnCount,
        request.History.Count);
    logger.LogInformation(
        "[Branching] Selected option id={SelectedId}, text={SelectedText}. Latest player={LatestPlayer}. Latest npc={LatestNpc}. Recent history={RecentHistory}.",
        string.IsNullOrWhiteSpace(request.SelectedOptionId) ? "(none)" : request.SelectedOptionId,
        string.IsNullOrWhiteSpace(request.SelectedOptionText) ? "(none)" : request.SelectedOptionText,
        request.History.LastOrDefault()?.PlayerChoiceText ?? "(none)",
        request.History.LastOrDefault()?.NpcResponse ?? "(none)",
        string.Join(" | ", request.History.TakeLast(3).Select(turn => $"P: {turn.PlayerChoiceText} / NPC: {turn.NpcResponse}")));

    try
    {
        BranchingDialogueResponse response = await service.GenerateAsync(request);
        bool profileResolved = !string.IsNullOrWhiteSpace(response.ActivePlayerProfileName);
        logger.LogInformation(
            "[Branching] Result npc={Npc}, profileIncluded={ProfileIncluded}, profileName={ProfileName}, matchMethod={MatchMethod}, options={OptionCount}, end={End}, error={Error}.",
            request.Context.CharacterName,
            profileResolved,
            profileResolved ? response.ActivePlayerProfileName : "(default farmer)",
            response.PlayerProfileMatchMethod,
            response.PlayerOptions.Count,
            response.ConversationShouldEnd,
            response.Error);
        logger.LogInformation(
            "[Branching] Prompt/template used: {Template}. Prompt includes selected option={IncludesSelected}.",
            response.PromptUsed.Contains("BRANCHING_DIALOGUE_CONVERSATION_V3", StringComparison.Ordinal) ? "BRANCHING_DIALOGUE_CONVERSATION_V3" : "(unknown)",
            !string.IsNullOrWhiteSpace(request.SelectedOptionText) && response.PromptUsed.Contains(request.SelectedOptionText, StringComparison.Ordinal));

        if (!profileResolved)
            logger.LogInformation("[Branching] No custom player profile resolved; default Stardew farmer profile was injected into the prompt.");

        return Results.Ok(response);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[Branching] Endpoint failed for npc={Npc}.", request.Context.CharacterName);
        // Return 500 so the SMAPI client knows generation failed and can show a neutral
        // in-game message.  Never return HTTP 200 with fake NPC dialogue on failure —
        // that hides the real error and makes debugging confusing.
        return Results.Json(
            new { error = ex.Message, npc = request.Context.CharacterName },
            statusCode: 500);
    }
}

// Resolves the OpenAI API key from (in priority order): the environment variable, app
// configuration, then the dedicated key file. The key file may contain comment lines starting
// with '#'; the first non-empty, non-comment line is used as the key.
static string ResolveOpenAiApiKey(LivingLoreWebOptions options, IConfiguration configuration)
{
    string? key = Environment.GetEnvironmentVariable(options.OpenAiApiKeyEnvironmentVariable);
    if (!string.IsNullOrWhiteSpace(key))
        return key.Trim();

    key = configuration["OpenAI:ApiKey"];
    if (!string.IsNullOrWhiteSpace(key))
        return key.Trim();

    if (File.Exists(options.ApiKeyFilePath))
    {
        foreach (string line in File.ReadAllLines(options.ApiKeyFilePath))
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                continue;
            return trimmed;
        }
    }

    return "";
}

// Parses a stored JSON string into a JsonElement for the API response, falling back to null.
static JsonElement? ParseJson(string? json)
{
    if (string.IsNullOrWhiteSpace(json))
        return null;
    try
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
    catch
    {
        return null;
    }
}

// Curated list of OpenAI models that work with the Responses API used for dialogue generation.
static string[] KnownOpenAiModels() => new[]
{
    "gpt-4.1",
    "gpt-4.1-mini",
    "gpt-4.1-nano",
    "gpt-4o",
    "gpt-4o-mini",
    "o4-mini"
};

static string[] DescribeEvidence(CharacterEvidence evidence)
{
    List<string> labels = new();
    if (evidence.HasFlag(CharacterEvidence.DataCharacters)) labels.Add("Data/Characters");
    if (evidence.HasFlag(CharacterEvidence.NpcDisposition)) labels.Add("NPC disposition");
    if (evidence.HasFlag(CharacterEvidence.CharacterAsset)) labels.Add("Character sprite");
    if (evidence.HasFlag(CharacterEvidence.PortraitAsset)) labels.Add("Portrait");
    if (evidence.HasFlag(CharacterEvidence.DialogueAsset)) labels.Add("Dialogue");
    if (evidence.HasFlag(CharacterEvidence.ScheduleAsset)) labels.Add("Schedule");
    if (evidence.HasFlag(CharacterEvidence.ContentPatcherPatch)) labels.Add("Content Patcher patch");
    return labels.ToArray();
}

static PlayerProfile ToPlayerProfile(PlayerProfileRequest request, long id) => new()
{
    Id = id,
    ProfileName = request.ProfileName,
    FarmerName = request.FarmerName ?? "",
    FarmName = request.FarmName ?? "",
    SaveFileName = string.IsNullOrWhiteSpace(request.SaveFileName) ? null : request.SaveFileName,
    SaveFilePath = string.IsNullOrWhiteSpace(request.SaveFilePath) ? null : request.SaveFilePath,
    Description = request.Description ?? "",
    Backstory = request.Backstory ?? "",
    Personality = request.Personality ?? "",
    RoleplayStyle = request.RoleplayStyle ?? "",
    PreferredTone = request.PreferredTone ?? "",
    ImportantHistory = request.ImportantHistory ?? "",
    CurrentGoals = request.CurrentGoals ?? "",
    RelationshipNotes = request.RelationshipNotes ?? "",
    CustomLore = request.CustomLore ?? "",
    IsActive = true
};

static PlayerProfileAutocompleteResult MergeGeneratedProfile(
    PlayerProfileAutocompleteResult generated,
    PlayerProfileDraft? existing,
    bool overwriteExisting)
{
    if (overwriteExisting || existing is null)
        return generated;

    return generated with
    {
        ProfileName = KeepExisting(existing.ProfileName, generated.ProfileName),
        Description = KeepExisting(existing.Description, generated.Description),
        Backstory = KeepExisting(existing.Backstory, generated.Backstory),
        Personality = KeepExisting(existing.Personality, generated.Personality),
        RoleplayStyle = KeepExisting(existing.RoleplayStyle, generated.RoleplayStyle),
        PreferredDialogueTone = KeepExisting(FirstExisting(existing.PreferredDialogueTone, existing.PreferredTone), generated.PreferredDialogueTone),
        ImportantHistory = KeepExisting(existing.ImportantHistory, generated.ImportantHistory),
        CurrentGoals = KeepExisting(existing.CurrentGoals, generated.CurrentGoals),
        RelationshipNotes = KeepExisting(existing.RelationshipNotes, generated.RelationshipNotes),
        CustomLore = KeepExisting(existing.CustomLore, generated.CustomLore)
    };
}

static string KeepExisting(string? existing, string generated) =>
    string.IsNullOrWhiteSpace(existing) ? generated : existing.Trim();

static string? FirstExisting(params string?[] values) =>
    values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

static TestScenario ToScenario(ScenarioRequest request, long id) => new()
{
    Id = id,
    Name = request.Name,
    PlayerName = request.PlayerName,
    FarmName = request.FarmName,
    Year = request.Year,
    Season = request.Season,
    Weather = request.Weather,
    Location = request.Location,
    FriendshipHearts = request.FriendshipHearts,
    RelationshipState = request.RelationshipState,
    SeenEvents = request.SeenEvents ?? "",
    CompletedQuests = request.CompletedQuests ?? "",
    CommunityCenterState = request.CommunityCenterState,
    PlayerProfileId = request.PlayerProfileId
};

static string CharacterKind(Character character)
{
    if (character.IsVanilla)
        return "vanilla";

    if (string.IsNullOrWhiteSpace(character.SourceModId))
        return character.RawModData is null ? "vanilla" : "custom";

    return "modded";
}

static string? GetCharacterField(Character? character, string fieldName)
{
    if (character is null)
        return null;

    return fieldName.ToLowerInvariant() switch
    {
        "name" => character.Name,
        "description" => character.Description,
        "personality" => character.Personality,
        "occupation" => character.Occupation,
        "homelocation" or "home location" => character.HomeLocation,
        _ => null
    };
}

static bool IsIdentityError(string? error)
{
    return !string.IsNullOrWhiteSpace(error)
        && (error.Contains("Character/location mismatch", StringComparison.OrdinalIgnoreCase)
            || error.Contains("known location/building/map", StringComparison.OrdinalIgnoreCase)
            || error.Contains("characterName is null or empty", StringComparison.OrdinalIgnoreCase));
}

static string FirstNonEmpty(params string?[] values)
{
    foreach (string? value in values)
    {
        if (!string.IsNullOrWhiteSpace(value))
            return value;
    }

    return "Unknown";
}

// Helpers for inferring metadata from vanilla dialogue keys (mirrors DialogueSourceScannerService logic).
static string? InferVanillaSeason(string key)
{
    foreach (string s in new[] { "spring", "summer", "fall", "winter" })
        if (key.Contains(s, StringComparison.OrdinalIgnoreCase)) return s;
    return null;
}

static int? InferVanillaHeartLevel(string key)
{
    foreach (int h in new[] { 14, 12, 10, 8, 6, 4, 2 })
        if (key.Contains($"{h}heart", StringComparison.OrdinalIgnoreCase) || key.Contains($"{h}_heart", StringComparison.OrdinalIgnoreCase))
            return h;
    return null;
}

static string? InferVanillaRelationship(string key)
{
    if (key.Contains("marriage", StringComparison.OrdinalIgnoreCase) || key.Contains("spouse", StringComparison.OrdinalIgnoreCase))
        return "Spouse";
    if (key.Contains("dating", StringComparison.OrdinalIgnoreCase))
        return "Dating";
    return null;
}

static int InferVanillaPriority(string key)
{
    if (key.Contains("marriage", StringComparison.OrdinalIgnoreCase)) return 90;
    if (key.Contains("heart", StringComparison.OrdinalIgnoreCase)) return 80;
    return 70;
}

namespace LivingLoreDialogue.Web
{
    public sealed class LivingLoreWebOptions
    {
        public string DatabasePath { get; set; } = "../ValleyLedger.db";
        public string SchemaPath { get; set; } = "../Data/schema.sql";
        public string SeedPath { get; set; } = "../Data/seed.sql";
        public string OpenAiApiKeyEnvironmentVariable { get; set; } = "OPENAI_API_KEY";

        /// <summary>Dedicated file the user can paste their OpenAI API key into (relative to the content root).</summary>
        public string ApiKeyFilePath { get; set; } = "openai-api-key.txt";

        /// <summary>API key resolved at startup from the env var, configuration, or the key file.</summary>
        public string ResolvedOpenAiApiKey { get; set; } = "";

        public string OpenAiModel { get; set; } = "gpt-4.1-mini";
        public string GamePath { get; set; } = "";
        public string ModsFolderPath { get; set; } = "";
        public bool EnableLiveInGameDialogueGeneration { get; set; } = true;
        public int ScanTimeoutSeconds { get; set; } = 90;
        public int PerFileParseTimeoutMs { get; set; } = 1000;
        public bool EnableScanCache { get; set; } = true;
        public int? MaxDialogueFilesPerScan { get; set; }
        public int MaxRecentMemories { get; set; } = 8;
    }

    public sealed record LoreOverrideRequest(string OverrideType, string FieldName, string OverrideValue, string? Notes);
    public sealed record MemoryRequest(
        long? CharacterId,
        string? SaveFileName,
        string? SaveFilePath,
        string? PlayerName,
        string? FarmName,
        long? PlayerProfileId,
        string? NpcName,
        string? MemoryType,
        string? Title,
        string? Summary,
        string? MemoryText,
        int Importance,
        string? Season,
        int Day,
        int Year,
        string? Location,
        string? Source,
        bool IsActive,
        string? Tags,
        string? ReferenceId)
    {
        public Memory ToMemory()
        {
            string summary = string.IsNullOrWhiteSpace(Summary) ? MemoryText ?? "" : Summary;
            return new Memory
            {
                CharacterId = CharacterId,
                SaveFileName = SaveFileName,
                SaveFilePath = SaveFilePath,
                PlayerName = PlayerName ?? "",
                FarmName = FarmName ?? "",
                PlayerProfileId = PlayerProfileId,
                NpcName = NpcName,
                MemoryType = string.IsNullOrWhiteSpace(MemoryType) ? "Manual" : MemoryType,
                Title = Title ?? "",
                Summary = summary,
                MemoryText = summary,
                Importance = Importance,
                Season = Season ?? "",
                Day = Day,
                Year = Year,
                Location = Location ?? "",
                Source = string.IsNullOrWhiteSpace(Source) ? "Manual" : Source,
                IsActive = IsActive,
                Tags = Tags ?? "",
                ReferenceId = ReferenceId ?? ""
            };
        }
    }
    public sealed record RelationshipRequest(long CharacterA, long CharacterB, string RelationshipType, int Strength);
    public sealed record DialogueTestRequest(
        string CharacterName,
        string? DisplayName,
        string? InterceptedNpcName,
        string? InternalLocationId,
        string? DisplayLocation,
        string? LocationName,
        string Topic,
        string Season,
        string Weather,
        string Location,
        int FriendshipLevel,
        string? RelationshipContext,
        long? PlayerProfileId = null,
        long? ActivePlayerProfileId = null,
        string? RequestSource = null,
        SaveFileContextSnapshot? SaveContext = null);
    public sealed record SettingsRequest(
        string OpenAiModel,
        string? GamePath,
        string ModsFolderPath,
        bool EnableLiveInGameDialogueGeneration,
        int ScanTimeoutSeconds,
        int PerFileParseTimeoutMs,
        bool EnableScanCache,
        int? MaxDialogueFilesPerScan);
    public sealed record ModelRequest(string Model);
    public sealed record SimulateRequest(long ScenarioId, string CharacterName, string Topic);
    public sealed record MergeDuplicatesRequest(string Name, long PrimaryCharacterId);
    public sealed record PlayerProfileRequest(
        string ProfileName,
        string FarmerName,
        string FarmName,
        string? SaveFileName,
        string? SaveFilePath,
        string Description,
        string Backstory,
        string Personality,
        string RoleplayStyle,
        string PreferredTone,
        string ImportantHistory,
        string CurrentGoals,
        string RelationshipNotes,
        string CustomLore);
    public sealed record PlayerProfileAutocompleteRequest(
        string? Concept,
        PlayerProfileDraft? ExistingProfile,
        bool OverwriteExisting = false);
    public sealed record PlayerProfileDraft(
        string? ProfileName,
        string? Description,
        string? Backstory,
        string? Personality,
        string? RoleplayStyle,
        string? PreferredTone,
        string? PreferredDialogueTone,
        string? ImportantHistory,
        string? CurrentGoals,
        string? RelationshipNotes,
        string? CustomLore);
    public sealed record PlayerProfileRelationshipRequest(
        long CanonicalCharacterId,
        string RelationshipType,
        string RelationshipDescription,
        int RelationshipStrength,
        string? CustomNotes);
    public sealed record PlayerProfileMemoryRequest(long? CanonicalCharacterId, string MemoryText, int Importance);
    public sealed record SaveLinkRequest(string SaveFileName, string? SaveFilePath, bool IsDefaultForSave);
    public sealed record VanillaDialogueRequest(string CharacterName, IReadOnlyDictionary<string, string> Entries);
    public sealed record ScenarioRequest(
        string Name,
        string PlayerName,
        string FarmName,
        int Year,
        string Season,
        string Weather,
        string Location,
        int FriendshipHearts,
        string RelationshipState,
        string SeenEvents,
        string CompletedQuests,
        string CommunityCenterState,
        long? PlayerProfileId = null);
}

using System.Collections.Concurrent;
using System.Text.Json;
using HarmonyLib;
using LivingLoreDialogue.Data;
using LivingLoreDialogue.Models;
using LivingLoreDialogue.Repositories;
using LivingLoreDialogue.Services;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;
using SdvCharacter = StardewValley.Character;
using LoreCharacter = LivingLoreDialogue.Models.Character;

namespace LivingLoreDialogue;

public sealed class ModEntry : Mod
{
    private ModConfig config = new();
    private DialogueManager? dialogueManager;
    private LocalDialogueApiClient? localDialogueApiClient;
    private DashboardProcessService? dashboardProcess;
    private CharacterRepository? characterRepository;
    private MemoryRepository? memoryRepository;
    private PlayerProfileRepository? playerProfileRepository;
    private BranchingDialogueSession? activeBranchingSession;
    private NPC? activeBranchingNpc;
    private IReadOnlyList<PlayerDialogueOption> activeBranchingOptions = Array.Empty<PlayerDialogueOption>();
    private string activeBranchingPrompt = "";
    private BranchingUiState branchingUiState = BranchingUiState.ConversationEnded;
    private IReadOnlyList<PlayerDialogueOption> pendingBranchingOptions = Array.Empty<PlayerDialogueOption>();
    private string pendingBranchingNpcResponse = "";
    private string? pendingBranchingEndReason;
    private DateTime branchingTypingStartedAt = DateTime.MinValue;
    private bool branchingConversationLockActive;
    private bool branchingCleanupInProgress;
    private bool branchingAwaitingResponse;

    // Names that are locations/buildings/objects, never valid dialogue characters.
    private static readonly HashSet<string> BlockedSpeakerNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "FarmHouse", "House", "Cabin", "Town", "Farm", "Beach", "Mountain", "Forest", "Desert",
        "SeedShop", "Saloon", "Hospital", "ManorHouse", "ScienceHouse", "AnimalShop", "Blacksmith",
        "FishShop", "JoshHouse", "HaleyHouse", "SamHouse", "Tent", "BusStop", "Backwoods", "Railroad",
        "Woods", "Sewer", "Mine", "Mines", "SkullCave", "WizardHouse", "Greenhouse", "Cellar",
        "Sign", "Chest", "Door", "Gate", "Object", "Building", "Furniture", "TerrainFeature"
    };

    // Actions queued from background generation tasks, flushed on the game thread each tick.
    private readonly ConcurrentQueue<Action> mainThreadActions = new();

    // Debounce so the button-press and menu-change paths don't both handle one interaction.
    private string? lastHandledNpc;
    private DateTime lastHandledAt = DateTime.MinValue;

    // After showing generated dialogue, briefly ignore action buttons so the click that triggered
    // generation (or a held button) cannot instantly dismiss the new dialogue box.
    private const double PostDisplaySuppressionMs = 400;
    private DateTime suppressActionUntil = DateTime.MinValue;
    private bool suppressionWindowActive;

    private enum BranchingUiState
    {
        ChoosingOption,
        WaitingForApiResponse,
        TypingNpcResponse,
        ShowingPlayerOptions,
        ConversationEnded
    }

    // In-memory cache of active character names, refreshed on save load / after scans. The Harmony
    // prefix must decide eligibility synchronously, so it cannot query the database directly.
    private volatile HashSet<string> activeCharacterNames = new(StringComparer.OrdinalIgnoreCase);

    // Known vanilla villagers whose dialogue can be loaded at save-load time via SMAPI content API.
    // These are registered with the server as StardewValley.Vanilla sources so the prompt builder
    // always has canonical examples even when no Content Patcher dialogue mods are installed.
    private static readonly string[] VanillaVillagerNames =
    {
        "Abigail", "Alex", "Caroline", "Clint", "Demetrius", "Elliott", "Emily",
        "Evelyn", "George", "Gus", "Haley", "Harvey", "Jas", "Jodi", "Kent",
        "Leah", "Leo", "Lewis", "Linus", "Marnie", "Maru", "Pam", "Penny",
        "Pierre", "Robin", "Sam", "Sandy", "Sebastian", "Shane", "Vincent",
        "Willy", "Wizard", "Krobus", "Dwarf", "Gunther"
    };

    // Pending Harmony generation tracking: a newer request supersedes an older one for the same NPC.
    private long dialogueRequestCounter;
    private long pendingRequestId;

    private readonly Dictionary<string, string> observedFriendshipMilestones = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> observedRelationshipMilestones = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> observedEventIds = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> observedCompletedQuestIds = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> observedCommunityMilestones = new(StringComparer.OrdinalIgnoreCase);

    public override void Entry(IModHelper helper)
    {
        this.config = helper.ReadConfig<ModConfig>();

        // ---- Loud startup diagnostics ------------------------------------------------------
        this.Monitor.Log("==================== LIVING LORE DIALOGUE ====================", LogLevel.Info);
        this.Monitor.Log("Living Lore Dialogue mod LOADED.", LogLevel.Info);
        this.Monitor.Log("Config loaded:", LogLevel.Info);
        this.Monitor.Log($"  EnableLiveInGameDialogueGeneration = {this.config.EnableLiveInGameDialogueGeneration}", LogLevel.Info);
        this.Monitor.Log($"  OverrideNpcDialogue (suppress vanilla, show generated) = {this.config.OverrideNpcDialogue}", LogLevel.Info);
        this.Monitor.Log($"  EnableBranchingDialogue = {this.config.EnableBranchingDialogue}", LogLevel.Info);
        this.Monitor.Log($"  BranchingDialogueMaxTurns = {this.config.BranchingDialogueMaxTurns}", LogLevel.Info);
        this.Monitor.Log($"  UseLocalWebApiForDialogue = {this.config.UseLocalWebApiForDialogue}", LogLevel.Info);
        this.Monitor.Log($"  Server URL = {this.config.LocalWebApiBaseUrl}", LogLevel.Info);
        this.Monitor.Log($"  UseHarmonyDialogueInterception = {this.config.UseHarmonyDialogueInterception}", LogLevel.Info);
        this.Monitor.Log($"  DebugLogging = {this.config.DebugLogging}", LogLevel.Info);
        this.Monitor.Log($"  PlaceholderDelayMs = {this.config.PlaceholderDelayMs}", LogLevel.Info);
        this.Monitor.Log($"  MaxGenerationWaitMs = {this.config.MaxGenerationWaitMs}", LogLevel.Info);

        // ---- Harmony patch (intercept NPC dialogue before vanilla opens) --------------------
        if (this.config.UseHarmonyDialogueInterception)
            this.ApplyHarmonyPatches();
        else
            this.Monitor.Log("Harmony interception DISABLED by config; using MenuChanged replacement fallback.", LogLevel.Info);

        // ---- Event subscriptions -----------------------------------------------------------
        helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;
        helper.Events.GameLoop.SaveLoaded += this.OnSaveLoaded;
        helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
        helper.Events.Input.ButtonPressed += this.OnButtonPressed;
        helper.Events.Display.MenuChanged += this.OnMenuChanged;
        helper.Events.Display.RenderedActiveMenu += this.OnRenderedActiveMenu;
        helper.Events.Player.Warped += this.OnWarped;
        this.Monitor.Log("Events subscribed: GameLaunched, SaveLoaded, UpdateTicked, Input.ButtonPressed, Display.MenuChanged, Player.Warped.", LogLevel.Info);

        // ---- Console commands --------------------------------------------------------------
        helper.ConsoleCommands.Add(
            "livinglore_dialogue",
            "Generate Living Lore dialogue. Usage: livinglore_dialogue <npcName> [topic]",
            this.HandleDialogueCommand);
        helper.ConsoleCommands.Add(
            "livinglore_testdialogue",
            "Request generated dialogue from the server (RequestSource=SMAPI) and log the result. Usage: livinglore_testdialogue <npcName>",
            this.HandleTestDialogueCommand);
        helper.ConsoleCommands.Add(
            "livinglore_say",
            "Generate dialogue and force-display it in-game. Usage: livinglore_say <npcName>",
            this.HandleSayCommand);
        this.Monitor.Log("Console commands registered: livinglore_dialogue, livinglore_testdialogue, livinglore_say.", LogLevel.Info);
        this.Monitor.Log("=============================================================", LogLevel.Info);
    }

    private async void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        try
        {
            string databasePath = Path.Combine(this.Helper.DirectoryPath, "ValleyLedger.db");
            string schemaPath = Path.Combine(this.Helper.DirectoryPath, "Data", "schema.sql");
            string seedPath = Path.Combine(this.Helper.DirectoryPath, "Data", "seed.sql");

            SqliteConnectionFactory connectionFactory = new(databasePath);
            DatabaseInitializer initializer = new(connectionFactory);
            await initializer.InitializeAsync(schemaPath, seedPath, this.config.EnableSeedDataOnFirstRun);

            CharacterRepository characterRepository = new(connectionFactory);
            this.characterRepository = characterRepository; // used to validate that a speaker is a real, active character
            await this.RefreshActiveCharacterCacheAsync(); // prime the interception eligibility cache from persisted data
            RelationshipRepository relationshipRepository = new(connectionFactory);
            EventRepository eventRepository = new(connectionFactory);
            MemoryRepository memoryRepository = new(connectionFactory);
            this.memoryRepository = memoryRepository;
            VoiceRuleRepository voiceRuleRepository = new(connectionFactory);
            CharacterHistoryRepository characterHistoryRepository = new(connectionFactory);
            LoreChangeLogRepository loreChangeLogRepository = new(connectionFactory);
            UserLoreOverrideRepository userLoreOverrideRepository = new(connectionFactory);
            ScannedModRepository scannedModRepository = new(connectionFactory);
            LoreConflictRepository loreConflictRepository = new(connectionFactory);
            ScanHistoryRepository scanHistoryRepository = new(connectionFactory);
            CharacterValidationRepository characterValidationRepository = new(connectionFactory);
            CanonicalCharacterRepository canonicalCharacterRepository = new(connectionFactory);
            DialogueSourceRepository dialogueSourceRepository = new(connectionFactory);
            ScanOptions scanOptions = new()
            {
                ScanTimeoutSeconds = this.config.ScanTimeoutSeconds,
                PerFileParseTimeoutMs = this.config.PerFileParseTimeoutMs,
                EnableScanCache = this.config.EnableScanCache,
                MaxDialogueFilesPerScan = this.config.MaxDialogueFilesPerScan
            };
            this.playerProfileRepository = new PlayerProfileRepository(connectionFactory);

            if (this.config.EnableDynamicModScanning)
            {
                CharacterSyncService characterSyncService = new(
                    characterRepository,
                    canonicalCharacterRepository,
                    characterHistoryRepository,
                    loreChangeLogRepository,
                    message => this.Monitor.Log(message, LogLevel.Trace));

                ModScanCoordinator scanCoordinator = new(
                    () => Task.FromResult<string?>(this.GetConfiguredModsFolderPath()),
                    () => Task.FromResult<string?>(this.GetConfiguredGamePath()),
                    new ModScannerService(scanOptions),
                    new VanillaCharacterScannerService(),
                    new CharacterValidationService(),
                    characterValidationRepository,
                    canonicalCharacterRepository,
                    characterSyncService,
                    scannedModRepository,
                    loreConflictRepository,
                    scanHistoryRepository,
                    new DialogueSourceScannerService(canonicalCharacterRepository, dialogueSourceRepository, null, scanOptions),
                    message => this.Monitor.Log(message, LogLevel.Info));

                _ = Task.Run(async () =>
                {
                    ModScanSummary summary = await scanCoordinator.RunScanAsync("SMAPI Startup");
                    this.Monitor.Log(
                        $"Living Lore scan complete: success={summary.Success}, mods={summary.ModsScanned}, vanilla={summary.VanillaCharactersFound}, modded={summary.ModdedCharactersFound}, canonical={summary.MergedCanonicalCharacters}, found={summary.CharactersFound}, added={summary.CharactersAdded}, updated={summary.CharactersUpdated}, reactivated={summary.CharactersReactivated}, inactive={summary.CharactersMarkedInactive}, conflicts={summary.ConflictsFound}.",
                        summary.Success ? LogLevel.Info : LogLevel.Warn);

                    foreach (string error in summary.Errors)
                        this.Monitor.Log($"Living Lore scan warning: {error}", LogLevel.Warn);

                    // Refresh the interception eligibility cache with the post-scan active set.
                    await this.RefreshActiveCharacterCacheAsync();
                });
            }

            // Start the bundled localhost dashboard/API automatically when enabled. Failures here
            // are logged but never stop the mod from loading.
            if (this.config.EnableLocalDashboardAutoStart)
                _ = Task.Run(this.StartDashboardAsync);

            if (this.config.UseLocalWebApiForDialogue)
            {
                HttpClient localApiHttpClient = new()
                {
                    BaseAddress = new Uri(this.config.LocalWebApiBaseUrl),
                    Timeout = TimeSpan.FromSeconds(30)
                };
                this.localDialogueApiClient = new LocalDialogueApiClient(
                    localApiHttpClient,
                    message => this.Monitor.Log(message, LogLevel.Info),
                    message => this.Monitor.Log(message, LogLevel.Warn));
                this.Monitor.Log($"Living Lore dialogue client READY. Requests go to {this.config.LocalWebApiBaseUrl}{(this.config.EnableLiveInGameDialogueGeneration ? "" : " (live generation DISABLED in config)")}.", LogLevel.Info);
                return;
            }

            string? apiKey = Environment.GetEnvironmentVariable(this.config.OpenAiApiKeyEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                this.Monitor.Log(
                    $"OpenAI API key was not found. Set {this.config.OpenAiApiKeyEnvironmentVariable} before generating dialogue.",
                    LogLevel.Warn);
                return;
            }

            PromptBuilder promptBuilder = new();
            HttpClient httpClient = new();
            OpenAiDialogueService openAiDialogueService = new(httpClient, promptBuilder, apiKey, this.config.OpenAiModel);

            this.dialogueManager = new DialogueManager(
                characterRepository,
                relationshipRepository,
                eventRepository,
                memoryRepository,
                voiceRuleRepository,
                userLoreOverrideRepository,
                loreChangeLogRepository,
                openAiDialogueService,
                this.config.MaxRecentMemories,
                TimeSpan.FromMinutes(this.config.DialogueCacheMinutes));

            this.Monitor.Log("Living Lore Dialogue initialized.", LogLevel.Info);
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"Failed to initialize Living Lore Dialogue: {ex}", LogLevel.Error);
        }
    }

    private async Task StartDashboardAsync()
    {
        try
        {
            string relativePath = this.config.LocalDashboardRelativePath
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);
            string exePath = Path.Combine(this.Helper.DirectoryPath, relativePath);

            this.dashboardProcess = new DashboardProcessService(
                exePath,
                this.config.LocalDashboardPort,
                this.config.LocalWebApiBaseUrl,
                this.config.DashboardStartupTimeoutSeconds,
                message => this.Monitor.Log(message, LogLevel.Info),
                message => this.Monitor.Log(message, LogLevel.Warn),
                message => this.Monitor.Log(message, LogLevel.Error));

            DashboardProcessService.StartResult result = await this.dashboardProcess.EnsureRunningAsync();

            if (result.Available && this.config.OpenDashboardBrowserOnLaunch)
                this.OpenDashboardInBrowser();

            if (!result.Available && result.Outcome != DashboardProcessService.StartOutcome.HealthPending)
                this.Monitor.Log("Continuing without the local dashboard; dialogue generation may be unavailable until it is running.", LogLevel.Warn);
        }
        catch (Exception ex)
        {
            // Defensive: never let dashboard startup crash the game.
            this.Monitor.Log($"Unexpected error starting the local dashboard: {ex.Message}", LogLevel.Warn);
        }
    }

    private void OpenDashboardInBrowser()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = this.config.LocalWebApiBaseUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"Could not open the dashboard in a browser: {ex.Message}", LogLevel.Trace);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            this.dashboardProcess?.Dispose();

        base.Dispose(disposing);
    }

    // ===================== Lifecycle event logging =========================================

    private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
    {
        string playerName = SafeGet(() => Game1.player?.Name, "Unknown");
        string farmName = SafeGet(() => Game1.player?.farmName?.Value, "Unknown");
        string saveFile = SafeGet(() => Constants.SaveFolderName ?? string.Empty, "(unknown)");
        this.Monitor.Log($"[Event] SaveLoaded. Player='{playerName}', farm='{farmName}', saveFile='{saveFile}', location='{Game1.currentLocation?.NameOrUniqueName}'. Living Lore interaction detection is active.", LogLevel.Info);
        _ = Task.Run(this.RefreshActiveCharacterCacheAsync); // ensure the eligibility cache reflects the loaded save
        this.ResetAutomaticMemoryBaselines(saveFile);

        // Extract vanilla dialogue via SMAPI content API (synchronous on game thread) and register
        // with the server in the background so the prompt builder has canonical examples even when
        // no Content Patcher dialogue mods are installed.
        if (this.localDialogueApiClient is not null)
            this.RegisterVanillaDialogue();
    }

    /// <summary>
    /// Loads vanilla character dialogue from the game's content via SMAPI's content pipeline
    /// (must be called on the game thread), then fires off async HTTP registration to the server.
    /// Sources are stored as StardewValley.Vanilla and are never deactivated by the mod scanner.
    /// </summary>
    private void RegisterVanillaDialogue()
    {
        if (this.localDialogueApiClient is null)
            return;

        Dictionary<string, Dictionary<string, string>> extracted = new(StringComparer.OrdinalIgnoreCase);
        foreach (string name in VanillaVillagerNames)
        {
            try
            {
                Dictionary<string, string>? dialogue = this.Helper.GameContent
                    .Load<Dictionary<string, string>>($"Characters/Dialogue/{name}");
                if (dialogue?.Count > 0)
                    extracted[name] = dialogue;
            }
            catch
            {
                // Some characters (e.g. Dwarf before progression) have no dialogue file; skip silently.
            }
        }

        if (extracted.Count == 0)
        {
            this.Monitor.Log("[VanillaDialogue] No vanilla dialogue found to register; skipping.", LogLevel.Trace);
            return;
        }

        int totalLines = extracted.Values.Sum(d => d.Count);
        this.Monitor.Log($"[VanillaDialogue] Loaded {totalLines} line(s) for {extracted.Count} vanilla character(s); posting to server in background.", LogLevel.Info);

        LocalDialogueApiClient client = this.localDialogueApiClient;
        _ = Task.Run(async () =>
        {
            foreach (KeyValuePair<string, Dictionary<string, string>> pair in extracted)
            {
                await client.RegisterVanillaDialogueAsync(pair.Key, pair.Value);
            }
        });
    }

    private void OnWarped(object? sender, WarpedEventArgs e)
    {
        if (this.config.DebugLogging)
            this.Monitor.Log($"[Event] Player warped to '{e.NewLocation?.NameOrUniqueName}' with {CountVillagers(e.NewLocation)} villager(s).", LogLevel.Trace);
    }

    private void OnRenderedActiveMenu(object? sender, RenderedActiveMenuEventArgs e)
    {
        try
        {
            if (this.activeBranchingSession?.IsActive != true || this.activeBranchingNpc is null)
                return;

            this.DrawBranchingNpcPortrait(e.SpriteBatch, this.activeBranchingSession, this.activeBranchingNpc);
        }
        catch (Exception ex)
        {
            if (this.config.DebugLogging)
                this.Monitor.Log($"[BranchingUI] Failed to draw persistent NPC portrait: {ex.Message}", LogLevel.Trace);
        }
    }

    private void DrawBranchingNpcPortrait(SpriteBatch spriteBatch, BranchingDialogueSession session, NPC npc)
    {
        Texture2D? portrait = TryGetNpcPortrait(npc);

        if (portrait is null)
            return;

        int scale = 4;
        int portraitSize = 64 * scale;
        int margin = 48;
        int x = Math.Max(margin, Game1.uiViewport.Width - portraitSize - margin);
        int y = 96;
        Rectangle panel = new(x - 16, y - 52, portraitSize + 32, portraitSize + 76);

        IClickableMenu.drawTextureBox(
            spriteBatch,
            Game1.menuTexture,
            new Rectangle(0, 256, 60, 60),
            panel.X,
            panel.Y,
            panel.Width,
            panel.Height,
            Color.White,
            1f,
            drawShadow: true);

        spriteBatch.Draw(
            portrait,
            new Rectangle(x, y, portraitSize, portraitSize),
            new Rectangle(0, 0, 64, 64),
            Color.White);

        string name = string.IsNullOrWhiteSpace(session.NpcDisplayName) ? npc.displayName ?? npc.Name : session.NpcDisplayName;
        Vector2 nameSize = Game1.smallFont.MeasureString(name);
        Utility.drawTextWithShadow(
            spriteBatch,
            name,
            Game1.smallFont,
            new Vector2(panel.Center.X - nameSize.X / 2f, panel.Y + 16),
            Game1.textColor);
    }

    private static Texture2D? TryGetNpcPortrait(NPC npc)
    {
        try
        {
            const System.Reflection.BindingFlags flags =
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance;

            System.Reflection.PropertyInfo? property = npc.GetType().GetProperty("Portrait", flags);
            if (property?.GetValue(npc) is Texture2D propertyPortrait)
                return propertyPortrait;

            System.Reflection.FieldInfo? field = npc.GetType().GetField("Portrait", flags);
            if (field?.GetValue(npc) is Texture2D fieldPortrait)
                return fieldPortrait;
        }
        catch
        {
            // Portraits are visual polish; missing/renamed portrait members should not break dialogue.
        }

        return null;
    }

    // Runs every tick on the game thread; flush any dialogue queued by background generation tasks.
    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        while (this.mainThreadActions.TryDequeue(out Action? action))
        {
            try { action(); }
            catch (Exception ex) { this.Monitor.Log($"[Display] Error running queued action: {ex.Message}", LogLevel.Error); }
        }

        // Announce when the post-display input-suppression window closes.
        if (this.suppressionWindowActive && DateTime.UtcNow >= this.suppressActionUntil)
        {
            this.suppressionWindowActive = false;
            this.Monitor.Log("[Input suppression] Ended; action buttons re-enabled for dialogue.", LogLevel.Info);
        }

        if (this.activeBranchingSession?.IsActive == true)
        {
            this.ApplyBranchingConversationLock("update tick");
            this.UpdateBranchingTypingState();
        }

        if (e.IsMultipleOf(60))
            this.CaptureAutomaticMemories();
    }

    private void ResetAutomaticMemoryBaselines(string saveFileName)
    {
        this.observedFriendshipMilestones.Clear();
        this.observedRelationshipMilestones.Clear();
        this.observedEventIds = ReadSeenEventIds();
        this.observedCompletedQuestIds = ReadCompletedQuestIds();
        this.observedCommunityMilestones = ReadCommunityMilestones();

        foreach (string npcName in this.GetMemoryCandidateNpcNames())
        {
            FriendshipSnapshot snapshot = this.GetFriendshipSnapshot(npcName);
            this.observedFriendshipMilestones[npcName] = snapshot.FriendshipMilestone;
            this.observedRelationshipMilestones[npcName] = snapshot.RelationshipMilestone;
        }

        this.Monitor.Log(
            $"[MemoryCapture] SaveLoaded baseline recorded for saveFile='{saveFileName}'. " +
            $"events={this.observedEventIds.Count}, quests={this.observedCompletedQuestIds.Count}, community={this.observedCommunityMilestones.Count}. " +
            "No automatic memory created on load.",
            LogLevel.Info);
    }

    private void CaptureAutomaticMemories()
    {
        if (!Context.IsWorldReady || Game1.player is null || this.memoryRepository is null)
            return;

        SaveFileContextSnapshot save = this.BuildSaveContextSnapshot(null);
        if (string.IsNullOrWhiteSpace(save.SaveFileName))
        {
            this.Monitor.Log("[MemoryCapture] Automatic memory skipped: SaveFileName is missing.", LogLevel.Warn);
            return;
        }

        this.CaptureFriendshipMemories(save);
        this.CaptureEventMemories(save);
        this.CaptureQuestMemories(save);
        this.CaptureCommunityMemories(save);
    }

    private void CaptureFriendshipMemories(SaveFileContextSnapshot save)
    {
        foreach (string npcName in this.GetMemoryCandidateNpcNames())
        {
            FriendshipSnapshot snapshot = this.GetFriendshipSnapshot(npcName);
            string previousFriendship = this.observedFriendshipMilestones.GetValueOrDefault(npcName, "unmet");
            string previousRelationship = this.observedRelationshipMilestones.GetValueOrDefault(npcName, "none");

            if (!snapshot.FriendshipMilestone.Equals(previousFriendship, StringComparison.OrdinalIgnoreCase)
                && FriendshipMilestoneRank(snapshot.FriendshipMilestone) > FriendshipMilestoneRank(previousFriendship))
            {
                this.observedFriendshipMilestones[npcName] = snapshot.FriendshipMilestone;
                string title = snapshot.FriendshipMilestone.Equals("met", StringComparison.OrdinalIgnoreCase)
                    ? $"Met {npcName}"
                    : $"{npcName} reached {snapshot.FriendshipMilestone} hearts";
                string summary = snapshot.FriendshipMilestone.Equals("met", StringComparison.OrdinalIgnoreCase)
                    ? $"{save.PlayerName} met {npcName} in this save."
                    : $"{save.PlayerName} and {npcName} reached {snapshot.FriendshipMilestone} hearts in this save.";
                this.QueueAutomaticMemory(save, npcName, "FriendshipThreshold", $"friendship:{npcName}:{snapshot.FriendshipMilestone}", title, summary, 4, "friendship,automatic");
            }

            if (!snapshot.RelationshipMilestone.Equals(previousRelationship, StringComparison.OrdinalIgnoreCase))
            {
                this.observedRelationshipMilestones[npcName] = snapshot.RelationshipMilestone;
                if (snapshot.RelationshipMilestone is "dating" or "marriage")
                {
                    string title = snapshot.RelationshipMilestone == "dating"
                        ? $"{save.PlayerName} started dating {npcName}"
                        : $"{save.PlayerName} married {npcName}";
                    string summary = snapshot.RelationshipMilestone == "dating"
                        ? $"{save.PlayerName} and {npcName} are dating in this save."
                        : $"{save.PlayerName} and {npcName} are married in this save.";
                    this.QueueAutomaticMemory(save, npcName, "RelationshipStatus", $"relationship:{npcName}:{snapshot.RelationshipMilestone}", title, summary, 5, "relationship,automatic");
                }
            }
        }
    }

    private void CaptureEventMemories(SaveFileContextSnapshot save)
    {
        HashSet<string> current = ReadSeenEventIds();
        foreach (string eventId in current.Except(this.observedEventIds, StringComparer.OrdinalIgnoreCase).Take(10))
        {
            this.observedEventIds.Add(eventId);
            this.QueueAutomaticMemory(
                save,
                npcName: null,
                memoryType: "EventSeen",
                referenceId: $"event:{eventId}",
                title: $"Event seen: {eventId}",
                summary: $"{save.PlayerName} saw event {eventId} in this save.",
                importance: 3,
                tags: $"event,{eventId},automatic");
        }
    }

    private void CaptureQuestMemories(SaveFileContextSnapshot save)
    {
        HashSet<string> current = ReadCompletedQuestIds();
        foreach (string questId in current.Except(this.observedCompletedQuestIds, StringComparer.OrdinalIgnoreCase).Take(10))
        {
            this.observedCompletedQuestIds.Add(questId);
            this.QueueAutomaticMemory(
                save,
                npcName: null,
                memoryType: "QuestCompleted",
                referenceId: $"quest:{questId}",
                title: $"Quest completed: {questId}",
                summary: $"{save.PlayerName} completed quest {questId} in this save.",
                importance: 3,
                tags: $"quest,{questId},automatic");
        }
    }

    private void CaptureCommunityMemories(SaveFileContextSnapshot save)
    {
        HashSet<string> current = ReadCommunityMilestones();
        foreach (string milestone in current.Except(this.observedCommunityMilestones, StringComparer.OrdinalIgnoreCase))
        {
            this.observedCommunityMilestones.Add(milestone);
            this.QueueAutomaticMemory(
                save,
                npcName: null,
                memoryType: "CommunityProgression",
                referenceId: $"community:{milestone}",
                title: $"Community milestone: {milestone}",
                summary: $"{save.PlayerName} reached community milestone {milestone} in this save.",
                importance: milestone.Contains("Complete", StringComparison.OrdinalIgnoreCase) ? 5 : 4,
                tags: $"community,{milestone},automatic");
        }
    }

    private void QueueAutomaticMemory(
        SaveFileContextSnapshot save,
        string? npcName,
        string memoryType,
        string referenceId,
        string title,
        string summary,
        int importance,
        string tags)
    {
        if (this.memoryRepository is null || string.IsNullOrWhiteSpace(save.SaveFileName))
        {
            this.Monitor.Log("[MemoryCapture] Automatic memory skipped: missing repository or save file name.", LogLevel.Warn);
            return;
        }

        this.Monitor.Log($"[MemoryCapture] memory trigger detected. saveFile='{save.SaveFileName}', npc='{npcName ?? "(none)"}', type='{memoryType}', reference='{referenceId}'.", LogLevel.Info);

        MemoryRepository repo = this.memoryRepository;
        CharacterRepository? characters = this.characterRepository;
        PlayerProfileRepository? profiles = this.playerProfileRepository;
        _ = Task.Run(async () =>
        {
            try
            {
                long? characterId = null;
                if (!string.IsNullOrWhiteSpace(npcName) && characters is not null)
                    characterId = (await characters.GetByNameAsync(npcName))?.Id;

                long? playerProfileId = profiles is null ? null : (await ResolvePlayerProfileForMemoryAsync(profiles, save))?.Id;
                LivingLoreDialogue.Models.Memory memory = new()
                {
                    CharacterId = characterId,
                    SaveFileName = save.SaveFileName,
                    SaveFilePath = save.SaveFilePath,
                    PlayerName = save.PlayerName,
                    FarmName = save.FarmName,
                    PlayerProfileId = playerProfileId,
                    NpcName = string.IsNullOrWhiteSpace(npcName) ? null : npcName,
                    MemoryType = memoryType,
                    Title = title,
                    Summary = summary,
                    MemoryText = summary,
                    Importance = importance,
                    Season = save.Season,
                    Day = save.Day,
                    Year = save.Year,
                    Location = save.Location,
                    Source = "Automatic",
                    IsActive = true,
                    Tags = tags,
                    ReferenceId = referenceId
                };

                AutomaticMemoryWriteResult result = await repo.UpsertAutomaticAsync(memory);
                if (result.Inserted)
                    this.Monitor.Log($"[MemoryCapture] memory inserted. saveFile='{save.SaveFileName}', npc='{npcName ?? "(none)"}', id={result.Id}.", LogLevel.Info);
                else if (result.DuplicateSkipped)
                    this.Monitor.Log($"[MemoryCapture] duplicate memory skipped. saveFile='{save.SaveFileName}', npc='{npcName ?? "(none)"}', id={result.Id}.", LogLevel.Info);
                else
                    this.Monitor.Log($"[MemoryCapture] memory skipped. saveFile='{save.SaveFileName}', npc='{npcName ?? "(none)"}', reason='{result.Message}'.", LogLevel.Warn);
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"[MemoryCapture] Error writing automatic memory: {ex.Message}", LogLevel.Warn);
            }
        });
    }

    private IEnumerable<string> GetMemoryCandidateNpcNames()
    {
        return this.activeCharacterNames
            .Concat(VanillaVillagerNames)
            .Where(name => !string.IsNullOrWhiteSpace(name) && !BlockedSpeakerNames.Contains(name))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private FriendshipSnapshot GetFriendshipSnapshot(string npcName)
    {
        int hearts = 0;
        bool hasMet = false;
        bool dating = false;
        bool married = false;

        try
        {
            if (Game1.player.friendshipData.TryGetValue(npcName, out Friendship? friendship) && friendship is not null)
            {
                hearts = Math.Clamp(friendship.Points / 250, 0, 14);
                hasMet = friendship.Points > 0 || friendship.TalkedToToday;
                dating = friendship.Status == FriendshipStatus.Dating;
                married = friendship.Status == FriendshipStatus.Married;
            }

            married = married || string.Equals(Game1.player.spouse, npcName, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            // A missing friendship row is normal for unknown/inactive characters.
        }

        return new FriendshipSnapshot(
            FriendshipMilestoneFor(hearts, hasMet),
            married ? "marriage" : dating ? "dating" : "none");
    }

    private static string FriendshipMilestoneFor(int hearts, bool hasMet)
    {
        if (hearts >= 10)
            return "10";
        if (hearts >= 8)
            return "8";
        if (hearts >= 6)
            return "6";
        if (hearts >= 4)
            return "4";
        if (hearts >= 2)
            return "2";
        return hasMet ? "met" : "unmet";
    }

    private static int FriendshipMilestoneRank(string milestone)
    {
        return milestone.ToLowerInvariant() switch
        {
            "met" => 1,
            "2" => 2,
            "4" => 3,
            "6" => 4,
            "8" => 5,
            "10" => 6,
            _ => 0
        };
    }

    private static HashSet<string> ReadSeenEventIds()
    {
        return ReadStringSet(() => Game1.player.eventsSeen.Select(id => id.ToString()));
    }

    private static HashSet<string> ReadCompletedQuestIds()
    {
        HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
        try
        {
            object? player = Game1.player;
            foreach (string memberName in new[] { "completedQuestIds", "completedQuests" })
            {
                object? value = player?.GetType().GetProperty(memberName)?.GetValue(player)
                    ?? player?.GetType().GetField(memberName)?.GetValue(player);
                if (value is System.Collections.IEnumerable enumerable and not string)
                {
                    foreach (object? item in enumerable)
                    {
                        if (!string.IsNullOrWhiteSpace(item?.ToString()))
                            ids.Add(item.ToString()!);
                    }
                }
            }
        }
        catch
        {
            // Quest APIs have varied across SDV versions; failing closed avoids noisy memories.
        }

        return ids;
    }

    private static HashSet<string> ReadCommunityMilestones()
    {
        HashSet<string> milestones = new(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (Game1.MasterPlayer.mailReceived.Contains("JojaMember"))
                milestones.Add("JojaMember");
            if (Game1.MasterPlayer.mailReceived.Contains("cc_Complete"))
                milestones.Add("CommunityCenterComplete");
            if (Game1.MasterPlayer.mailReceived.Contains("joja_Begin"))
                milestones.Add("JojaRouteStarted");
            if (Game1.MasterPlayer.mailReceived.Contains("joja_Complete"))
                milestones.Add("JojaRouteComplete");
            if (Game1.MasterPlayer.mailReceived.Contains("Minecart"))
                milestones.Add("MinecartsUnlocked");
            if (Game1.MasterPlayer.mailReceived.Contains("ccVault"))
                milestones.Add("BusUnlocked");
        }
        catch
        {
            // Mail flags are best-effort progression hints.
        }

        return milestones;
    }

    private static HashSet<string> ReadStringSet(Func<IEnumerable<string>> getter)
    {
        try
        {
            return getter()
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static async Task<PlayerProfile?> ResolvePlayerProfileForMemoryAsync(PlayerProfileRepository profiles, SaveFileContextSnapshot save)
    {
        if (!string.IsNullOrWhiteSpace(save.SaveFileName))
        {
            PlayerProfile? linked = await profiles.GetBySaveFileAsync(save.SaveFileName);
            if (linked is not null)
                return linked;
        }

        PlayerProfile? matched = await profiles.GetByFarmerAndFarmAsync(save.PlayerName, save.FarmName);
        if (matched is not null)
            return matched;

        return await profiles.GetActiveAsync();
    }

    // ===================== Interaction detection (button press) =============================

    // The action button no longer triggers generation. We only use it to swallow the click that
    // would otherwise dismiss a freshly displayed generated dialogue box.
    private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
        try
        {
            if (this.activeBranchingSession?.IsActive == true && (e.Button is SButton.Escape or SButton.ControllerB))
            {
                this.Helper.Input.Suppress(e.Button);
                this.EndBranchingConversation("cancel button");
                return;
            }

            if (this.activeBranchingSession?.IsActive == true && this.branchingAwaitingResponse && e.Button.IsActionButton())
            {
                this.Helper.Input.Suppress(e.Button);
                this.Monitor.Log($"[Branching] Suppressed {e.Button} while waiting for API response.", LogLevel.Trace);
                return;
            }

            if (!Context.IsWorldReady || !e.Button.IsActionButton())
                return;

            if (DateTime.UtcNow < this.suppressActionUntil)
            {
                this.Helper.Input.Suppress(e.Button);
                if (this.config.DebugLogging)
                    this.Monitor.Log($"[Input suppression] Ignored {e.Button} (post-display guard active).", LogLevel.Trace);
            }
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"[Input] Error in OnButtonPressed: {ex.Message}", LogLevel.Error);
        }
    }

    // ===================== Primary trigger: a dialogue box opened ===========================
    // Game1.currentSpeaker is the source of truth. We never use the clicked tile/object/location
    // as the character identity.

    private void OnMenuChanged(object? sender, MenuChangedEventArgs e)
    {
        try
        {
            if (e.NewMenu is not DialogueBox)
                return;

            NPC? speaker = Game1.currentSpeaker;
            this.Monitor.Log(
                $"[MenuChanged] DialogueBox opened. currentSpeaker name='{speaker?.Name ?? "null"}', " +
                $"display='{speaker?.displayName ?? "null"}', text='{Preview(GetCurrentDialogueText())}'.",
                LogLevel.Info);

            if (this.activeBranchingSession?.IsActive == true)
            {
                this.Monitor.Log(
                    $"[MenuChanged] Ignoring DialogueBox during active branching session={this.activeBranchingSession.SessionId}; prevents continuity reset.",
                    LogLevel.Trace);
                return;
            }

            // Tasks 3/8: no NPC speaker means this is not a character conversation (sign, letter,
            // event, our own generated box, etc.). Leave vanilla alone.
            if (speaker is null)
            {
                this.Monitor.Log("[MenuChanged] REJECTED: no currentSpeaker (not an NPC conversation). Leaving vanilla dialogue.", LogLevel.Info);
                return;
            }
            if (string.IsNullOrEmpty(speaker.Name))
            {
                this.Monitor.Log("[MenuChanged] REJECTED: speaker has an empty name. Leaving vanilla dialogue.", LogLevel.Info);
                return;
            }

            if (!this.config.EnableLiveInGameDialogueGeneration || !this.config.OverrideNpcDialogue)
            {
                if (this.config.DebugLogging)
                    this.Monitor.Log("[MenuChanged] Generation/override disabled in config; leaving vanilla dialogue.", LogLevel.Trace);
                return;
            }

            // When Harmony interception is active it handles dialogue before vanilla opens, so the
            // MenuChanged replacement path must stand down to avoid the two systems fighting.
            if (this.config.UseHarmonyDialogueInterception)
            {
                if (this.config.DebugLogging)
                    this.Monitor.Log("[MenuChanged] Harmony interception is active; MenuChanged replacement is disabled (log only).", LogLevel.Trace);
                return;
            }

            // Debounce: also stops our own replacement box (same speaker) from re-triggering.
            if (this.RecentlyHandled(speaker.Name))
            {
                if (this.config.DebugLogging)
                    this.Monitor.Log($"[MenuChanged] '{speaker.Name}' recently handled; skipping.", LogLevel.Trace);
                return;
            }
            this.MarkHandled(speaker.Name);

            if (this.config.EnableBranchingDialogue)
            {
                if (Game1.activeClickableMenu is DialogueBox)
                    Game1.exitActiveMenu();
                this.StartBranchingConversation(speaker, ++this.dialogueRequestCounter);
            }
            else
            {
                this.RequestAndReplace(speaker);
            }
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"[MenuChanged] Error: {ex.Message}", LogLevel.Error);
        }
    }

    /// <summary>
    /// Validates that a dialogue speaker is a real, active character. Returns a rejection reason,
    /// or null if the speaker is accepted.
    /// </summary>
    private async Task<string?> ValidateSpeakerAsync(NPC speaker)
    {
        if (string.IsNullOrWhiteSpace(speaker.Name))
            return "speaker has no name";
        if (!speaker.IsVillager)
            return "speaker is not a villager NPC (object/animal/monster)";
        if (BlockedSpeakerNames.Contains(speaker.Name))
            return $"name '{speaker.Name}' is a known location/building/object, not a character";
        if (this.characterRepository is null)
            return "character repository not initialized";
        if (!await this.characterRepository.IsActiveCharacterAsync(speaker.Name))
            return $"'{speaker.Name}' is not present in the active characters table";
        return null;
    }

    // ===================== Harmony interception (NPC.checkAction prefix) ====================

    private void ApplyHarmonyPatches()
    {
        try
        {
            NpcCheckActionPatch.Mod = this;
            Harmony harmony = new(this.ModManifest.UniqueID);
            harmony.Patch(
                original: AccessTools.Method(typeof(NPC), nameof(NPC.checkAction)),
                prefix: new HarmonyMethod(typeof(NpcCheckActionPatch), nameof(NpcCheckActionPatch.Prefix)));
            this.Monitor.Log("Harmony patches applied: NPC.checkAction prefix (dialogue interception).", LogLevel.Info);
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"[Harmony] Failed to patch NPC.checkAction: {ex}. Falling back to MenuChanged replacement.", LogLevel.Error);
            // Disable interception so the MenuChanged path takes over.
            this.config.UseHarmonyDialogueInterception = false;
        }
    }

    private async Task RefreshActiveCharacterCacheAsync()
    {
        try
        {
            if (this.characterRepository is null)
                return;
            IReadOnlyList<string> names = await this.characterRepository.GetActiveNamesAsync();
            this.activeCharacterNames = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
            this.Monitor.Log($"[Harmony] Active character cache refreshed: {this.activeCharacterNames.Count} name(s).", LogLevel.Info);
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"[Harmony] Active character cache refresh failed: {ex.Message}", LogLevel.Warn);
        }
    }

    private bool IsEligibleSpeaker(NPC npc, out string reason)
    {
        reason = "";
        if (npc is null || string.IsNullOrEmpty(npc.Name)) { reason = "no NPC/name"; return false; }
        if (!npc.IsVillager) { reason = "not a villager NPC"; return false; }
        if (BlockedSpeakerNames.Contains(npc.Name)) { reason = $"'{npc.Name}' is a location/building/object"; return false; }
        if (!this.activeCharacterNames.Contains(npc.Name)) { reason = $"'{npc.Name}' not in active character cache"; return false; }
        return true;
    }

    /// <summary>
    /// Called by the Harmony prefix on the game thread. Returns true to let vanilla NPC.checkAction
    /// run, or false to intercept (after showing a placeholder and starting async generation).
    /// </summary>
    internal bool TryInterceptNpcDialogue(NPC npc, Farmer who, ref bool result)
    {
        // Respect master switches; any "no" means vanilla behavior.
        if (!this.config.UseHarmonyDialogueInterception || !this.config.EnableLiveInGameDialogueGeneration || !this.config.OverrideNpcDialogue)
            return true;
        if (!Context.IsWorldReady || !Context.IsPlayerFree)
            return true;
        if (who is null || !who.IsLocalPlayer)
            return true;
        // Holding an item (e.g. a gift) -> let vanilla handle gifting/tool use.
        if (who.ActiveObject is not null)
            return true;

        if (!this.IsEligibleSpeaker(npc, out string reason))
        {
            if (this.config.DebugLogging)
                this.Monitor.Log($"[Harmony] checkAction for '{npc?.Name ?? "null"}' NOT eligible ({reason}); vanilla dialogue continues.", LogLevel.Trace);
            return true; // task 3: not eligible -> vanilla
        }

        // Eligible: intercept before vanilla opens any dialogue.
        long requestId = ++this.dialogueRequestCounter;
        this.pendingRequestId = requestId;
        this.Monitor.Log($"[Harmony] INTERCEPTING checkAction for '{npc.Name}' (request #{requestId}). Starting generation with delayed placeholder.", LogLevel.Info);

        result = true; // tell the game the action was handled

        if (this.config.EnableBranchingDialogue)
            this.StartBranchingConversation(npc, requestId);
        else
            this.RequestGeneratedForHarmony(npc, requestId);

        return false; // task 4: skip vanilla NPC.checkAction
    }

    private void StartBranchingConversation(NPC npc, long requestId)
    {
        if (this.localDialogueApiClient is null)
        {
            this.Monitor.Log("[Branching] Local web API client is not available; falling back to single-line generation.", LogLevel.Warn);
            this.RequestGeneratedForHarmony(npc, requestId);
            return;
        }

        DialogueContext context = this.BuildContext(npc, "branching conversation", "SMAPI-Branching");
        SaveFileContextSnapshot save = context.SaveContext ?? this.BuildSaveContextSnapshot(npc.Name);
        BranchingDialogueSession session = new()
        {
            NpcName = npc.Name,
            NpcDisplayName = npc.displayName ?? npc.Name,
            PlayerName = save.PlayerName,
            SaveContext = save,
            MaxTurnCount = Math.Max(1, this.config.BranchingDialogueMaxTurns),
            IsActive = true
        };

        this.activeBranchingSession = session;
        this.activeBranchingNpc = npc;
        this.pendingRequestId = requestId;
        this.MarkHandled(npc.Name);
        this.AcquireBranchingConversationLock(session, npc, "conversation start");
        this.Monitor.Log(
            $"[Branching] Conversation start session={session.SessionId}, npc={session.NpcName}, player={session.PlayerName}, hearts={save.FriendshipHearts}, relationship={save.RelationshipState}, location={save.Location}.",
            LogLevel.Info);

        this.RequestBranchingAsync(session, npc, "opening_options", null, requestId);
    }

    private void RequestBranchingAsync(
        BranchingDialogueSession session,
        NPC npc,
        string mode,
        PlayerDialogueOption? selectedOption,
        long requestId)
    {
        DialogueContext context = this.BuildContext(npc, "branching conversation", "SMAPI-Branching");
        BranchingDialogueRequest request = new()
        {
            SessionId = session.SessionId,
            Context = context,
            SaveContext = context.SaveContext,
            Mode = mode,
            TurnCount = session.TurnCount,
            MaxTurnCount = session.MaxTurnCount,
            SelectedOptionId = selectedOption?.Id ?? "",
            SelectedOptionText = selectedOption?.Text ?? "",
            History = session.Turns.ToArray()
        };

        if (selectedOption is not null)
            this.Monitor.Log($"[Branching] Selected player choice session={session.SessionId}, npc={session.NpcName}, id={selectedOption.Id}, text={Preview(selectedOption.Text)}.", LogLevel.Info);
        this.Monitor.Log(
            $"[Branching] Request context session={session.SessionId}, mode={mode}, turnCount={session.TurnCount}, selected='{Preview(selectedOption?.Text ?? "")}', historyEntries={session.Turns.Count}, latestPlayer='{Preview(session.Turns.LastOrDefault()?.PlayerChoiceText ?? "")}', latestNpc='{Preview(session.Turns.LastOrDefault()?.NpcResponse ?? "")}', recentHistory='{Preview(FormatRecentBranchingHistory(session))}'.",
            LogLevel.Info);
        this.EnterBranchingWaitingState(session, npc, selectedOption is null ? "opening options" : "selected option");

        _ = Task.Run(async () =>
        {
            BranchingDialogueResponse? response = this.localDialogueApiClient is null
                ? null
                : await this.localDialogueApiClient.GenerateBranchingAsync(request, "SMAPI-Branching");

            response ??= CreateFallbackBranchingResponse(mode);
            EnsureFiveBranchingOptions(response, mode);
            if (!string.IsNullOrWhiteSpace(response.Error))
                this.Monitor.Log($"[Branching] API request returned fallback/error session={session.SessionId}: {response.Error}", LogLevel.Warn);
            else
                this.Monitor.Log($"[Branching] API request success session={session.SessionId}, mode={mode}, options={response.PlayerOptions.Count}.", LogLevel.Info);

            this.mainThreadActions.Enqueue(() =>
            {
                if (!Context.IsWorldReady || this.activeBranchingSession != session || requestId != this.pendingRequestId || !session.IsActive)
                {
                    this.Monitor.Log($"[Branching] Discarded response for inactive/superseded session={session.SessionId}.", LogLevel.Info);
                    return;
                }

                if (response.PlayerOptions.Count == 0)
                {
                    this.Monitor.Log($"[Branching] Malformed response handling session={session.SessionId}: no player options.", LogLevel.Warn);
                    response = CreateFallbackBranchingResponse(mode);
                }
                EnsureFiveBranchingOptions(response, mode);

                session.PlayerProfileName = response.ActivePlayerProfileName;
                session.PlayerProfileMatchMethod = response.PlayerProfileMatchMethod;
                session.PlayerProfileSummary = string.IsNullOrWhiteSpace(response.ActivePlayerProfileName) ? "Default Stardew farmer profile" : response.ActivePlayerProfileName;

                if (selectedOption is not null && !selectedOption.IsNpcInitiates)
                {
                    session.Turns.Add(new BranchingDialogueTurn
                    {
                        PlayerChoiceId = selectedOption.Id,
                        PlayerChoiceText = selectedOption.Text,
                        NpcResponse = response.NpcResponse
                    });
                    session.TurnCount++;
                }

                if (response.ConversationShouldEnd || session.TurnCount >= session.MaxTurnCount)
                {
                    string endReason = response.ConversationShouldEnd ? "model requested end" : "max turn count";
                    if (!string.IsNullOrWhiteSpace(response.NpcResponse))
                        this.ShowBranchingNpcTyping(session, npc, response.NpcResponse, Array.Empty<PlayerDialogueOption>(), endReason);
                    else
                        this.EndBranchingConversation(endReason);
                    return;
                }

                if (string.IsNullOrWhiteSpace(response.NpcResponse))
                    this.ShowBranchingOptions(session, npc, response.NpcResponse, response.PlayerOptions);
                else
                    this.ShowBranchingNpcTyping(session, npc, response.NpcResponse, response.PlayerOptions, endReason: null);
            });
        });
    }

    private void ShowBranchingOptions(
        BranchingDialogueSession session,
        NPC npc,
        string npcResponse,
        IReadOnlyList<PlayerDialogueOption> options)
    {
        this.ApplyBranchingConversationLock("show options");
        this.branchingAwaitingResponse = false;
        this.branchingUiState = BranchingUiState.ShowingPlayerOptions;
        this.pendingBranchingOptions = Array.Empty<PlayerDialogueOption>();
        this.pendingBranchingNpcResponse = "";
        this.pendingBranchingEndReason = null;
        if (Game1.activeClickableMenu is not null)
            Game1.exitActiveMenu();

        this.activeBranchingOptions = options.ToArray();
        Response[] responses = this.activeBranchingOptions
            .Select(option => new Response(option.Id, option.Text))
            .ToArray();

        string prompt = string.IsNullOrWhiteSpace(npcResponse)
            ? $"Talk to {session.NpcDisplayName}:"
            : npcResponse.Trim();

        this.activeBranchingPrompt = prompt;
        Game1.currentLocation.createQuestionDialogue(prompt, responses, this.OnBranchingOptionSelected);
        Game1.currentSpeaker = npc;
        this.suppressActionUntil = DateTime.UtcNow.AddMilliseconds(PostDisplaySuppressionMs);
        this.suppressionWindowActive = true;
        this.Monitor.Log($"[Branching] Displayed options session={session.SessionId}, npc={session.NpcName}, prompt={Preview(prompt)}, options={options.Count}.", LogLevel.Info);
    }

    private void ShowBranchingNpcTyping(
        BranchingDialogueSession session,
        NPC npc,
        string npcResponse,
        IReadOnlyList<PlayerDialogueOption> nextOptions,
        string? endReason)
    {
        if (!Context.IsWorldReady || this.activeBranchingSession != session || !session.IsActive)
            return;

        this.ApplyBranchingConversationLock("npc typing response");
        this.branchingAwaitingResponse = false;
        this.branchingUiState = BranchingUiState.TypingNpcResponse;
        this.pendingBranchingNpcResponse = npcResponse.Trim();
        this.pendingBranchingOptions = nextOptions.ToArray();
        this.pendingBranchingEndReason = endReason;
        this.branchingTypingStartedAt = DateTime.UtcNow;

        if (Game1.activeClickableMenu is not null)
            Game1.exitActiveMenu();

        try
        {
            StardewValley.Dialogue dialogue = new(npc, null, this.pendingBranchingNpcResponse);
            npc.CurrentDialogue.Clear();
            npc.CurrentDialogue.Push(dialogue);
            Game1.currentSpeaker = npc;
            Game1.drawDialogue(npc);
            this.Monitor.Log($"[Branching] NPC typing started session={session.SessionId}, npc={session.NpcName}, response={Preview(this.pendingBranchingNpcResponse)}, nextOptions={this.pendingBranchingOptions.Count}, endReason={endReason ?? "(none)"}.", LogLevel.Info);
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"[Branching] NPC typing display failed ({ex.Message}); showing options immediately.", LogLevel.Warn);
            if (!string.IsNullOrWhiteSpace(endReason))
                this.EndBranchingConversation(endReason);
            else
                this.ShowBranchingOptions(session, npc, this.pendingBranchingNpcResponse, this.pendingBranchingOptions);
        }
    }

    private void UpdateBranchingTypingState()
    {
        if (this.branchingUiState != BranchingUiState.TypingNpcResponse)
            return;

        BranchingDialogueSession? session = this.activeBranchingSession;
        NPC? npc = this.activeBranchingNpc;
        if (session is null || npc is null || !session.IsActive)
            return;

        if (!this.IsBranchingNpcTypingComplete())
            return;

        this.Monitor.Log($"[Branching] NPC typing complete session={session.SessionId}, npc={session.NpcName}, nextOptions={this.pendingBranchingOptions.Count}, endReason={this.pendingBranchingEndReason ?? "(none)"}.", LogLevel.Info);
        string? endReason = this.pendingBranchingEndReason;
        IReadOnlyList<PlayerDialogueOption> nextOptions = this.pendingBranchingOptions;
        string prompt = this.pendingBranchingNpcResponse;

        this.pendingBranchingEndReason = null;
        this.pendingBranchingOptions = Array.Empty<PlayerDialogueOption>();
        this.pendingBranchingNpcResponse = "";

        if (!string.IsNullOrWhiteSpace(endReason))
        {
            this.EndBranchingConversation(endReason);
            return;
        }

        this.ShowBranchingOptions(session, npc, prompt, nextOptions);
    }

    private bool IsBranchingNpcTypingComplete()
    {
        if (Game1.activeClickableMenu is not DialogueBox)
            return (DateTime.UtcNow - this.branchingTypingStartedAt).TotalMilliseconds > 250;

        string current = GetCurrentDialogueText();
        if (string.IsNullOrWhiteSpace(current))
            return false;

        string expected = NormalizeDialogueForCompletion(this.pendingBranchingNpcResponse);
        string shown = NormalizeDialogueForCompletion(current);
        return shown.Length >= expected.Length;
    }

    private void EnterBranchingWaitingState(BranchingDialogueSession session, NPC npc, string reason)
    {
        if (!Context.IsWorldReady || this.activeBranchingSession != session || !session.IsActive)
            return;

        this.ApplyBranchingConversationLock("waiting for response");
        this.branchingAwaitingResponse = true;
        this.branchingUiState = BranchingUiState.WaitingForApiResponse;
        Game1.currentSpeaker = npc;

        if (Game1.activeClickableMenu is null && this.activeBranchingOptions.Count > 0)
        {
            Response[] responses = this.activeBranchingOptions
                .Select(option => new Response(option.Id, option.Text))
                .ToArray();
            string prompt = string.IsNullOrWhiteSpace(this.activeBranchingPrompt)
                ? $"Talk to {session.NpcDisplayName}:"
                : this.activeBranchingPrompt;
            Game1.currentLocation.createQuestionDialogue(prompt, responses, this.OnBranchingOptionSelected);
            Game1.currentSpeaker = npc;
        }

        this.Monitor.Log(
            $"[Branching] Waiting for API response session={session.SessionId}, npc={session.NpcName}, reason={reason}, keeping current menu={Game1.activeClickableMenu?.GetType().Name ?? "(none)"} visible.",
            LogLevel.Info);
    }

    private void OnBranchingOptionSelected(Farmer who, string answer)
    {
        BranchingDialogueSession? session = this.activeBranchingSession;
        NPC? npc = this.activeBranchingNpc;
        if (session is null || npc is null || !session.IsActive)
            return;

        if (this.branchingAwaitingResponse || this.branchingUiState is BranchingUiState.WaitingForApiResponse or BranchingUiState.TypingNpcResponse)
        {
            this.Monitor.Log($"[Branching] Ignored option selection while awaiting API response session={session.SessionId}, answer={answer}.", LogLevel.Trace);
            return;
        }

        PlayerDialogueOption? selected = this.activeBranchingOptions.FirstOrDefault(option => option.Id == answer)
            ?? this.activeBranchingOptions.FirstOrDefault(option => option.Text == answer);

        if (selected is null)
        {
            this.Monitor.Log($"[Branching] Malformed selection session={session.SessionId}: answer={answer}. Ending conversation.", LogLevel.Warn);
            this.EndBranchingConversation("invalid option");
            return;
        }

        if (selected.IsExit)
        {
            this.Monitor.Log($"[Branching] Selected conversation-ending option session={session.SessionId}, text={Preview(selected.Text)}.", LogLevel.Info);
            this.EndBranchingConversation("player selected exit option");
            return;
        }

        string mode = selected.IsNpcInitiates ? "npc_initiates" : "turn";
        this.branchingUiState = BranchingUiState.WaitingForApiResponse;
        this.RequestBranchingAsync(session, npc, mode, selected, this.pendingRequestId);
    }

    private void EndBranchingConversation(string reason)
    {
        if (this.branchingCleanupInProgress)
            return;

        this.branchingCleanupInProgress = true;
        BranchingDialogueSession? session = this.activeBranchingSession;
        if (session is not null)
        {
            session.IsActive = false;
            session.IsEnded = true;
            this.Monitor.Log($"[Branching] Conversation end session={session.SessionId}, npc={session.NpcName}, reason={reason}, turns={session.TurnCount}.", LogLevel.Info);
        }

        this.Monitor.Log($"[Branching] Cleanup begin reason={reason}; before={this.DescribeBranchingGameState()}.", LogLevel.Info);

        this.activeBranchingSession = null;
        this.activeBranchingNpc = null;
        this.activeBranchingOptions = Array.Empty<PlayerDialogueOption>();
        this.activeBranchingPrompt = "";
        this.pendingBranchingOptions = Array.Empty<PlayerDialogueOption>();
        this.pendingBranchingNpcResponse = "";
        this.pendingBranchingEndReason = null;
        this.branchingUiState = BranchingUiState.ConversationEnded;
        this.branchingConversationLockActive = false;
        this.branchingAwaitingResponse = false;
        this.suppressionWindowActive = false;
        this.suppressActionUntil = DateTime.MinValue;

        try
        {
            if (Game1.activeClickableMenu is not null)
                Game1.exitActiveMenu();
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"[Branching] Cleanup menu close failed: {ex.Message}", LogLevel.Warn);
        }

        this.ReleaseBranchingConversationLock(reason);
        this.Monitor.Log($"[Branching] Cleanup complete reason={reason}; after={this.DescribeBranchingGameState()}.", LogLevel.Info);
        this.branchingCleanupInProgress = false;
    }

    private static BranchingDialogueResponse CreateFallbackBranchingResponse(string mode)
    {
        if (mode.Equals("opening_options", StringComparison.OrdinalIgnoreCase))
        {
            return new BranchingDialogueResponse
            {
                PlayerOptions = new[]
                {
                    new PlayerDialogueOption { Id = "fallback_open", Text = "Hi. How are you doing?", Action = "choose" },
                    new PlayerDialogueOption { Id = "fallback_open_town", Text = "How have things been around town?", Action = "choose" },
                    new PlayerDialogueOption { Id = "fallback_open_chat", Text = "Got a minute to talk?", Action = "choose" },
                    new PlayerDialogueOption { Id = "let_them_speak_first", Text = "Let them speak first.", Action = "npc_initiates" },
                    new PlayerDialogueOption { Id = "exit", Text = "Never mind.", Action = "exit", EndsConversation = true }
                },
                Error = "Fallback branching options used."
            };
        }

        return new BranchingDialogueResponse
        {
            NpcResponse = "Let's talk about something simple for now.",
            PlayerOptions = new[]
            {
                new PlayerDialogueOption { Id = "fallback_continue", Text = "That's okay.", Action = "choose" },
                new PlayerDialogueOption { Id = "fallback_more", Text = "Tell me more.", Action = "choose" },
                new PlayerDialogueOption { Id = "fallback_day", Text = "How has your day been otherwise?", Action = "choose" },
                new PlayerDialogueOption { Id = "fallback_farm", Text = "I've been keeping busy on the farm.", Action = "choose" },
                new PlayerDialogueOption { Id = "fallback_end", Text = "I should get going.", Action = "exit", EndsConversation = true }
            },
            Error = "Fallback branching response used."
        };
    }

    private static void EnsureFiveBranchingOptions(BranchingDialogueResponse response, string mode)
    {
        bool opening = mode.Equals("opening_options", StringComparison.OrdinalIgnoreCase);
        List<PlayerDialogueOption> options = response.PlayerOptions
            .Where(option => !string.IsNullOrWhiteSpace(option.Text))
            .ToList();

        PlayerDialogueOption? exit = options.FirstOrDefault(option => option.IsExit);
        if (exit is null)
            exit = new PlayerDialogueOption { Id = opening ? "exit" : "end_conversation", Text = opening ? "Never mind." : "I should get going.", Action = "exit", EndsConversation = true };

        PlayerDialogueOption? speakFirst = opening
            ? options.FirstOrDefault(option => option.IsNpcInitiates)
            : null;
        if (opening && speakFirst is null)
            speakFirst = new PlayerDialogueOption { Id = "let_them_speak_first", Text = "Let them speak first.", Action = "npc_initiates" };

        List<PlayerDialogueOption> normal = options
            .Where(option => !option.IsExit && !option.IsNpcInitiates)
            .Take(opening ? 3 : 4)
            .ToList();

        string[] fallbackTexts = opening
            ? new[] { "Hi. How are you doing?", "How have things been around town?", "Got a minute to talk?" }
            : new[] { "That's okay.", "Tell me more.", "How has your day been otherwise?", "I've been keeping busy on the farm." };

        int index = 0;
        while (normal.Count < (opening ? 3 : 4) && index < fallbackTexts.Length)
        {
            string text = fallbackTexts[index++];
            if (normal.Any(option => option.Text.Equals(text, StringComparison.OrdinalIgnoreCase)))
                continue;
            normal.Add(new PlayerDialogueOption { Id = $"fallback_option_{index}", Text = text, Action = "choose" });
        }

        response.PlayerOptions = opening
            ? normal.Concat(new[] { speakFirst!, exit }).Take(5).ToArray()
            : normal.Concat(new[] { exit }).Take(5).ToArray();
    }

    private void AcquireBranchingConversationLock(BranchingDialogueSession session, NPC npc, string reason)
    {
        this.branchingConversationLockActive = true;
        Game1.currentSpeaker = npc;
        this.ApplyBranchingConversationLock(reason);
        this.Monitor.Log($"[Branching] Lock acquired session={session.SessionId}, reason={reason}; state={this.DescribeBranchingGameState()}.", LogLevel.Info);
    }

    private void ApplyBranchingConversationLock(string reason)
    {
        if (!this.branchingConversationLockActive || !Context.IsWorldReady)
            return;

        TrySetStaticBool(typeof(Game1), "freezeControls", true);
        TrySetPlayerBool("CanMove", false);
        TrySetPlayerBool("canMove", false);

        if (this.config.DebugLogging)
            this.Monitor.Log($"[Branching] Lock maintained reason={reason}; state={this.DescribeBranchingGameState()}.", LogLevel.Trace);
    }

    private void ReleaseBranchingConversationLock(string reason)
    {
        TrySetStaticBool(typeof(Game1), "freezeControls", false);
        TrySetStaticBool(typeof(Game1), "dialogueUp", false);
        TrySetStaticBool(typeof(Game1), "eventUp", false);
        TrySetPlayerBool("CanMove", true);
        TrySetPlayerBool("canMove", true);

        try
        {
            Game1.currentSpeaker = null;
        }
        catch
        {
            // Best-effort cleanup; currentSpeaker is diagnostic/UI state, not worth failing cleanup.
        }

        this.Monitor.Log($"[Branching] Lock released reason={reason}; state={this.DescribeBranchingGameState()}.", LogLevel.Info);
    }

    private string DescribeBranchingGameState()
    {
        string menu = Game1.activeClickableMenu?.GetType().Name ?? "(none)";
        string speaker = Game1.currentSpeaker?.Name ?? "(none)";
        string freezeControls = TryGetStaticBool(typeof(Game1), "freezeControls")?.ToString() ?? "(unknown)";
        string dialogueUp = TryGetStaticBool(typeof(Game1), "dialogueUp")?.ToString() ?? "(unknown)";
        string eventUp = TryGetStaticBool(typeof(Game1), "eventUp")?.ToString() ?? "(unknown)";
        string canMove = TryGetPlayerBool("CanMove")?.ToString()
            ?? TryGetPlayerBool("canMove")?.ToString()
            ?? "(unknown)";
        return $"menu={menu}, speaker={speaker}, freezeControls={freezeControls}, dialogueUp={dialogueUp}, eventUp={eventUp}, playerCanMove={canMove}, lockActive={this.branchingConversationLockActive}, awaitingResponse={this.branchingAwaitingResponse}";
    }

    private static string FormatRecentBranchingHistory(BranchingDialogueSession session)
    {
        return string.Join(" | ", session.Turns.TakeLast(3).Select(turn =>
            $"P: {turn.PlayerChoiceText} / NPC: {turn.NpcResponse}"));
    }

    private static bool? TryGetStaticBool(Type type, string name)
    {
        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Static;

        try
        {
            System.Reflection.FieldInfo? field = type.GetField(name, flags);
            if (field?.FieldType == typeof(bool))
                return (bool)field.GetValue(null)!;

            System.Reflection.PropertyInfo? property = type.GetProperty(name, flags);
            if (property?.PropertyType == typeof(bool) && property.GetMethod is not null)
                return (bool)property.GetValue(null)!;
        }
        catch { }

        return null;
    }

    private static void TrySetStaticBool(Type type, string name, bool value)
    {
        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Static;

        try
        {
            System.Reflection.FieldInfo? field = type.GetField(name, flags);
            if (field?.FieldType == typeof(bool))
            {
                field.SetValue(null, value);
                return;
            }

            System.Reflection.PropertyInfo? property = type.GetProperty(name, flags);
            if (property?.PropertyType == typeof(bool) && property.SetMethod is not null)
                property.SetValue(null, value);
        }
        catch { }
    }

    private static bool? TryGetPlayerBool(string name)
    {
        Farmer? player = Game1.player;
        if (player is null)
            return null;

        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Instance;

        try
        {
            System.Reflection.PropertyInfo? property = player.GetType().GetProperty(name, flags);
            if (property?.PropertyType == typeof(bool) && property.GetMethod is not null)
                return (bool)property.GetValue(player)!;

            System.Reflection.FieldInfo? field = player.GetType().GetField(name, flags);
            if (field?.FieldType == typeof(bool))
                return (bool)field.GetValue(player)!;
        }
        catch { }

        return null;
    }

    private static void TrySetPlayerBool(string name, bool value)
    {
        Farmer? player = Game1.player;
        if (player is null)
            return;

        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Instance;

        try
        {
            System.Reflection.PropertyInfo? property = player.GetType().GetProperty(name, flags);
            if (property?.PropertyType == typeof(bool) && property.SetMethod is not null)
            {
                property.SetValue(player, value);
                return;
            }

            System.Reflection.FieldInfo? field = player.GetType().GetField(name, flags);
            if (field?.FieldType == typeof(bool))
                field.SetValue(player, value);
        }
        catch { }
    }

    private void RequestGeneratedForHarmony(NPC npc, long requestId)
    {
        string speakerName = npc.Name;
        DialogueContext context = this.BuildContext(npc, "general", "SMAPI-Harmony");
        this.LogIdentityContext(context, speakerName);

        // Log the live save identity fields being sent to the server for profile resolution.
        if (context.SaveContext is SaveFileContextSnapshot scs)
        {
            this.Monitor.Log(
                $"[SMAPI] Sending to server: requestSource={context.RequestSource}, " +
                $"saveFileName={scs.SaveFileName ?? "(none)"}, playerName={scs.PlayerName}, farmName={scs.FarmName}. " +
                $"activePlayerProfileId=(none — server will auto-resolve).",
                LogLevel.Info);
        }
        else
        {
            this.Monitor.Log("[SMAPI] No save context built; server will use fallback defaults.", LogLevel.Warn);
        }
        int placeholderDelayMs = Math.Max(0, this.config.PlaceholderDelayMs);
        int maxGenerationWaitMs = Math.Max(1000, this.config.MaxGenerationWaitMs);
        string placeholderText = string.IsNullOrWhiteSpace(this.config.PlaceholderText) ? "..." : this.config.PlaceholderText;
        DateTime startedAt = DateTime.UtcNow;
        object stateLock = new();
        bool placeholderShown = false;
        bool placeholderConsidered = false;
        this.Monitor.Log($"[Harmony] Generation started for '{speakerName}' (request #{requestId}, placeholderDelayMs={placeholderDelayMs}, maxWaitMs={maxGenerationWaitMs}).", LogLevel.Info);

        _ = Task.Run(async () =>
        {
            Task<GeneratedDialogueResult?> generationTask = this.GenerateResultAsync(context, "SMAPI-Harmony");
            _ = Task.Run(async () =>
            {
                if (placeholderDelayMs > 0)
                    await Task.Delay(placeholderDelayMs);

                if (generationTask.IsCompleted)
                {
                    lock (stateLock)
                        placeholderConsidered = true;
                    return;
                }

                this.mainThreadActions.Enqueue(() =>
                {
                    if (!Context.IsWorldReady || requestId != this.pendingRequestId || Game1.activeClickableMenu is not null)
                    {
                        lock (stateLock)
                            placeholderConsidered = true;
                        this.Monitor.Log($"[Harmony] Placeholder skipped for '{speakerName}' (request #{requestId}); request no longer displayable.", LogLevel.Info);
                        return;
                    }

                    this.DisplayGeneratedDialogue(npc, placeholderText, replaceOpenBox: false);
                    lock (stateLock)
                    {
                        placeholderShown = true;
                        placeholderConsidered = true;
                    }
                    this.Monitor.Log($"[Harmony] Placeholder shown for '{speakerName}' (request #{requestId}) after {(DateTime.UtcNow - startedAt).TotalMilliseconds:0}ms.", LogLevel.Info);
                });
            });

            Task completed = await Task.WhenAny(generationTask, Task.Delay(maxGenerationWaitMs));
            GeneratedDialogueResult? result = null;
            if (completed == generationTask)
                result = await generationTask;
            else
                this.Monitor.Log($"[Harmony] Generation timed out for '{speakerName}' (request #{requestId}) after {maxGenerationWaitMs}ms.", LogLevel.Warn);

            double firstResponseMs = (DateTime.UtcNow - startedAt).TotalMilliseconds;
            this.LogResolvedIdentity(result, context);
            string text = ExtractText(result);
            bool usable = result is not null && string.IsNullOrWhiteSpace(result.Error) && !string.IsNullOrWhiteSpace(text);
            bool showedPlaceholder;
            bool consideredPlaceholder;
            lock (stateLock)
            {
                showedPlaceholder = placeholderShown;
                consideredPlaceholder = placeholderConsidered;
            }
            this.Monitor.Log($"[Harmony] Generation returned for '{speakerName}' (request #{requestId}, usable={usable}, placeholderShown={showedPlaceholder}, placeholderConsidered={consideredPlaceholder}, firstResponseMs={firstResponseMs:0}); applying on next update tick.", LogLevel.Info);

            this.mainThreadActions.Enqueue(() =>
            {
                if (!Context.IsWorldReady)
                {
                    this.Monitor.Log($"[Harmony] DISCARDED '{speakerName}' (#{requestId}): no longer in-game.", LogLevel.Info);
                    return;
                }
                if (requestId != this.pendingRequestId)
                {
                    this.Monitor.Log($"[Harmony] DISCARDED '{speakerName}' (#{requestId}): superseded by a newer request (#{this.pendingRequestId}).", LogLevel.Info);
                    return;
                }

                lock (stateLock)
                    showedPlaceholder = placeholderShown;
                bool boxOpen = Game1.activeClickableMenu is DialogueBox;
                NPC? current = Game1.currentSpeaker;
                if (showedPlaceholder && (!boxOpen || (current is not null && !string.Equals(current.Name, speakerName, StringComparison.OrdinalIgnoreCase))))
                {
                    this.Monitor.Log($"[Harmony] DISCARDED '{speakerName}' (#{requestId}): dialogue closed or speaker changed (boxOpen={boxOpen}, current='{current?.Name ?? "none"}').", LogLevel.Info);
                    return;
                }
                if (!showedPlaceholder && Game1.activeClickableMenu is not null)
                {
                    this.Monitor.Log($"[Harmony] DISCARDED '{speakerName}' (#{requestId}): another menu opened before generated dialogue arrived.", LogLevel.Info);
                    return;
                }

                if (!usable)
                {
                    this.Monitor.Log($"[Harmony] Generation failed/empty for '{speakerName}' (placeholderShown={showedPlaceholder}); leaving generated dialogue blank. Error='{result?.Error}'.", LogLevel.Warn);
                    if (showedPlaceholder && Game1.activeClickableMenu is DialogueBox)
                        Game1.exitActiveMenu();
                    return;
                }

                this.DisplayGeneratedDialogue(npc, text, replaceOpenBox: showedPlaceholder);
                double totalMs = (DateTime.UtcNow - startedAt).TotalMilliseconds;
                this.Monitor.Log($"[Harmony] Dialogue displayed for '{speakerName}' (request #{requestId}); placeholderShown={showedPlaceholder}, timeToFirstResponseMs={firstResponseMs:0}, totalGenerationDurationMs={totalMs:0}.", LogLevel.Info);
            });
        });
    }

    private static int CountVillagers(GameLocation? location)
    {
        if (location is null)
            return 0;
        int count = 0;
        foreach (NPC npc in location.characters)
        {
            if (npc.IsVillager)
                count++;
        }
        return count;
    }

    // ===================== Generation + display ============================================

    private void RequestAndReplace(NPC speaker)
    {
        // Capture the speaker identity with the pending request (tasks 10-12).
        string speakerName = speaker.Name;
        DialogueContext context = this.BuildContext(speaker, "general", "SMAPI-Dialogue");
        this.LogIdentityContext(context, speakerName);

        _ = Task.Run(async () =>
        {
            // Only call the server for a real, active character.
            string? rejectReason = await this.ValidateSpeakerAsync(speaker);
            if (rejectReason is not null)
            {
                this.Monitor.Log($"[MenuChanged] Speaker '{speakerName}' REJECTED: {rejectReason}. Leaving vanilla dialogue.", LogLevel.Info);
                return;
            }
            this.Monitor.Log($"[MenuChanged] Speaker '{speakerName}' ACCEPTED as active character; requesting generated dialogue.", LogLevel.Info);

            GeneratedDialogueResult? result = await this.GenerateResultAsync(context, "SMAPI-Dialogue");
            this.LogResolvedIdentity(result, context);
            string text = ExtractText(result);
            bool usable = result is not null && string.IsNullOrWhiteSpace(result.Error) && !string.IsNullOrWhiteSpace(text);
            this.Monitor.Log($"[Display] Generated dialogue QUEUED for speaker '{speakerName}' (usable={usable}); will verify speaker before applying.", LogLevel.Info);

            this.mainThreadActions.Enqueue(() =>
            {
                // Tasks 11-12: only overwrite if the SAME speaker is still talking and the box is open.
                NPC? current = Game1.currentSpeaker;
                bool boxOpen = Game1.activeClickableMenu is DialogueBox;
                if (!boxOpen || current is null || !string.Equals(current.Name, speakerName, StringComparison.OrdinalIgnoreCase))
                {
                    this.Monitor.Log($"[Display] DISCARDED generated dialogue for '{speakerName}': now speaker='{current?.Name ?? "none"}', boxOpen={boxOpen}. Leaving whatever is on screen.", LogLevel.Info);
                    return;
                }

                if (!usable)
                {
                    this.Monitor.Log($"[Display] Generation failed/empty for '{speakerName}'; leaving vanilla dialogue.", LogLevel.Warn);
                    return;
                }

                // Replace the still-open vanilla box for the same speaker with the generated line.
                this.DisplayGeneratedDialogue(speaker, text, replaceOpenBox: true);
            });
        });
    }

    /// <summary>
    /// Shows generated dialogue as a normal Stardew NPC dialogue box (stays open until dismissed)
    /// and starts a short input-suppression window so the triggering click cannot close it.
    /// </summary>
    private void DisplayGeneratedDialogue(NPC? npc, string text, bool replaceOpenBox = false)
    {
        // When replacing (e.g. placeholder -> generated), close the currently open dialogue first.
        if (replaceOpenBox && Game1.activeClickableMenu is DialogueBox)
            Game1.exitActiveMenu();

        bool shown = false;
        try
        {
            if (npc is not null)
            {
                // SDV 1.6 has no Game1.drawDialogue(npc, text) overload. Build a Dialogue, make it
                // the NPC's current dialogue, then draw it as a normal portrait box that stays open.
                StardewValley.Dialogue dialogue = new(npc, null, text);
                npc.CurrentDialogue.Clear();
                npc.CurrentDialogue.Push(dialogue);
                Game1.drawDialogue(npc);
                shown = true;
            }
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"[Display] NPC dialogue display failed ({ex.Message}); using a plain dialogue box.", LogLevel.Warn);
        }

        if (!shown)
            Game1.drawDialogueNoTyping(text);

        // Re-arm the debounce so our own replacement box (same speaker) does not re-trigger
        // generation, even if generation took longer than the debounce window.
        if (npc is not null)
            this.MarkHandled(npc.Name);

        // Guard the new box from the original/lingering action button.
        this.suppressActionUntil = DateTime.UtcNow.AddMilliseconds(PostDisplaySuppressionMs);
        this.suppressionWindowActive = true;
        this.Monitor.Log($"[Display] Generated dialogue DISPLAYED for '{npc?.Name ?? "(no npc)"}': {Preview(text)}", LogLevel.Info);
        this.Monitor.Log($"[Input suppression] Started for {PostDisplaySuppressionMs:0}ms after display.", LogLevel.Info);
    }

    private DialogueContext BuildContext(NPC npc, string topic, string requestSource = "SMAPI-Harmony")
    {
        string characterName = npc.Name;
        SaveFileContextSnapshot saveContext = this.BuildSaveContextSnapshot(characterName);
        return new DialogueContext
        {
            CharacterName = characterName,
            DisplayName = npc.displayName ?? characterName,
            InterceptedNpcName = characterName,
            Topic = string.IsNullOrWhiteSpace(topic) ? "general" : topic,
            Season = saveContext.Season,
            Weather = saveContext.Weather,
            InternalLocationId = saveContext.Location,
            Location = saveContext.Location,
            FriendshipLevel = saveContext.FriendshipHearts,
            SaveContext = saveContext,
            RequestSource = requestSource
        };
    }

    private DialogueContext BuildContext(string characterName, string topic, string requestSource = "SMAPI-Command")
    {
        SaveFileContextSnapshot saveContext = this.BuildSaveContextSnapshot(characterName);
        return new DialogueContext
        {
            CharacterName = characterName,
            DisplayName = characterName,
            InterceptedNpcName = characterName,
            Topic = string.IsNullOrWhiteSpace(topic) ? "general" : topic,
            Season = saveContext.Season,
            Weather = saveContext.Weather,
            InternalLocationId = saveContext.Location,
            Location = saveContext.Location,
            FriendshipLevel = saveContext.FriendshipHearts,
            SaveContext = saveContext,
            RequestSource = requestSource
        };
    }

    /// <summary>
    /// Reads the current live Stardew Valley game state and returns a complete save context snapshot.
    /// NPC-specific friendship/relationship fields are populated when <paramref name="npcName"/> is provided.
    /// Returns a minimal fallback snapshot when the world is not ready or Game1.player is null.
    /// </summary>
    private SaveFileContextSnapshot BuildSaveContextSnapshot(string? npcName)
    {
        if (!Context.IsWorldReady || Game1.player is null)
        {
            this.Monitor.Log("[SaveContext] World not ready or player is null; using fallback save context.", LogLevel.Warn);
            return new SaveFileContextSnapshot();
        }

        if (this.config.DebugLogging)
            this.Monitor.Log($"[SaveContext] Building save context for NPC: {npcName ?? "(none)"}", LogLevel.Trace);

        string playerName = SafeGet(() => Game1.player.Name, "Unknown");
        string farmName = SafeGet(() => Game1.player.farmName.Value, "Unknown");
        string? spouseName = null;
        try
        {
            string? raw = Game1.player.spouse;
            spouseName = string.IsNullOrWhiteSpace(raw) ? null : raw;
        }
        catch { }
        string saveFileName = SafeGet(() => Constants.SaveFolderName ?? string.Empty, string.Empty);
        string? saveFilePath = null;
        if (!string.IsNullOrWhiteSpace(saveFileName))
        {
            try { saveFilePath = Path.Combine(Constants.SavesPath, saveFileName); }
            catch { }
        }
        string season = SafeGet(() => Game1.currentSeason, "spring");
        int day = SafeGetValue(() => Game1.Date.DayOfMonth, 0);
        int year = SafeGetValue(() => Game1.Date.Year, 0);
        string weather = SafeGet(GetWeather, "clear");
        string location = SafeGet(() => Game1.currentLocation?.NameOrUniqueName ?? "Unknown", "Unknown");
        IReadOnlyList<string> seenEvents = SafeGetList(() => Game1.player.eventsSeen.Select(id => id.ToString()).ToList());
        IReadOnlyList<string> completedQuests = SafeGetList(GetCompletedQuestNames);
        string communityState = GetCommunityState();
        string? festivalOrSpecialDay = GetFestivalOrSpecialDay();

        // NPC-specific friendship/relationship fields.
        int friendshipHearts = 0;
        bool hasMetNpc = false;
        bool isDating = false;

        if (!string.IsNullOrWhiteSpace(npcName))
        {
            try
            {
                if (Game1.player.friendshipData.TryGetValue(npcName, out Friendship? friendship) && friendship is not null)
                {
                    friendshipHearts = Math.Clamp(friendship.Points / 250, 0, 14);
                    isDating = friendship.Status == FriendshipStatus.Dating;
                    hasMetNpc = friendship.Points > 0 || friendship.TalkedToToday;
                }
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"[SaveContext] Error reading friendship for '{npcName}': {ex.Message}", LogLevel.Trace);
            }
        }

        bool isSpouse = !string.IsNullOrWhiteSpace(spouseName)
            && string.Equals(spouseName, npcName, StringComparison.OrdinalIgnoreCase);

        string datingStatus = !string.IsNullOrWhiteSpace(spouseName) ? "Married"
            : isDating ? "Dating"
            : "Single";

        string relationshipState = isSpouse ? "Spouse"
            : isDating ? "Dating"
            : hasMetNpc && friendshipHearts >= 2 ? "Friend"
            : hasMetNpc ? "Acquaintance"
            : !string.IsNullOrWhiteSpace(npcName) ? "Unmet"
            : "Unknown";

        this.Monitor.Log(
            $"[SaveContext] Built: player={playerName}, farm={farmName}, npc={npcName ?? "(none)"}, " +
            $"hearts={friendshipHearts}, relation={relationshipState}, location={location}, " +
            $"season={season}, day={day}, year={year}",
            LogLevel.Info);

        return new SaveFileContextSnapshot
        {
            SaveFileName = string.IsNullOrWhiteSpace(saveFileName) ? null : saveFileName,
            SaveFilePath = saveFilePath,
            PlayerName = playerName,
            FarmName = farmName,
            Spouse = spouseName,
            DatingStatus = datingStatus,
            FriendshipHearts = friendshipHearts,
            SeenEvents = seenEvents,
            CompletedQuests = completedQuests,
            CommunityState = communityState,
            Season = season,
            Day = day,
            Year = year,
            Weather = weather,
            Location = location,
            FestivalOrSpecialDay = festivalOrSpecialDay,
            HasMetNpc = hasMetNpc,
            RelationshipState = relationshipState,
            CustomUserLoreRelationshipState = ""
        };
    }

    private void LogIdentityContext(DialogueContext context, string interceptedNpc)
    {
        this.Monitor.Log($"[LivingLore] Intercepted NPC: {interceptedNpc}", LogLevel.Info);
        this.Monitor.Log($"[LivingLore] CharacterName: {context.CharacterName}", LogLevel.Info);
        this.Monitor.Log($"[LivingLore] LocationName: {context.Location}", LogLevel.Info);
        if (!string.Equals(context.CharacterName, interceptedNpc, StringComparison.OrdinalIgnoreCase))
        {
            this.Monitor.Log("[LivingLore] WARNING: Character/location mismatch detected.", LogLevel.Warn);
            this.Monitor.Log($"CharacterName={context.CharacterName}", LogLevel.Warn);
            this.Monitor.Log($"LocationName={context.Location}", LogLevel.Warn);
        }
    }

    private void LogResolvedIdentity(GeneratedDialogueResult? result, DialogueContext context)
    {
        string resolved = result?.ResolvedCharacterName ?? context.ResolvedCharacterName;
        this.Monitor.Log($"[LivingLore] Resolved Character: {resolved}", LogLevel.Info);
        if (IsIdentityError(result?.Error))
        {
            this.Monitor.Log("[LivingLore] WARNING: Character/location mismatch detected.", LogLevel.Warn);
            this.Monitor.Log($"CharacterName={context.CharacterName}", LogLevel.Warn);
            this.Monitor.Log($"LocationName={context.Location}", LogLevel.Warn);
        }
    }

    private static bool IsIdentityError(string? error)
    {
        return !string.IsNullOrWhiteSpace(error)
            && (error.Contains("Character/location mismatch", StringComparison.OrdinalIgnoreCase)
                || error.Contains("known location/building/map", StringComparison.OrdinalIgnoreCase)
                || error.Contains("characterName is null or empty", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Generates dialogue via the local server (preferred) or the in-process manager. Never throws.</summary>
    private async Task<GeneratedDialogueResult?> GenerateResultAsync(DialogueContext context, string source)
    {
        if (this.localDialogueApiClient is not null)
            return await this.localDialogueApiClient.GenerateAsync(context, source);

        if (this.dialogueManager is not null)
        {
            try
            {
                GeneratedDialogue dialogue = await this.dialogueManager.GenerateAsync(context);
                return new GeneratedDialogueResult { Dialogue = dialogue, ReturnedDialogue = dialogue.Dialogue };
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"[Server request] In-process generation failed: {ex.Message}", LogLevel.Warn);
                return null;
            }
        }

        this.Monitor.Log("[Server request] No dialogue source is available (neither local API client nor in-process manager initialized).", LogLevel.Warn);
        return null;
    }

    private static string ExtractText(GeneratedDialogueResult? result)
    {
        if (result is null)
            return "";
        if (!string.IsNullOrWhiteSpace(result.ReturnedDialogue))
            return result.ReturnedDialogue;
        return result.Dialogue?.Dialogue ?? "";
    }

    // ===================== Console commands ================================================

    private async void HandleDialogueCommand(string command, string[] args)
    {
        if (args.Length < 1)
        {
            this.Monitor.Log("Usage: livinglore_dialogue <npcName> [topic]", LogLevel.Info);
            return;
        }

        string characterName = args[0];
        string topic = args.Length > 1 ? string.Join(' ', args.Skip(1)) : "general";
        GeneratedDialogueResult? result = await this.GenerateResultAsync(this.BuildContext(characterName, topic, "SMAPI-Command"), "SMAPI-Command");
        string text = ExtractText(result);
        this.Monitor.Log(result is null ? "No response from dialogue source." : $"Generated for '{characterName}': {text}", LogLevel.Info);
        if (!string.IsNullOrWhiteSpace(text))
            this.mainThreadActions.Enqueue(() => this.DisplayGeneratedDialogue(FindNpc(characterName), text));
    }

    private async void HandleTestDialogueCommand(string command, string[] args)
    {
        if (args.Length < 1)
        {
            this.Monitor.Log("Usage: livinglore_testdialogue <npcName>", LogLevel.Info);
            return;
        }

        string characterName = args[0];
        this.Monitor.Log($"[Command] livinglore_testdialogue '{characterName}' -> calling server as RequestSource=SMAPI...", LogLevel.Info);
        GeneratedDialogueResult? result = await this.GenerateResultAsync(this.BuildContext(characterName, "general", "SMAPI"), "SMAPI");
        if (result is null)
        {
            this.Monitor.Log("[Command] No result returned (see [Server request]/[Server response] logs above).", LogLevel.Warn);
            return;
        }
        this.Monitor.Log($"[Command] Returned text: {ExtractText(result)}", LogLevel.Info);
        if (!string.IsNullOrWhiteSpace(result.Error))
            this.Monitor.Log($"[Command] Server error: {result.Error}", LogLevel.Warn);
    }

    private async void HandleSayCommand(string command, string[] args)
    {
        if (args.Length < 1)
        {
            this.Monitor.Log("Usage: livinglore_say <npcName>", LogLevel.Info);
            return;
        }

        if (!Context.IsWorldReady)
        {
            this.Monitor.Log("[Command] livinglore_say requires a loaded save (be in-game).", LogLevel.Warn);
            return;
        }

        string characterName = args[0];
        this.Monitor.Log($"[Command] livinglore_say '{characterName}' -> generating and displaying...", LogLevel.Info);
        GeneratedDialogueResult? result = await this.GenerateResultAsync(this.BuildContext(characterName, "general", "SMAPI-Say"), "SMAPI-Say");
        string text = ExtractText(result);
        if (string.IsNullOrWhiteSpace(text))
        {
            this.Monitor.Log("[Command] Nothing to display (generation failed or empty).", LogLevel.Warn);
            return;
        }
        this.mainThreadActions.Enqueue(() => this.DisplayGeneratedDialogue(FindNpc(characterName), text));
    }

    private static NPC? FindNpc(string name)
    {
        try
        {
            return Context.IsWorldReady ? Game1.getCharacterFromName(name) : null;
        }
        catch
        {
            return null;
        }
    }

    private static string GetCurrentDialogueText()
    {
        try
        {
            if (Game1.activeClickableMenu is DialogueBox box)
            {
                // Called reflectively so this compiles regardless of exact 1.6 method availability.
                System.Reflection.MethodInfo? method = typeof(DialogueBox).GetMethod("getCurrentString", Type.EmptyTypes);
                if (method is not null)
                    return method.Invoke(box, null) as string ?? "";
            }
        }
        catch
        {
            // Non-fatal: this is only used for diagnostic logging.
        }
        return "";
    }

    private static string NormalizeDialogueForCompletion(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        return new string(text
            .Where(c => !char.IsControl(c) && !char.IsWhiteSpace(c))
            .ToArray());
    }

    private bool RecentlyHandled(string npcName)
    {
        return string.Equals(this.lastHandledNpc, npcName, StringComparison.OrdinalIgnoreCase)
            && (DateTime.UtcNow - this.lastHandledAt).TotalSeconds < 2.5;
    }

    private void MarkHandled(string npcName)
    {
        this.lastHandledNpc = npcName;
        this.lastHandledAt = DateTime.UtcNow;
    }

    private static string SafeGet(Func<string?> getter, string fallback)
    {
        try
        {
            string? value = getter();
            return string.IsNullOrEmpty(value) ? fallback : value;
        }
        catch
        {
            return fallback;
        }
    }

    private static string Preview(string text, int max = 160)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        text = text.Replace("\r", " ").Replace("\n", " ");
        return text.Length <= max ? text : text[..max] + "...";
    }

    private static string GetWeather()
    {
        if (Game1.isLightning)
            return "storm";
        if (Game1.isRaining)
            return "rain";
        if (Game1.isSnowing)
            return "snow";

        return "clear";
    }

    private static int GetFriendshipLevel(string npcName)
    {
        try
        {
            return Game1.player?.getFriendshipHeartLevelForNPC(npcName) ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    private static T SafeGetValue<T>(Func<T> getter, T fallback)
    {
        try { return getter(); }
        catch { return fallback; }
    }

    private static IReadOnlyList<string> SafeGetList(Func<List<string>> getter)
    {
        try { return getter() ?? new List<string>(); }
        catch { return Array.Empty<string>(); }
    }

    private static List<string> GetCompletedQuestNames()
    {
        // Quest completion names are informational context only.
        // SDV 1.6 uses a NetBool field named "completed" rather than a simple bool property;
        // we skip the per-quest check to avoid version-specific API surface issues.
        return new List<string>();
    }

    private static string GetCommunityState()
    {
        try
        {
            if (Game1.MasterPlayer.mailReceived.Contains("JojaMember"))
                return "JojaMember";
            if (Game1.MasterPlayer.mailReceived.Contains("cc_Complete"))
                return "Complete";
            return "InProgress";
        }
        catch
        {
            return "Unknown";
        }
    }

    private static string? GetFestivalOrSpecialDay()
    {
        try
        {
            return Utility.isFestivalDay(Game1.Date.DayOfMonth, Game1.Date.Season) ? "Festival" : null;
        }
        catch
        {
            return null;
        }
    }

    private string GetConfiguredModsFolderPath()
    {
        if (!string.IsNullOrWhiteSpace(this.config.ModsFolderPath))
            return this.config.ModsFolderPath;

        DirectoryInfo? modDirectory = Directory.GetParent(this.Helper.DirectoryPath);
        return modDirectory?.FullName ?? this.Helper.DirectoryPath;
    }

    private string GetConfiguredGamePath()
    {
        if (!string.IsNullOrWhiteSpace(this.config.GamePath))
            return this.config.GamePath;

        string modsFolderPath = this.GetConfiguredModsFolderPath();
        DirectoryInfo? gameDirectory = Directory.GetParent(modsFolderPath);
        return gameDirectory?.FullName ?? modsFolderPath;
    }
}

internal sealed record FriendshipSnapshot(string FriendshipMilestone, string RelationshipMilestone);

/// <summary>
/// Harmony prefix on <see cref="NPC.checkAction"/>. Lets the mod intercept eligible NPC dialogue
/// before vanilla opens it. All decision logic lives in <see cref="ModEntry.TryInterceptNpcDialogue"/>.
/// </summary>
internal static class NpcCheckActionPatch
{
    public static ModEntry? Mod;

    // Returns false to skip the original NPC.checkAction, true to let it run.
    public static bool Prefix(NPC __instance, Farmer who, ref bool __result)
    {
        ModEntry? mod = Mod;
        if (mod is null)
            return true;

        try
        {
            return mod.TryInterceptNpcDialogue(__instance, who, ref __result);
        }
        catch
        {
            // Never break vanilla interaction if interception throws.
            return true;
        }
    }
}

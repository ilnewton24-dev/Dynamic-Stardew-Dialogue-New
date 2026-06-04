using System.Collections.Concurrent;
using System.Text.Json;
using LivingLoreDialogue.Data;
using LivingLoreDialogue.Models;
using LivingLoreDialogue.Repositories;
using LivingLoreDialogue.Services;
using Microsoft.Xna.Framework;
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

    public override void Entry(IModHelper helper)
    {
        this.config = helper.ReadConfig<ModConfig>();

        // ---- Loud startup diagnostics ------------------------------------------------------
        this.Monitor.Log("==================== LIVING LORE DIALOGUE ====================", LogLevel.Info);
        this.Monitor.Log("Living Lore Dialogue mod LOADED.", LogLevel.Info);
        this.Monitor.Log("Config loaded:", LogLevel.Info);
        this.Monitor.Log($"  EnableLiveInGameDialogueGeneration = {this.config.EnableLiveInGameDialogueGeneration}", LogLevel.Info);
        this.Monitor.Log($"  OverrideNpcDialogue (suppress vanilla, show generated) = {this.config.OverrideNpcDialogue}", LogLevel.Info);
        this.Monitor.Log($"  UseLocalWebApiForDialogue = {this.config.UseLocalWebApiForDialogue}", LogLevel.Info);
        this.Monitor.Log($"  Server URL = {this.config.LocalWebApiBaseUrl}", LogLevel.Info);
        this.Monitor.Log($"  DebugLogging = {this.config.DebugLogging}", LogLevel.Info);
        this.Monitor.Log("Harmony patches applied: NONE (this mod uses SMAPI events, not Harmony).", LogLevel.Info);

        // ---- Event subscriptions -----------------------------------------------------------
        helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;
        helper.Events.GameLoop.SaveLoaded += this.OnSaveLoaded;
        helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
        helper.Events.Input.ButtonPressed += this.OnButtonPressed;
        helper.Events.Display.MenuChanged += this.OnMenuChanged;
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
            RelationshipRepository relationshipRepository = new(connectionFactory);
            EventRepository eventRepository = new(connectionFactory);
            MemoryRepository memoryRepository = new(connectionFactory);
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
                    new ModScannerService(),
                    new VanillaCharacterScannerService(),
                    new CharacterValidationService(),
                    characterValidationRepository,
                    canonicalCharacterRepository,
                    characterSyncService,
                    scannedModRepository,
                    loreConflictRepository,
                    scanHistoryRepository,
                    new DialogueSourceScannerService(canonicalCharacterRepository, dialogueSourceRepository),
                    message => this.Monitor.Log(message, LogLevel.Info));

                _ = Task.Run(async () =>
                {
                    ModScanSummary summary = await scanCoordinator.RunScanAsync("SMAPI Startup");
                    this.Monitor.Log(
                        $"Living Lore scan complete: success={summary.Success}, mods={summary.ModsScanned}, vanilla={summary.VanillaCharactersFound}, modded={summary.ModdedCharactersFound}, canonical={summary.MergedCanonicalCharacters}, found={summary.CharactersFound}, added={summary.CharactersAdded}, updated={summary.CharactersUpdated}, reactivated={summary.CharactersReactivated}, inactive={summary.CharactersMarkedInactive}, conflicts={summary.ConflictsFound}.",
                        summary.Success ? LogLevel.Info : LogLevel.Warn);

                    foreach (string error in summary.Errors)
                        this.Monitor.Log($"Living Lore scan warning: {error}", LogLevel.Warn);
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
        this.Monitor.Log($"[Event] SaveLoaded. Player='{Game1.player?.Name}', location='{Game1.currentLocation?.NameOrUniqueName}'. Living Lore interaction detection is active.", LogLevel.Info);
    }

    private void OnWarped(object? sender, WarpedEventArgs e)
    {
        if (this.config.DebugLogging)
            this.Monitor.Log($"[Event] Player warped to '{e.NewLocation?.NameOrUniqueName}' with {CountVillagers(e.NewLocation)} villager(s).", LogLevel.Trace);
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
    }

    // ===================== Interaction detection (button press) =============================

    // The action button no longer triggers generation. We only use it to swallow the click that
    // would otherwise dismiss a freshly displayed generated dialogue box.
    private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
        try
        {
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

            // Debounce: also stops our own replacement box (same speaker) from re-triggering.
            if (this.RecentlyHandled(speaker.Name))
            {
                if (this.config.DebugLogging)
                    this.Monitor.Log($"[MenuChanged] '{speaker.Name}' recently handled; skipping.", LogLevel.Trace);
                return;
            }
            this.MarkHandled(speaker.Name);

            this.RequestAndReplace(speaker);
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
        DialogueContext context = this.BuildContext(speakerName, "general");

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
                Game1.exitActiveMenu();
                this.DisplayGeneratedDialogue(speaker, text);
            });
        });
    }

    /// <summary>
    /// Shows generated dialogue as a normal Stardew NPC dialogue box (stays open until dismissed)
    /// and starts a short input-suppression window so the triggering click cannot close it.
    /// </summary>
    private void DisplayGeneratedDialogue(NPC? npc, string text)
    {
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

    private DialogueContext BuildContext(string characterName, string topic)
    {
        return new DialogueContext
        {
            CharacterName = characterName,
            Topic = string.IsNullOrWhiteSpace(topic) ? "general" : topic,
            Season = SafeGet(() => Game1.currentSeason, "spring"),
            Weather = SafeGet(GetWeather, "clear"),
            Location = SafeGet(() => Game1.currentLocation?.NameOrUniqueName, "Unknown"),
            FriendshipLevel = GetFriendshipLevel(characterName)
        };
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
        GeneratedDialogueResult? result = await this.GenerateResultAsync(this.BuildContext(characterName, topic), "SMAPI-Command");
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
        GeneratedDialogueResult? result = await this.GenerateResultAsync(this.BuildContext(characterName, "general"), "SMAPI");
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
        GeneratedDialogueResult? result = await this.GenerateResultAsync(this.BuildContext(characterName, "general"), "SMAPI-Say");
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

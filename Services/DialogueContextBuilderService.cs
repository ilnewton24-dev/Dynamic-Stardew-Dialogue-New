using LivingLoreDialogue.Models;
using LivingLoreDialogue.Repositories;
using System.Collections.Concurrent;

namespace LivingLoreDialogue.Services;

public sealed class DialogueContextBuilderService
{
    private static readonly TimeSpan ReusableContextCacheDuration = TimeSpan.FromSeconds(5);

    private readonly CharacterRepository characterRepository;
    private readonly CanonicalCharacterRepository canonicalRepository;
    private readonly DialogueSourceRepository dialogueSourceRepository;
    private readonly RelationshipRepository relationshipRepository;
    private readonly EventRepository eventRepository;
    private readonly MemoryRepository memoryRepository;
    private readonly VoiceRuleRepository voiceRuleRepository;
    private readonly UserLoreOverrideRepository userLoreOverrideRepository;
    private readonly LoreChangeLogRepository loreChangeLogRepository;
    private readonly GeneratedDialogueHistoryRepository dialogueHistoryRepository;
    private readonly SaveFileContextService saveFileContextService;
    private readonly DialogueContextSelectionService contextSelectionService;
    private readonly PlayerProfileRepository playerProfileRepository;
    private readonly int maxRecentMemories;
    private readonly string? modsFolderPathFilter;
    private readonly ConcurrentDictionary<string, CachedContextPacket> contextCache = new();

    public DialogueContextBuilderService(
        CharacterRepository characterRepository,
        CanonicalCharacterRepository canonicalRepository,
        DialogueSourceRepository dialogueSourceRepository,
        RelationshipRepository relationshipRepository,
        EventRepository eventRepository,
        MemoryRepository memoryRepository,
        VoiceRuleRepository voiceRuleRepository,
        UserLoreOverrideRepository userLoreOverrideRepository,
        LoreChangeLogRepository loreChangeLogRepository,
        GeneratedDialogueHistoryRepository dialogueHistoryRepository,
        SaveFileContextService saveFileContextService,
        DialogueContextSelectionService contextSelectionService,
        PlayerProfileRepository playerProfileRepository,
        int maxRecentMemories,
        string? modsFolderPathFilter = null)
    {
        this.characterRepository = characterRepository;
        this.canonicalRepository = canonicalRepository;
        this.dialogueSourceRepository = dialogueSourceRepository;
        this.relationshipRepository = relationshipRepository;
        this.eventRepository = eventRepository;
        this.memoryRepository = memoryRepository;
        this.voiceRuleRepository = voiceRuleRepository;
        this.userLoreOverrideRepository = userLoreOverrideRepository;
        this.loreChangeLogRepository = loreChangeLogRepository;
        this.dialogueHistoryRepository = dialogueHistoryRepository;
        this.saveFileContextService = saveFileContextService;
        this.contextSelectionService = contextSelectionService;
        this.playerProfileRepository = playerProfileRepository;
        this.maxRecentMemories = maxRecentMemories;
        this.modsFolderPathFilter = string.IsNullOrWhiteSpace(modsFolderPathFilter)
            ? null
            : Path.GetFullPath(modsFolderPathFilter);
    }

    public async Task<DialogueContextPacket> BuildAsync(DialogueContext context, string? relationshipContext, SaveFileContextSnapshot? saveContextOverride = null, long? playerProfileId = null)
    {
        // Simulation/test mode can inject a save context (a saved scenario); otherwise build the live one.
        SaveFileContextSnapshot saveContext = saveContextOverride
            ?? await this.saveFileContextService.GetSnapshotAsync(context, relationshipContext);

        string cacheKey = BuildCacheKey(context, relationshipContext, saveContext, playerProfileId);
        if (this.contextCache.TryGetValue(cacheKey, out CachedContextPacket? cached) && cached.ExpiresAtUtc > DateTime.UtcNow)
            return cached.Packet;

        CanonicalCharacter? canonical = await this.canonicalRepository.GetByNameOrAliasAsync(context.CharacterName);
        Character? directCharacter = await this.characterRepository.GetByNameAsync(canonical?.CanonicalName ?? context.CharacterName)
            ?? await this.characterRepository.GetByNameAsync(context.CharacterName);

        if (canonical is null && directCharacter?.CanonicalCharacterId is not null)
            canonical = (await this.canonicalRepository.GetAllAsync()).FirstOrDefault(item => item.Id == directCharacter.CanonicalCharacterId);

        long? canonicalId = canonical?.Id ?? directCharacter?.CanonicalCharacterId;

        // Load all character sources (each carries an IsActive flag from ScannedMods JOIN).
        // Split into active-only (used in prompt / voice profile) and the full set (used in trace).
        IReadOnlyList<CharacterSource> characterSourcesAll = canonicalId is null
            ? Array.Empty<CharacterSource>()
            : await this.canonicalRepository.GetSourcesAsync(canonicalId.Value);

        IReadOnlyList<CharacterSource> characterSources = characterSourcesAll
            .Where(s => s.IsActive)
            .ToArray();

        // Log filtering results so it is visible in server output for diagnostics.
        int excludedCount = characterSourcesAll.Count - characterSources.Count;
        if (excludedCount > 0)
        {
            IEnumerable<string> excludedExamples = characterSourcesAll
                .Where(s => !s.IsActive)
                .Select(s => s.SourceModId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(5);
            System.Diagnostics.Debug.WriteLine(
                $"[CharacterSources] '{context.CharacterName}': {characterSources.Count} active source(s) included, " +
                $"{excludedCount} inactive historical source(s) excluded. " +
                $"Excluded mods (up to 5): {string.Join(", ", excludedExamples)}.");
        }
        else
        {
            System.Diagnostics.Debug.WriteLine(
                $"[CharacterSources] '{context.CharacterName}': {characterSources.Count} active source(s) included, none excluded.");
        }

        IReadOnlyList<Character> canonicalInstances = canonicalId is null
            ? directCharacter is null ? Array.Empty<Character>() : new[] { directCharacter }
            : await this.characterRepository.GetForCanonicalAsync(canonicalId.Value);

        Character character = ChooseProfileCharacter(canonicalInstances, characterSources)
            ?? directCharacter
            ?? throw new InvalidOperationException($"No lore profile exists for character '{context.CharacterName}'.");

        IReadOnlyList<DialogueSource> dialogueSources = canonicalId is null
            ? Array.Empty<DialogueSource>()
            : await this.dialogueSourceRepository.GetForCanonicalAsync(canonicalId.Value, activeOnly: true, limit: 80);

        System.Diagnostics.Debug.WriteLine(
            $"[DialogueSources] '{context.CharacterName}': {dialogueSources.Count} active source(s) loaded " +
            $"(canonicalId={canonicalId?.ToString() ?? "null"}, activeOnly=true, limit=80).");

        // Secondary path-prefix guard: exclude sources from archived/alternate mods folders even
        // when their IsActive flag hasn't been updated yet (e.g. the user changed the mods folder
        // path between scans). Vanilla sources (SourceModId == null) and StardewValley.* sources
        // (SourceRootPath == null) are always kept.
        if (this.modsFolderPathFilter is not null && dialogueSources.Count > 0)
        {
            int before = dialogueSources.Count;
            dialogueSources = dialogueSources
                .Where(s => s.SourceModId is null                             // vanilla — always keep
                         || s.SourceRootPath is null                          // vanilla/no-path rows — keep (fail open)
                         || s.SourceRootPath.Equals(this.modsFolderPathFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();
            int excluded = before - dialogueSources.Count;
            if (excluded > 0)
                System.Diagnostics.Debug.WriteLine(
                    $"[DialogueSources] '{context.CharacterName}': excluded {excluded} source(s) by SourceRootPath filter. " +
                    $"Remaining: {dialogueSources.Count}.");
        }

        if (dialogueSources.Count == 0)
            System.Diagnostics.Debug.WriteLine(
                $"[DialogueSources] '{context.CharacterName}': zero active sources after filtering. " +
                $"Prompt will have no dialogue examples. " +
                $"Run in-game to trigger vanilla dialogue registration via SMAPI, or re-scan with active Content Patcher mods.");

        DialogueSourceSummary? summary = canonicalId is null
            ? null
            : await this.dialogueSourceRepository.GetSummaryAsync(canonicalId.Value);

        IReadOnlyList<GeneratedDialogueHistoryEntry> recentGeneratedDialogue =
            await this.dialogueHistoryRepository.GetForCharacterIdsAsync(canonicalInstances.Select(instance => instance.Id), 16);

        // Resolve the player profile by priority. Missing profiles never fail generation.
        PlayerProfileResolution playerProfileResolution = await this.ResolvePlayerProfileAsync(playerProfileId, saveContext, context.RequestSource);
        PlayerProfile? playerProfile = playerProfileResolution.Profile;
        IReadOnlyList<PlayerProfileRelationship> playerRelationships = Array.Empty<PlayerProfileRelationship>();
        IReadOnlyList<PlayerProfileMemory> playerMemories = Array.Empty<PlayerProfileMemory>();
        if (playerProfile is not null)
        {
            playerRelationships = canonicalId is long relCanonicalId
                ? await this.playerProfileRepository.GetRelationshipsAsync(playerProfile.Id, relCanonicalId)
                : Array.Empty<PlayerProfileRelationship>();
            playerMemories = await this.playerProfileRepository.GetMemoriesAsync(playerProfile.Id, canonicalId, includeGeneral: true);
        }
        DialogueLoreBundle preliminaryLore = new()
        {
            Character = character,
            CanonicalCharacter = canonical,
            CharacterInstances = canonicalInstances,
            CharacterSources = characterSources,
            DialogueSources = dialogueSources,
            DialogueSummary = summary,
            SaveContext = saveContext,
            Events = await this.eventRepository.GetRecentAsync(),
            RecentGeneratedDialogue = recentGeneratedDialogue
        };

        IReadOnlyList<ScoredDialogueSource> relevantDialogueSources =
            this.contextSelectionService.SelectRelevantDialogueSources(context, preliminaryLore, limit: 10);
        CharacterVoiceProfile voiceProfile =
            this.contextSelectionService.BuildVoiceProfile(dialogueSources, summary);

        IReadOnlyList<Memory> saveScopedMemories = await this.memoryRepository.GetRelevantForGenerationAsync(
            saveContext.SaveFileName,
            canonicalInstances.Select(instance => instance.Id),
            context.CharacterName,
            playerProfile?.Id,
            Math.Min(this.maxRecentMemories, 3));

        DialogueLoreBundle lore = new()
        {
            Character = character,
            CanonicalCharacter = canonical,
            CharacterInstances = canonicalInstances,
            CharacterSources = characterSources,          // active-only → used in prompt
            CharacterSourcesAll = characterSourcesAll,    // all incl. inactive → used in trace
            Relationships = await GetMergedAsync(canonicalInstances, item => this.relationshipRepository.GetForCharacterAsync(item.Id)),
            Events = preliminaryLore.Events,
            Memories = saveScopedMemories,
            VoiceRules = await GetMergedAsync(canonicalInstances, item => this.voiceRuleRepository.GetForCharacterAsync(item.Id)),
            UserOverrides = await GetMergedAsync(canonicalInstances, item => this.userLoreOverrideRepository.GetForCharacterAsync(item.Id)),
            RecentChanges = (await GetMergedAsync(canonicalInstances, item => this.loreChangeLogRepository.GetRecentForCharacterAsync(item.Id, 8)))
                .OrderByDescending(change => change.Timestamp)
                .Take(8)
                .ToArray(),
            DialogueSources = dialogueSources,
            RelevantDialogueSources = relevantDialogueSources,
            DialogueSummary = summary,
            VoiceProfile = voiceProfile,
            SaveContext = saveContext,
            RecentGeneratedDialogue = recentGeneratedDialogue,
            PlayerProfile = playerProfile,
            PlayerRelationships = playerRelationships,
            PlayerMemories = playerMemories,
            PlayerProfileSaveLink = playerProfileResolution.SaveLink,
            PlayerProfileMatchMethod = playerProfileResolution.MatchMethod
        };

        DialogueContextPacket packet = new()
        {
            Scene = context,
            Lore = lore,
            DialogueSources = relevantDialogueSources,
            DialogueSummary = summary,
            SaveContext = lore.SaveContext
        };
        this.contextCache[cacheKey] = new CachedContextPacket(packet, DateTime.UtcNow.Add(ReusableContextCacheDuration));
        return packet;
    }

    private static string BuildCacheKey(DialogueContext context, string? relationshipContext, SaveFileContextSnapshot saveContext, long? playerProfileId)
    {
        return string.Join('|',
            context.CharacterName.Trim().ToLowerInvariant(),
            context.Topic.Trim().ToLowerInvariant(),
            context.RequestSource.Trim().ToLowerInvariant(),
            relationshipContext ?? "",
            playerProfileId?.ToString() ?? "",
            saveContext.SaveFileName ?? "",
            saveContext.PlayerName,
            saveContext.FarmName,
            saveContext.Season,
            saveContext.Weather,
            saveContext.Location,
            saveContext.FriendshipHearts,
            saveContext.RelationshipState);
    }

    private async Task<PlayerProfileResolution> ResolvePlayerProfileAsync(long? playerProfileId, SaveFileContextSnapshot saveContext, string? requestSource)
    {
        // 1. Explicit id from request (dashboard dropdown or direct hint from SMAPI).
        if (playerProfileId is long id)
        {
            PlayerProfile? explicitProfile = await this.playerProfileRepository.GetByIdAsync(id);
            System.Diagnostics.Debug.WriteLine($"[ProfileResolution] Step 1 explicit id={id}: {(explicitProfile is null ? "not found" : $"'{explicitProfile.ProfileName}'")}");
            if (explicitProfile is not null)
                return new PlayerProfileResolution(explicitProfile, null, "explicit selection");
            System.Diagnostics.Debug.WriteLine("[ProfileResolution] Explicit id not found; continuing.");
        }

        // 2. Save-file link mapped in the dashboard.
        if (!string.IsNullOrWhiteSpace(saveContext.SaveFileName))
        {
            PlayerProfile? linked = await this.playerProfileRepository.GetBySaveFileAsync(saveContext.SaveFileName!);
            System.Diagnostics.Debug.WriteLine($"[ProfileResolution] Step 2 save link={saveContext.SaveFileName}: {(linked is null ? "not found" : $"'{linked.ProfileName}'")}");
            if (linked is not null)
                return new PlayerProfileResolution(linked, saveContext.SaveFileName, "save mapping");
        }

        // 3. Match active profile by playerName + farmName (primary auto-resolution path for SMAPI).
        PlayerProfile? matched = await this.playerProfileRepository.GetByFarmerAndFarmAsync(saveContext.PlayerName, saveContext.FarmName);
        System.Diagnostics.Debug.WriteLine($"[ProfileResolution] Step 3 playerName={saveContext.PlayerName} farmName={saveContext.FarmName}: {(matched is null ? "not found" : $"'{matched.ProfileName}'")}");
        if (matched is not null)
            return new PlayerProfileResolution(matched, null, "playerName + farmName");

        // 4. Globally active profile fallback (works for dashboard and SMAPI when no identity match).
        PlayerProfile? active = await this.playerProfileRepository.GetActiveAsync();
        System.Diagnostics.Debug.WriteLine($"[ProfileResolution] Step 4 globally active: {(active is null ? "not found" : $"'{active.ProfileName}'")}");
        if (active is not null)
            return new PlayerProfileResolution(active, null, "globally active profile");

        System.Diagnostics.Debug.WriteLine("[ProfileResolution] Step 5: no profile resolved.");
        return PlayerProfileResolution.None;
    }

    private static Character? ChooseProfileCharacter(
        IReadOnlyList<Character> instances,
        IReadOnlyList<CharacterSource> sources)
    {
        if (instances.Count == 0)
            return null;

        HashSet<string> baseSourceIds = sources
            .Where(source => source.SourceType.Equals("BaseDefinition", StringComparison.OrdinalIgnoreCase))
            .Select(source => source.SourceModId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return instances
            .OrderByDescending(instance => instance.IsActive)
            .ThenByDescending(instance => !string.IsNullOrWhiteSpace(instance.SourceModId) && baseSourceIds.Contains(instance.SourceModId))
            .ThenBy(instance => instance.IsExtension)
            .ThenBy(instance => instance.SourceModName)
            .FirstOrDefault();
    }

    private static async Task<IReadOnlyList<T>> GetMergedAsync<T>(
        IReadOnlyList<Character> instances,
        Func<Character, Task<IReadOnlyList<T>>> load)
    {
        List<T> results = new();
        foreach (Character instance in instances)
            results.AddRange(await load(instance));
        return results;
    }

    private sealed record PlayerProfileResolution(PlayerProfile? Profile, string? SaveLink, string MatchMethod)
    {
        public static PlayerProfileResolution None { get; } = new(null, null, "none");
    }

    private sealed record CachedContextPacket(DialogueContextPacket Packet, DateTime ExpiresAtUtc);
}

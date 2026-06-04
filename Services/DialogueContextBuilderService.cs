using LivingLoreDialogue.Models;
using LivingLoreDialogue.Repositories;

namespace LivingLoreDialogue.Services;

public sealed class DialogueContextBuilderService
{
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
        int maxRecentMemories)
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
    }

    public async Task<DialogueContextPacket> BuildAsync(DialogueContext context, string? relationshipContext, SaveFileContextSnapshot? saveContextOverride = null, long? playerProfileId = null)
    {
        CanonicalCharacter? canonical = await this.canonicalRepository.GetByNameOrAliasAsync(context.CharacterName);
        Character? directCharacter = await this.characterRepository.GetByNameAsync(canonical?.CanonicalName ?? context.CharacterName)
            ?? await this.characterRepository.GetByNameAsync(context.CharacterName);

        if (canonical is null && directCharacter?.CanonicalCharacterId is not null)
            canonical = (await this.canonicalRepository.GetAllAsync()).FirstOrDefault(item => item.Id == directCharacter.CanonicalCharacterId);

        long? canonicalId = canonical?.Id ?? directCharacter?.CanonicalCharacterId;
        IReadOnlyList<CharacterSource> characterSources = canonicalId is null
            ? Array.Empty<CharacterSource>()
            : await this.canonicalRepository.GetSourcesAsync(canonicalId.Value);
        IReadOnlyList<Character> canonicalInstances = canonicalId is null
            ? directCharacter is null ? Array.Empty<Character>() : new[] { directCharacter }
            : await this.characterRepository.GetForCanonicalAsync(canonicalId.Value);

        Character character = ChooseProfileCharacter(canonicalInstances, characterSources)
            ?? directCharacter
            ?? throw new InvalidOperationException($"No lore profile exists for character '{context.CharacterName}'.");

        IReadOnlyList<DialogueSource> dialogueSources = canonicalId is null
            ? Array.Empty<DialogueSource>()
            : await this.dialogueSourceRepository.GetForCanonicalAsync(canonicalId.Value, activeOnly: true, limit: 80);

        DialogueSourceSummary? summary = canonicalId is null
            ? null
            : await this.dialogueSourceRepository.GetSummaryAsync(canonicalId.Value);

        IReadOnlyList<GeneratedDialogueHistoryEntry> recentGeneratedDialogue =
            await this.dialogueHistoryRepository.GetForCharacterIdsAsync(canonicalInstances.Select(instance => instance.Id), 16);

        // Simulation/test mode can inject a save context (a saved scenario); otherwise build the live one.
        SaveFileContextSnapshot saveContext = saveContextOverride
            ?? await this.saveFileContextService.GetSnapshotAsync(context, relationshipContext);

        // Resolve the player profile by priority: explicit selection, then save-file link,
        // then the active/default profile. Missing profiles never fail generation.
        (PlayerProfile? playerProfile, string? playerSaveLink) = await this.ResolvePlayerProfileAsync(playerProfileId, saveContext);
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

        IReadOnlyList<DialogueSource> relevantDialogueSources =
            this.contextSelectionService.SelectRelevantDialogueSources(context, preliminaryLore, limit: 10);
        CharacterVoiceProfile voiceProfile =
            this.contextSelectionService.BuildVoiceProfile(dialogueSources, summary);

        DialogueLoreBundle lore = new()
        {
            Character = character,
            CanonicalCharacter = canonical,
            CharacterInstances = canonicalInstances,
            CharacterSources = characterSources,
            Relationships = await GetMergedAsync(canonicalInstances, item => this.relationshipRepository.GetForCharacterAsync(item.Id)),
            Events = preliminaryLore.Events,
            Memories = (await GetMergedAsync(canonicalInstances, item => this.memoryRepository.GetRecentForCharacterAsync(item.Id, this.maxRecentMemories)))
                .OrderByDescending(memory => memory.CreatedDate)
                .Take(this.maxRecentMemories)
                .ToArray(),
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
            PlayerProfileSaveLink = playerSaveLink
        };

        return new DialogueContextPacket
        {
            Scene = context,
            Lore = lore,
            DialogueSources = relevantDialogueSources,
            DialogueSummary = summary,
            SaveContext = lore.SaveContext
        };
    }

    private async Task<(PlayerProfile? Profile, string? SaveLink)> ResolvePlayerProfileAsync(long? playerProfileId, SaveFileContextSnapshot saveContext)
    {
        // 1. Explicit selection (Dialogue Test / Simulation dropdown).
        if (playerProfileId is long id)
            return (await this.playerProfileRepository.GetByIdAsync(id), null);

        // 2. Save-file link (auto-detected current save).
        if (!string.IsNullOrWhiteSpace(saveContext.SaveFileName))
        {
            PlayerProfile? linked = await this.playerProfileRepository.GetBySaveFileAsync(saveContext.SaveFileName!);
            if (linked is not null)
                return (linked, saveContext.SaveFileName);
        }

        // 3. Active/default profile, or none.
        return (await this.playerProfileRepository.GetActiveAsync(), null);
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
}

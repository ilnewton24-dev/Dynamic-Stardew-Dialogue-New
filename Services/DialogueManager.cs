using LivingLoreDialogue.Models;
using LivingLoreDialogue.Repositories;

namespace LivingLoreDialogue.Services;

public sealed class DialogueManager
{
    private static readonly HashSet<string> KnownEnvironmentNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Farm", "FarmHouse", "House", "Cabin", "Town", "Mountain", "Beach", "Mine", "Mines",
        "BusStop", "Forest", "SeedShop", "JoshHouse", "HaleyHouse", "SamHouse", "ManorHouse",
        "ScienceHouse", "AnimalShop", "Blacksmith", "FishShop", "Saloon", "Hospital", "Tent",
        "Backwoods", "Railroad", "Woods", "Sewer", "SkullCave", "WizardHouse", "Greenhouse",
        "Cellar", "Desert"
    };

    private readonly CharacterRepository characterRepository;
    private readonly RelationshipRepository relationshipRepository;
    private readonly EventRepository eventRepository;
    private readonly MemoryRepository memoryRepository;
    private readonly VoiceRuleRepository voiceRuleRepository;
    private readonly UserLoreOverrideRepository userLoreOverrideRepository;
    private readonly LoreChangeLogRepository loreChangeLogRepository;
    private readonly OpenAiDialogueService openAiDialogueService;
    private readonly int maxRecentMemories;
    private readonly TimeSpan cacheDuration;
    private readonly Dictionary<string, CacheEntry> cache = new();

    public DialogueManager(
        CharacterRepository characterRepository,
        RelationshipRepository relationshipRepository,
        EventRepository eventRepository,
        MemoryRepository memoryRepository,
        VoiceRuleRepository voiceRuleRepository,
        UserLoreOverrideRepository userLoreOverrideRepository,
        LoreChangeLogRepository loreChangeLogRepository,
        OpenAiDialogueService openAiDialogueService,
        int maxRecentMemories,
        TimeSpan cacheDuration)
    {
        this.characterRepository = characterRepository;
        this.relationshipRepository = relationshipRepository;
        this.eventRepository = eventRepository;
        this.memoryRepository = memoryRepository;
        this.voiceRuleRepository = voiceRuleRepository;
        this.userLoreOverrideRepository = userLoreOverrideRepository;
        this.loreChangeLogRepository = loreChangeLogRepository;
        this.openAiDialogueService = openAiDialogueService;
        this.maxRecentMemories = maxRecentMemories;
        this.cacheDuration = cacheDuration;
    }

    public async Task<GeneratedDialogue> GenerateAsync(DialogueContext context)
    {
        if (string.IsNullOrWhiteSpace(context.CharacterName))
            throw new InvalidOperationException("Rejected generation: characterName is null or empty.");
        if (KnownEnvironmentNames.Contains(context.CharacterName.Trim()))
            throw new InvalidOperationException($"Rejected generation: characterName '{context.CharacterName}' is a known location/building/map, not a character.");

        string cacheKey = BuildCacheKey(context);
        if (this.cache.TryGetValue(cacheKey, out CacheEntry? cached) && cached.ExpiresAtUtc > DateTime.UtcNow)
            return cached.Dialogue;

        Character character = await this.characterRepository.GetByNameAsync(context.CharacterName)
            ?? throw new InvalidOperationException($"No lore profile exists for character '{context.CharacterName}'.");

        DialogueLoreBundle lore = new()
        {
            Character = character,
            Relationships = await this.relationshipRepository.GetForCharacterAsync(character.Id),
            Events = await this.eventRepository.GetRecentAsync(),
            Memories = await this.memoryRepository.GetRecentForCharacterAsync(character.Id, this.maxRecentMemories),
            VoiceRules = await this.voiceRuleRepository.GetForCharacterAsync(character.Id),
            UserOverrides = await this.userLoreOverrideRepository.GetForCharacterAsync(character.Id),
            RecentChanges = await this.loreChangeLogRepository.GetRecentForCharacterAsync(character.Id, 8)
        };

        GeneratedDialogue dialogue = await this.openAiDialogueService.GenerateDialogueAsync(context, lore);
        await this.memoryRepository.AddAsync(character.Id, $"Conversation about {dialogue.Topic}: {dialogue.Dialogue}", 2);

        this.cache[cacheKey] = new CacheEntry(dialogue, DateTime.UtcNow.Add(this.cacheDuration));
        return dialogue;
    }

    public async Task RecordGiftMemoryAsync(string characterName, string giftName, bool liked)
    {
        Character? character = await this.characterRepository.GetByNameAsync(characterName);
        if (character is null)
            return;

        string reaction = liked ? "appreciated" : "did not seem fond of";
        await this.memoryRepository.AddAsync(character.Id, $"{character.Name} {reaction} receiving {giftName} from the farmer.", 3);
    }

    public async Task RecordMarriageMemoryAsync(string characterName, string spouseName)
    {
        Character? character = await this.characterRepository.GetByNameAsync(characterName);
        if (character is null)
            return;

        await this.memoryRepository.AddAsync(character.Id, $"{character.Name} remembers that {spouseName} and the farmer are married.", 4);
    }

    public async Task RecordMajorEventMemoryAsync(string characterName, string eventText)
    {
        Character? character = await this.characterRepository.GetByNameAsync(characterName);
        if (character is null)
            return;

        await this.memoryRepository.AddAsync(character.Id, eventText, 5);
    }

    private static string BuildCacheKey(DialogueContext context)
    {
        return string.Join('|',
            context.CharacterName.Trim().ToLowerInvariant(),
            context.Topic.Trim().ToLowerInvariant(),
            context.Season,
            context.Weather,
            context.Location,
            context.FriendshipLevel);
    }

    private sealed record CacheEntry(GeneratedDialogue Dialogue, DateTime ExpiresAtUtc);
}

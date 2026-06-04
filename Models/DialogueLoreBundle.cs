namespace LivingLoreDialogue.Models;

public sealed class DialogueLoreBundle
{
    public Character Character { get; init; } = new();
    public CanonicalCharacter? CanonicalCharacter { get; init; }
    public IReadOnlyList<Character> CharacterInstances { get; init; } = Array.Empty<Character>();
    public IReadOnlyList<CharacterSource> CharacterSources { get; init; } = Array.Empty<CharacterSource>();
    public IReadOnlyList<DialogueSource> DialogueSources { get; init; } = Array.Empty<DialogueSource>();
    public IReadOnlyList<DialogueSource> RelevantDialogueSources { get; init; } = Array.Empty<DialogueSource>();
    public DialogueSourceSummary? DialogueSummary { get; init; }
    public CharacterVoiceProfile VoiceProfile { get; init; } = new();
    public SaveFileContextSnapshot SaveContext { get; init; } = new();
    public IReadOnlyList<Relationship> Relationships { get; init; } = Array.Empty<Relationship>();
    public IReadOnlyList<LoreEvent> Events { get; init; } = Array.Empty<LoreEvent>();
    public IReadOnlyList<Memory> Memories { get; init; } = Array.Empty<Memory>();
    public IReadOnlyList<VoiceRule> VoiceRules { get; init; } = Array.Empty<VoiceRule>();
    public IReadOnlyList<UserLoreOverride> UserOverrides { get; init; } = Array.Empty<UserLoreOverride>();
    public IReadOnlyList<LoreChangeLogEntry> RecentChanges { get; init; } = Array.Empty<LoreChangeLogEntry>();
    public IReadOnlyList<GeneratedDialogueHistoryEntry> RecentGeneratedDialogue { get; init; } = Array.Empty<GeneratedDialogueHistoryEntry>();

    // Player lore (selected farmer profile for this save/scenario), if any.
    public PlayerProfile? PlayerProfile { get; init; }
    public IReadOnlyList<PlayerProfileRelationship> PlayerRelationships { get; init; } = Array.Empty<PlayerProfileRelationship>();
    public IReadOnlyList<PlayerProfileMemory> PlayerMemories { get; init; } = Array.Empty<PlayerProfileMemory>();
    public string? PlayerProfileSaveLink { get; init; }
    public string PlayerProfileMatchMethod { get; init; } = "none";
}

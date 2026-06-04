namespace LivingLoreDialogue.Models;

/// <summary>
/// A complete record of the inputs used to generate one dialogue line, so the dashboard can
/// explain WHY a line was produced. The collection fields are stored as JSON strings.
/// </summary>
public sealed class DialogueGenerationTrace
{
    public long Id { get; set; }
    public long GeneratedDialogueId { get; set; }
    public DateTime GeneratedAt { get; set; }
    public long CharacterId { get; set; }
    public string InterceptedNpcName { get; set; } = "";
    public string CharacterName { get; set; } = "";
    public string ResolvedCharacterName { get; set; } = "";
    public string LocationName { get; set; } = "";
    public string InternalLocationId { get; set; } = "";
    public string DisplayLocationName { get; set; } = "";

    /// <summary>Serialized <see cref="SaveFileContextSnapshot"/>.</summary>
    public string SaveContextSnapshot { get; set; } = "{}";

    /// <summary>JSON array of the memories selected for the prompt.</summary>
    public string MemoriesUsed { get; set; } = "[]";

    /// <summary>JSON array of the relationships selected for the prompt.</summary>
    public string RelationshipsUsed { get; set; } = "[]";

    /// <summary>JSON array of the user lore overrides applied.</summary>
    public string UserOverridesUsed { get; set; } = "[]";

    /// <summary>JSON array of the existing dialogue sources used (file, mod, key, text).</summary>
    public string DialogueSourcesUsed { get; set; } = "[]";

    /// <summary>JSON array of the character's source mods.</summary>
    public string SourceModsUsed { get; set; } = "[]";

    /// <summary>JSON object of the player profile used (or null literal if none).</summary>
    public string PlayerProfileUsed { get; set; } = "null";

    /// <summary>JSON array of the player relationship notes used for the target character.</summary>
    public string PlayerRelationshipNotesUsed { get; set; } = "[]";

    /// <summary>JSON array of the player memories used for the target character.</summary>
    public string PlayerMemoriesUsed { get; set; } = "[]";

    /// <summary>The save-file link used to resolve the player profile, if any.</summary>
    public string? SaveFileLinkUsed { get; set; }

    /// <summary>How the active player profile was selected for this generation.</summary>
    public string PlayerProfileMatchMethod { get; set; } = "none";

    public string PromptVersion { get; set; } = "";
    public string PromptText { get; set; } = "";
    public string ModelUsed { get; set; } = "";

    /// <summary>Where this request originated ("SMAPI-Harmony", "SMAPI-Dialogue", "Dashboard", etc.).</summary>
    public string RequestSource { get; set; } = "";
}

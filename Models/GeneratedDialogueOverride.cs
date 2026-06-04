namespace LivingLoreDialogue.Models;

public sealed class GeneratedDialogueOverride
{
    public long Id { get; set; }
    public long CanonicalCharacterId { get; set; }
    public string DialogueKey { get; set; } = "";
    public long? OriginalDialogueSourceId { get; set; }
    public string GeneratedText { get; set; } = "";
    public string PromptUsed { get; set; } = "";
    public string SaveContextSnapshot { get; set; } = "";
    public bool IsEnabled { get; set; }
    public bool IsApproved { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

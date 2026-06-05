namespace LivingLoreDialogue.Models;

public sealed class DialogueContextPacket
{
    public DialogueContext Scene { get; set; } = new();
    public DialogueLoreBundle Lore { get; set; } = new();
    public SaveFileContextSnapshot SaveContext { get; set; } = new();
    public IReadOnlyList<ScoredDialogueSource> DialogueSources { get; set; } = Array.Empty<ScoredDialogueSource>();
    public DialogueSourceSummary? DialogueSummary { get; set; }
}

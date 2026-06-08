namespace LivingLoreDialogue.Models;

public sealed class BranchingDialogueRequest
{
    public DialogueContext Context { get; set; } = new();
    public SaveFileContextSnapshot? SaveContext { get; set; }
    public long? PlayerProfileId { get; set; }
    public string RelationshipContext { get; set; } = "";
    public string Mode { get; set; } = "opening_options";
    public int TurnCount { get; set; }
    public int MaxTurnCount { get; set; } = 10;
    public string SelectedOptionId { get; set; } = "";
    public string SelectedOptionText { get; set; } = "";
    public IReadOnlyList<BranchingDialogueTurn> History { get; set; } = Array.Empty<BranchingDialogueTurn>();
}

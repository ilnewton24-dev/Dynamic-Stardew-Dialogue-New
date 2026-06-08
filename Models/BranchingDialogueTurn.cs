namespace LivingLoreDialogue.Models;

public sealed class BranchingDialogueTurn
{
    public string PlayerChoiceId { get; set; } = "";
    public string PlayerChoiceText { get; set; } = "";
    public string NpcResponse { get; set; } = "";
}

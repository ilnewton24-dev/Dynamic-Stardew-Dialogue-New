namespace LivingLoreDialogue.Models;

public sealed class BranchingDialogueSession
{
    public string SessionId { get; set; } = Guid.NewGuid().ToString("N");
    public string NpcName { get; set; } = "";
    public string NpcDisplayName { get; set; } = "";
    public string PlayerName { get; set; } = "";
    public string PlayerProfileName { get; set; } = "";
    public string PlayerProfileSummary { get; set; } = "";
    public string PlayerProfileMatchMethod { get; set; } = "none";
    public SaveFileContextSnapshot SaveContext { get; set; } = new();
    public int TurnCount { get; set; }
    public int MaxTurnCount { get; set; } = 10;
    public bool IsActive { get; set; } = true;
    public bool IsEnded { get; set; }
    public List<BranchingDialogueTurn> Turns { get; set; } = new();
}

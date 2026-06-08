namespace LivingLoreDialogue.Models;

public sealed class BranchingDialogueResponse
{
    public string NpcResponse { get; set; } = "";
    public IReadOnlyList<PlayerDialogueOption> PlayerOptions { get; set; } = Array.Empty<PlayerDialogueOption>();
    public bool ConversationShouldEnd { get; set; }
    public string Error { get; set; } = "";
    public string PromptUsed { get; set; } = "";
    public string ActivePlayerProfileName { get; set; } = "";
    public string PlayerProfileMatchMethod { get; set; } = "none";
    public SaveFileContextSnapshot? SaveContext { get; set; }
}

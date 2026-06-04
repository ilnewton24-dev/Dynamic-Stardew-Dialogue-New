namespace LivingLoreDialogue.Models;

public sealed class GeneratedDialogueResult
{
    public GeneratedDialogue Dialogue { get; set; } = new();
    public string Prompt { get; set; } = "";
    public SaveFileContextSnapshot SaveContext { get; set; } = new();
    public string PromptUsed { get; set; } = "";
    public string ReturnedDialogue { get; set; } = "";
    public string? Error { get; set; }
    public DialogueQualityScores QualityScores { get; set; } = new();
    public long HistoryId { get; set; }
}

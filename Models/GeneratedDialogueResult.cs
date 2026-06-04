namespace LivingLoreDialogue.Models;

public sealed class GeneratedDialogueResult
{
    public string InterceptedNpcName { get; set; } = "";
    public string CharacterName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string ResolvedCharacterName { get; set; } = "";
    public string LocationName { get; set; } = "";
    public string InternalLocationId { get; set; } = "";
    public string DisplayLocation { get; set; } = "";
    public string ActivePlayerProfileName { get; set; } = "";
    public string PlayerProfileMatchMethod { get; set; } = "none";
    public GeneratedDialogue Dialogue { get; set; } = new();
    public string Prompt { get; set; } = "";
    public SaveFileContextSnapshot SaveContext { get; set; } = new();
    public string PromptUsed { get; set; } = "";
    public string ReturnedDialogue { get; set; } = "";
    public string? Error { get; set; }
    public DialogueQualityScores QualityScores { get; set; } = new();
    public long HistoryId { get; set; }
}

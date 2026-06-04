namespace LivingLoreDialogue.Models;

public sealed class VoiceRule
{
    public long Id { get; set; }
    public long CharacterId { get; set; }
    public string RuleText { get; set; } = "";
}

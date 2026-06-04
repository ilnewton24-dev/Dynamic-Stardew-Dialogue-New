namespace LivingLoreDialogue.Models;

public sealed class CanonicalMergeDecision
{
    public string Action { get; set; } = "";
    public long? CanonicalCharacterId { get; set; }
    public string? Alias { get; set; }
    public bool LockDecision { get; set; }
}

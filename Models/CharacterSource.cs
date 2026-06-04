namespace LivingLoreDialogue.Models;

public sealed class CharacterSource
{
    public long Id { get; set; }
    public long CanonicalCharacterId { get; set; }
    public string SourceModId { get; set; } = "";
    public string SourceType { get; set; } = "";
    public int Priority { get; set; }
    public string? Notes { get; set; }
}

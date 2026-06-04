namespace LivingLoreDialogue.Models;

public sealed class DialogueSource
{
    public long Id { get; set; }
    public long CanonicalCharacterId { get; set; }
    public string? SourceModId { get; set; }
    public string FilePath { get; set; } = "";
    public string? AssetName { get; set; }
    public string DialogueKey { get; set; } = "";
    public string RawText { get; set; } = "";
    public string? Conditions { get; set; }
    public string? Season { get; set; }
    public string? Weather { get; set; }
    public string? Location { get; set; }
    public int? HeartLevel { get; set; }
    public string? RelationshipState { get; set; }
    public int SourcePriority { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime LastSeen { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

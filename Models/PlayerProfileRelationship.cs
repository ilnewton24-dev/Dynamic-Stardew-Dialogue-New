namespace LivingLoreDialogue.Models;

/// <summary>A player profile's relationship note toward a specific canonical character.</summary>
public sealed class PlayerProfileRelationship
{
    public long Id { get; set; }
    public long PlayerProfileId { get; set; }
    public long CanonicalCharacterId { get; set; }
    public string? CanonicalName { get; set; }
    public string RelationshipType { get; set; } = "";
    public string RelationshipDescription { get; set; } = "";
    public int RelationshipStrength { get; set; }
    public string CustomNotes { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

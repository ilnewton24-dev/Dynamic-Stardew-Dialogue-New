namespace LivingLoreDialogue.Models;

public sealed class Relationship
{
    public long Id { get; set; }
    public long CharacterA { get; set; }
    public long CharacterB { get; set; }
    public string RelationshipType { get; set; } = "";
    public int Strength { get; set; }
}

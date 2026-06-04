namespace LivingLoreDialogue.Models;

/// <summary>A set of character rows that share the same name (potential duplicates to merge).</summary>
public sealed class DuplicateCharacterGroup
{
    public string Name { get; set; } = "";
    public IReadOnlyList<DuplicateCharacterEntry> Characters { get; set; } = Array.Empty<DuplicateCharacterEntry>();
    public int Count => this.Characters.Count;
}

public sealed class DuplicateCharacterEntry
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public long? CanonicalCharacterId { get; set; }
    public string? SourceModId { get; set; }
    public string? SourceModName { get; set; }
    public bool IsActive { get; set; }
}

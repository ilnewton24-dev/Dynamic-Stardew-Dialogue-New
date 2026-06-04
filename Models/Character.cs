namespace LivingLoreDialogue.Models;

public sealed class Character
{
    public long Id { get; set; }
    public long? CanonicalCharacterId { get; set; }
    public string Name { get; set; } = "";
    public string? InternalName { get; set; }
    public string? DisplayName { get; set; }
    public string Description { get; set; } = "";
    public string Personality { get; set; } = "";
    public string Occupation { get; set; } = "";
    public string HomeLocation { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public bool IsVanilla { get; set; }
    public bool IsCustomNpc { get; set; }
    public bool IsExtension { get; set; }
    public DateTime? LastSeen { get; set; }
    public string? SourceModId { get; set; }
    public string? SourceModName { get; set; }
    public string? SourceModVersion { get; set; }
    public string? SourceModAuthor { get; set; }
    public string? CharacterFingerprint { get; set; }
    public DateTime? LastModified { get; set; }
    public string? RawModData { get; set; }
}

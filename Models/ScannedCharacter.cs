namespace LivingLoreDialogue.Models;

public sealed record class ScannedCharacter
{
    public long? CanonicalCharacterId { get; init; }
    public string Name { get; init; } = "";
    public string InternalName { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Description { get; init; } = "";
    public string Personality { get; init; } = "";
    public string Occupation { get; init; } = "";
    public string HomeLocation { get; init; } = "";
    public string SourceModId { get; init; } = "";
    public string SourceModName { get; init; } = "";
    public string SourceModVersion { get; init; } = "";
    public string SourceModAuthor { get; init; } = "";
    public string CharacterFingerprint { get; init; } = "";
    public bool IsVanilla { get; init; }
    public bool IsCustomNpc { get; init; } = true;
    public bool IsExtension { get; init; }
    public string RawModData { get; init; } = "";
    public DateTime LastSeen { get; init; }
}

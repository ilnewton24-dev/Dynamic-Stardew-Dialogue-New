namespace LivingLoreDialogue.Models;

/// <summary>
/// A character name discovered during a mod scan, together with the accumulated
/// evidence that it represents a real NPC. Produced by <c>ModScannerService</c>
/// and scored by <c>CharacterValidationService</c>.
/// </summary>
public sealed class CharacterCandidate
{
    public string Name { get; init; } = "";
    public string SourceModId { get; init; } = "";
    public string SourceModName { get; init; } = "";
    public string SourceModVersion { get; init; } = "";
    public string SourceModAuthor { get; init; } = "";
    public bool IsVanilla { get; init; }
    public CharacterEvidence Evidence { get; set; }
    public string RawModData { get; set; } = "";
    public string CharacterFingerprint { get; set; } = "";
    public DateTime LastSeen { get; init; }
}

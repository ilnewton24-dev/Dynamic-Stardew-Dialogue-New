namespace LivingLoreDialogue.Models;

public sealed class CharacterSource
{
    public long Id { get; set; }
    public long CanonicalCharacterId { get; set; }
    public string SourceModId { get; set; } = "";
    public string SourceType { get; set; } = "";
    public int Priority { get; set; }
    public string? Notes { get; set; }

    /// <summary>
    /// True when the source mod is currently active in the latest scan (or when no ScannedMods
    /// entry exists — vanilla/custom sources without a scan record are treated as always active).
    /// Populated by a LEFT JOIN against ScannedMods at load time; not stored in the CharacterSources table.
    /// </summary>
    public bool IsActive { get; set; } = true;
}

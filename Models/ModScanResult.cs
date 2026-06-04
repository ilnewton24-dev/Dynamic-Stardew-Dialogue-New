namespace LivingLoreDialogue.Models;

public sealed class ModScanResult
{
    public string ModsFolderPath { get; set; } = "";
    public DateTime StartedAt { get; set; }
    public DateTime CompletedAt { get; set; }
    public IReadOnlyList<ScannedMod> Mods { get; set; } = Array.Empty<ScannedMod>();
    public IReadOnlyList<ScannedCharacter> Characters { get; set; } = Array.Empty<ScannedCharacter>();
    public IReadOnlyList<CharacterCandidate> Candidates { get; set; } = Array.Empty<CharacterCandidate>();
    public int VanillaCharactersFound { get; set; }
    public int ModdedCharactersFound { get; set; }
    public int FilesInspected { get; set; }
    public IReadOnlyList<string> Errors { get; set; } = Array.Empty<string>();
}

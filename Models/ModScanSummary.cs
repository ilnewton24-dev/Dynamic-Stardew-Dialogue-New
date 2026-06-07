namespace LivingLoreDialogue.Models;

public sealed class ModScanSummary
{
    public bool Success { get; set; }
    public bool IsPartial { get; set; }
    public string TimedOutPhase { get; set; } = "";
    public string LastFileProcessed { get; set; } = "";
    public int FilesRemaining { get; set; }
    public bool DatabaseStatePartial { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime CompletedAt { get; set; }
    public int ModsScanned { get; set; }
    public int CharactersFound { get; set; }
    public int VanillaCharactersFound { get; set; }
    public int ModdedCharactersFound { get; set; }
    public int MergedCanonicalCharacters { get; set; }
    public int CharactersAdded { get; set; }
    public int CharactersUpdated { get; set; }
    public int CharactersReactivated { get; set; }
    public int CharactersMarkedInactive { get; set; }
    public int ConflictsFound { get; set; }
    public int TotalFilesQueued { get; set; }
    public int FilesScanned { get; set; }
    public int FilesSkippedFromCache { get; set; }
    public int FilesFailed { get; set; }
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Errors { get; set; } = Array.Empty<string>();
}

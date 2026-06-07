namespace LivingLoreDialogue.Models;

public sealed class ScanPhaseProgress
{
    public string Phase { get; init; } = "";
    public string Message { get; init; } = "";
    public TimeSpan Duration { get; init; }
    public TimeSpan Elapsed { get; init; }
    public int TotalFilesQueued { get; init; }
    public int FilesInspected { get; init; }
    public int FilesScanned { get; init; }
    public int FilesSkippedFromCache { get; init; }
    public int FilesFailed { get; init; }
    public int FilesRemaining { get; init; }
    public string LastFileProcessed { get; init; } = "";
    public bool TimedOut { get; init; }
    public bool DatabaseStatePartial { get; init; }
    public int CharactersFound { get; init; }
    public int DialogueFilesFound { get; init; }
    public int Warnings { get; init; }
    public int Errors { get; init; }
}

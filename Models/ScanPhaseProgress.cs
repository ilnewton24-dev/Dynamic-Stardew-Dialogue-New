namespace LivingLoreDialogue.Models;

public sealed class ScanPhaseProgress
{
    public string Phase { get; init; } = "";
    public string Message { get; init; } = "";
    public TimeSpan Duration { get; init; }
    public int FilesInspected { get; init; }
    public int CharactersFound { get; init; }
    public int DialogueFilesFound { get; init; }
    public int Warnings { get; init; }
    public int Errors { get; init; }
}

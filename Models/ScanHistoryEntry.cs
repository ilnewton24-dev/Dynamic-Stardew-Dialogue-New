namespace LivingLoreDialogue.Models;

public sealed class ScanHistoryEntry
{
    public long Id { get; set; }
    public string TriggerSource { get; set; } = "";
    public DateTime StartedAt { get; set; }
    public DateTime CompletedAt { get; set; }
    public bool Success { get; set; }
    public int ModsScanned { get; set; }
    public int CharactersFound { get; set; }
    public int CharactersAdded { get; set; }
    public int CharactersUpdated { get; set; }
    public int CharactersReactivated { get; set; }
    public int CharactersMarkedInactive { get; set; }
    public int ConflictsFound { get; set; }
    public string? ErrorMessage { get; set; }
}

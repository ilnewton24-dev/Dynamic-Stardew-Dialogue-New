namespace LivingLoreDialogue.Models;

public sealed class CharacterSyncSummary
{
    public int CharactersAdded { get; set; }
    public int CharactersUpdated { get; set; }
    public int CharactersReactivated { get; set; }
    public int CharactersMarkedInactive { get; set; }

    // Post-sync database totals (for scan diagnostics/logging).
    public int TotalCharactersInDatabase { get; set; }
    public int ActiveCharactersInDatabase { get; set; }
}

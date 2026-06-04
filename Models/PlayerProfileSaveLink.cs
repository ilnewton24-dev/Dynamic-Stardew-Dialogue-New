namespace LivingLoreDialogue.Models;

/// <summary>Links a player profile to a Stardew save file so the right profile is auto-selected.</summary>
public sealed class PlayerProfileSaveLink
{
    public long Id { get; set; }
    public long PlayerProfileId { get; set; }
    public string SaveFileName { get; set; } = "";
    public string? SaveFilePath { get; set; }
    public DateTime? LastSeen { get; set; }
    public bool IsDefaultForSave { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

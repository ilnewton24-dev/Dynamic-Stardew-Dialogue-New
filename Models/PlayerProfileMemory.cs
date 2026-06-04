namespace LivingLoreDialogue.Models;

/// <summary>
/// A memory tied to a player profile, optionally involving a specific canonical character
/// (CanonicalCharacterId = null means a general player memory).
/// </summary>
public sealed class PlayerProfileMemory
{
    public long Id { get; set; }
    public long PlayerProfileId { get; set; }
    public long? CanonicalCharacterId { get; set; }
    public string? CanonicalName { get; set; }
    public string MemoryText { get; set; } = "";
    public int Importance { get; set; } = 3;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

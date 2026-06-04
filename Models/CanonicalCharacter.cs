namespace LivingLoreDialogue.Models;

public sealed class CanonicalCharacter
{
    public long Id { get; set; }
    public string CanonicalName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int CanonPriority { get; set; }
    public bool UserLocked { get; set; }
}

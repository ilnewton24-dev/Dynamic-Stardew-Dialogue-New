namespace LivingLoreDialogue.Models;

public sealed class CharacterHistory
{
    public long Id { get; set; }
    public long CharacterId { get; set; }
    public DateTime Timestamp { get; set; }
    public string PreviousData { get; set; } = "";
    public string NewData { get; set; } = "";
    public string ChangeReason { get; set; } = "";
}
